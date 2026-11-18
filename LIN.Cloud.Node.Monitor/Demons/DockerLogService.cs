using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using LIN.Cloud.Node.Monitor.Models;
using LIN.Cloud.Node.Monitor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LIN.Cloud.Node.Monitor.Demons;

class DockerLogService(ApiService api, ILogger<DockerLogService> logger) : BackgroundService
{
    const int ContainerPollInterval = 10_000;
    const int StreamRetryDelay     = 5_000;
    const int InitialSinceOffset   = 5;
    const int CheckpointBackoffMs  = 100;

    record struct ContainerLogTask(Task Task, CancellationTokenSource Cts);

    readonly ConcurrentDictionary<string, ContainerLogTask> _running = new();
    readonly DockerClient _docker = DockerClientFactory.Create();

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

            await Task.Delay(ContainerPollInterval, stoppingToken).ConfigureAwait(false);
        }

        await StopAllContainersAsync();
    }

    async Task<HashSet<string>> ListContainerIdsAsync(CancellationToken ct)
    {
        var containers = await _docker.Containers.ListContainersAsync(
            new ContainersListParameters { All = false }, ct);
        return containers.Select(c => c.ID).ToHashSet();
    }

    async Task StartContainerLogTasksAsync(HashSet<string> ids, CancellationToken stoppingToken)
    {
        foreach (var id in ids)
        {
            if (_running.ContainsKey(id)) continue;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var task = StreamLogsForContainerAsync(id, cts.Token);

            if (!_running.TryAdd(id, new ContainerLogTask(task, cts)))
            {
                await cts.CancelAsync();
                cts.Dispose();
            }
        }
    }

    async Task StopMissingContainersAsync(HashSet<string> currentIds)
    {
        var gone = _running.Keys.Where(id => !currentIds.Contains(id)).ToList();
        foreach (var id in gone)
        {
            if (_running.TryRemove(id, out var t))
                await CleanupContainerTaskAsync(id, t);
        }
    }

    async Task StopAllContainersAsync()
    {
        var all = _running.ToArray();
        _running.Clear();
        foreach (var (id, t) in all)
            await CleanupContainerTaskAsync(id, t);
    }

    async Task CleanupContainerTaskAsync(string containerId, ContainerLogTask logTask)
    {
        try
        {
            await logTask.Cts.CancelAsync();
            await Task.WhenAny(logTask.Task, Task.Delay(5_000));
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

    async Task StreamLogsForContainerAsync(string containerId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var since   = await ResolveSinceAsync(containerId, ct);
                var inspect = await InspectContainerAsync(containerId, ct);

                if (inspect is null)
                    return;

                using var logStream = await LogStreamRetryPolicy.GetLogStreamAsync(
                    _docker, containerId, inspect.Config.Tty, since, ct);

                var buffer = new byte[8192];

                // Un splitter por stream: el Decoder conserva los bytes de un carácter UTF-8
                // partido entre lecturas, y stdout/stderr no se mezclan en la misma línea.
                var stdout = new LineSplitter(buffer.Length);
                var stderr = new LineSplitter(buffer.Length);

                while (true)
                {
                    var result = await logStream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
                    if (result.EOF) break;

                    var splitter = result.Target == MultiplexedStream.TargetStream.StandardError
                        ? stderr
                        : stdout;

                    foreach (var line in splitter.Push(buffer, result.Count))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            await ProcessLineAsync(containerId, line, ct);
                    }
                }

                if (!ct.IsCancellationRequested)
                {
                    logger.LogDebug("Log stream ended for container {ContainerId}, retrying in {Delay}s",
                        containerId, StreamRetryDelay / 1000);
                    await Task.Delay(StreamRetryDelay, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (LogIngestException)
            {
                // No se avanzó el checkpoint: al reconectar se relee desde la última línea
                // indexada (el id determinista evita duplicados en el servidor).
                logger.LogWarning("Log ingestion failed for container {ContainerId}, reconnecting from checkpoint in {Delay}s",
                    containerId, StreamRetryDelay / 1000);
                await Task.Delay(StreamRetryDelay, ct);
            }
            catch (DockerContainerNotFoundException)
            {
                logger.LogInformation("Container {ContainerId} no longer exists, stopping stream", containerId);
                return;
            }
            catch (DockerApiException ex)
            {
                logger.LogWarning(ex, "Docker API error for container {ContainerId}, retrying in {Delay}s",
                    containerId, StreamRetryDelay / 1000);
                await Task.Delay(StreamRetryDelay, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unexpected error for container {ContainerId}, retrying", containerId);
                await Task.Delay(StreamRetryDelay, ct);
            }
        }
    }

    async Task ProcessLineAsync(string containerId, string line, CancellationToken ct)
    {
        if (!TryParseLogLine(line, out var timestamp, out var message))
        {
            timestamp = DateTime.UtcNow;
            message   = line;
        }

        var entry = new LogEntry(containerId, timestamp, message);

        // Solo se avanza el checkpoint si la línea quedó indexada; si no, se corta el stream
        // para reconectar desde el último checkpoint en lugar de perder la línea.
        if (!await api.IndexLogAsync(entry, ct))
            throw new LogIngestException();

        await api.UpdateCheckpointAsync(containerId, timestamp, ct);
    }

    async Task<DateTime> ResolveSinceAsync(string containerId, CancellationToken ct)
    {
        // Si la API falla aquí, la excepción se propaga y el stream se reintenta: usar
        // "ahora - 5 s" ante un error de red saltaría los logs del periodo caído.
        var checkpoint = await api.GetCheckpointAsync(containerId, ct);
        return checkpoint.HasValue
            ? checkpoint.Value.AddMilliseconds(-CheckpointBackoffMs)
            : DateTime.UtcNow.AddSeconds(-InitialSinceOffset);
    }

    async Task<ContainerInspectResponse?> InspectContainerAsync(string containerId, CancellationToken ct)
    {
        try
        {
            return await _docker.Containers.InspectContainerAsync(containerId, ct);
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

    static bool TryParseLogLine(string line, out DateTime timestamp, out string message)
    {
        timestamp = default;
        message   = string.Empty;

        var spaceIndex = line.IndexOf(' ');
        if (spaceIndex <= 0) return false;

        message = line[(spaceIndex + 1)..];
        if (!DateTime.TryParse(line[..spaceIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
            return false;

        timestamp = ApiService.ToUtc(timestamp);
        return true;
    }

    sealed class LogIngestException() : Exception("Log line could not be indexed");

    /// <summary>
    /// Convierte bloques de bytes UTF-8 en líneas completas, manteniendo entre llamadas
    /// tanto los bytes de un carácter partido como el resto de una línea sin terminar.
    /// </summary>
    sealed class LineSplitter(int maxBytes)
    {
        readonly Decoder       _decoder = Encoding.UTF8.GetDecoder();
        readonly char[]        _chars   = new char[Encoding.UTF8.GetMaxCharCount(maxBytes)];
        readonly StringBuilder _pending = new();

        public List<string> Push(byte[] bytes, int count)
        {
            var charCount = _decoder.GetChars(bytes, 0, count, _chars, 0, flush: false);
            var lines     = new List<string>();
            var start     = 0;

            for (var i = 0; i < charCount; i++)
            {
                if (_chars[i] != '\n') continue;

                _pending.Append(_chars, start, i - start);
                lines.Add(_pending.ToString().TrimEnd('\r'));
                _pending.Clear();
                start = i + 1;
            }

            if (start < charCount)
                _pending.Append(_chars, start, charCount - start);

            return lines;
        }
    }
}