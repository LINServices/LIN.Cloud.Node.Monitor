using System.Collections.Concurrent;
using System.Globalization;
using Docker.DotNet;
using Docker.DotNet.Models;
using LIN.Cloud.Node.Monitor.Common;
using LIN.Cloud.Node.Monitor.Dtos;
using LIN.Cloud.Node.Monitor.Exceptions;
using LIN.Cloud.Node.Monitor.Extensions;
using LIN.Cloud.Node.Monitor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LIN.Cloud.Node.Monitor.Demons;

internal class DockerLogService(IApiService api, IDockerClient docker, ILogger<DockerLogService> logger) : BackgroundService
{
    private const int ContainerPollInterval = 10_000;
    private const int StreamRetryDelay = 5_000;
    private const int InitialSinceOffset = 5;
    private const int CheckpointBackoffMs = 100;
    private const int StopTimeout = 5_000;
    private const int ReadBufferSize = 8192;

    private readonly ConcurrentDictionary<string, ContainerLogTask> _running = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ids = await ListContainerIdsAsync(stoppingToken);
                await StartContainerLogTasksAsync(ids, stoppingToken);
                await StopMissingContainersAsync(ids);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in container polling loop");
            }

            await Task.Delay(ContainerPollInterval, stoppingToken);
        }

        await StopAllContainersAsync();
    }

    private async Task<HashSet<string>> ListContainerIdsAsync(CancellationToken ct)
    {
        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = false }, ct);
        return containers.Select(c => c.ID).ToHashSet();
    }

    private async Task StartContainerLogTasksAsync(HashSet<string> ids, CancellationToken stoppingToken)
    {
        foreach (var id in ids.Where(i => !_running.ContainsKey(i)))
            await StartContainerLogTaskAsync(id, stoppingToken);
    }

    private async Task StartContainerLogTaskAsync(string containerId, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var task = StreamLogsForContainerAsync(containerId, cts.Token);

        if (_running.TryAdd(containerId, new ContainerLogTask(task, cts)))
            return;

        await cts.CancelAsync();
        cts.Dispose();
    }

    private async Task StopMissingContainersAsync(HashSet<string> currentIds)
    {
        var gone = _running.Keys.Where(i => !currentIds.Contains(i)).ToList();
        foreach (var id in gone)
            await StopContainerAsync(id);
    }

    private async Task StopContainerAsync(string containerId)
    {
        if (_running.TryRemove(containerId, out var logTask))
            await CleanupContainerTaskAsync(containerId, logTask);
    }

    private async Task StopAllContainersAsync()
    {
        var all = _running.ToArray();
        _running.Clear();
        foreach (var (id, logTask) in all)
            await CleanupContainerTaskAsync(id, logTask);
    }

    private async Task CleanupContainerTaskAsync(string containerId, ContainerLogTask logTask)
    {
        try
        {
            await logTask.Cts.CancelAsync();
            await Task.WhenAny(logTask.Task, Task.Delay(StopTimeout));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error stopping task for container {ContainerId}", containerId);
        }
        finally
        {
            logTask.Cts.Dispose();
        }
    }

    private async Task StreamLogsForContainerAsync(string containerId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var containerExists = await StreamUntilEndAsync(containerId, ct);
                if (!containerExists)
                    return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (LogIngestException)
            {
                // No se avanzó el checkpoint: al reconectar se relee desde la última línea
                // indexada (el id determinista evita duplicados en el servidor).
                logger.LogWarning("Log ingestion failed for container {ContainerId}, reconnecting from checkpoint in {Delay}s", containerId, StreamRetryDelay / 1000);
                await Task.Delay(StreamRetryDelay, ct);
            }
            catch (DockerContainerNotFoundException)
            {
                logger.LogInformation("Container {ContainerId} no longer exists, stopping stream", containerId);
                return;
            }
            catch (DockerApiException ex)
            {
                logger.LogWarning(ex, "Docker API error for container {ContainerId}, retrying in {Delay}s", containerId, StreamRetryDelay / 1000);
                await Task.Delay(StreamRetryDelay, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unexpected error for container {ContainerId}, retrying", containerId);
                await Task.Delay(StreamRetryDelay, ct);
            }
        }
    }

    // Devuelve false si el contenedor ya no existe y el stream no debe reintentarse.
    private async Task<bool> StreamUntilEndAsync(string containerId, CancellationToken ct)
    {
        var since = await ResolveSinceAsync(containerId, ct);
        var inspect = await InspectContainerAsync(containerId, ct);

        if (inspect is null)
            return false;

        using var logStream = await docker.GetLogStreamAsync(containerId, inspect.Config.Tty, since, ct);
        await ReadLogStreamAsync(containerId, logStream, ct);

        if (ct.IsCancellationRequested)
            return true;

        logger.LogDebug("Log stream ended for container {ContainerId}, retrying in {Delay}s", containerId, StreamRetryDelay / 1000);
        await Task.Delay(StreamRetryDelay, ct);
        return true;
    }

    private async Task ReadLogStreamAsync(string containerId, MultiplexedStream logStream, CancellationToken ct)
    {
        var buffer = new byte[ReadBufferSize];

        // Un splitter por stream: el Decoder conserva los bytes de un carácter UTF-8
        // partido entre lecturas, y stdout/stderr no se mezclan en la misma línea.
        var stdout = new LineSplitter(buffer.Length);
        var stderr = new LineSplitter(buffer.Length);

        while (true)
        {
            var result = await logStream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
            if (result.EOF)
                return;

            var splitter = result.Target == MultiplexedStream.TargetStream.StandardError ? stderr : stdout;
            await ProcessLinesAsync(containerId, splitter.Push(buffer, result.Count), ct);
        }
    }

    private async Task ProcessLinesAsync(string containerId, List<string> lines, CancellationToken ct)
    {
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            await ProcessLineAsync(containerId, line, ct);
    }

    private async Task ProcessLineAsync(string containerId, string line, CancellationToken ct)
    {
        if (!TryParseLogLine(line, out var timestamp, out var message))
        {
            timestamp = DateTime.UtcNow;
            message = line;
        }

        var entry = new LogEntry(containerId, timestamp, message);

        // Solo se avanza el checkpoint si la línea quedó indexada; si no, se corta el stream
        // para reconectar desde el último checkpoint en lugar de perder la línea.
        if (!await api.IndexLogAsync(entry, ct))
            throw new LogIngestException();

        await api.UpdateCheckpointAsync(containerId, timestamp, ct);
    }

    private async Task<DateTime> ResolveSinceAsync(string containerId, CancellationToken ct)
    {
        // Si la API falla aquí, la excepción se propaga y el stream se reintenta: usar
        // "ahora - 5 s" ante un error de red saltaría los logs del periodo caído.
        var checkpoint = await api.GetCheckpointAsync(containerId, ct);
        return checkpoint.HasValue ? checkpoint.Value.AddMilliseconds(-CheckpointBackoffMs) : DateTime.UtcNow.AddSeconds(-InitialSinceOffset);
    }

    private async Task<ContainerInspectResponse?> InspectContainerAsync(string containerId, CancellationToken ct)
    {
        try
        {
            return await docker.Containers.InspectContainerAsync(containerId, ct);
        }
        catch (DockerContainerNotFoundException)
        {
            return null;
        }
        catch (DockerApiException ex)
        {
            logger.LogWarning(ex, "Docker API error inspecting container {ContainerId}", containerId);
            throw;
        }
    }

    private static bool TryParseLogLine(string line, out DateTime timestamp, out string message)
    {
        timestamp = default;
        message = string.Empty;

        var spaceIndex = line.IndexOf(' ');
        if (spaceIndex <= 0)
            return false;

        message = line[(spaceIndex + 1)..];
        if (!DateTime.TryParse(line[..spaceIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
            return false;

        timestamp = timestamp.ToUtc();
        return true;
    }
}
