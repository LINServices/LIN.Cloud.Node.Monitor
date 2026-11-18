using Docker.DotNet;
using Docker.DotNet.Models;
using LIN.Cloud.Node.Monitor.Common;
using LIN.Cloud.Node.Monitor.Dtos;
using LIN.Cloud.Node.Monitor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LIN.Cloud.Node.Monitor.Demons;

internal class ContainerMetricsService(IApiService api, IDockerClient docker, ILogger<ContainerMetricsService> logger) : BackgroundService
{
    private const int PollInterval = 10_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectMetricsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in metrics polling loop: {Message}", ex.Message);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task CollectMetricsAsync(CancellationToken ct)
    {
        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = false }, ct);

        if (containers.Count == 0)
        {
            logger.LogDebug("No running containers found");
            return;
        }

        foreach (var container in containers)
        {
            if (ct.IsCancellationRequested)
                break;

            await CollectContainerMetricsAsync(container, ct);
        }
    }

    private async Task CollectContainerMetricsAsync(ContainerListResponse container, CancellationToken ct)
    {
        try
        {
            ContainerStatsResponse? snapshot = null;
            // Progress<T> invoca el callback de forma asíncrona (ThreadPool), así que al volver
            // de GetContainerStatsAsync el snapshot podría no estar asignado todavía.
            var progress = new SyncProgress<ContainerStatsResponse>(s => snapshot = s);

            await docker.Containers.GetContainerStatsAsync(container.ID, new ContainerStatsParameters { Stream = false }, progress, ct);

            if (snapshot is null)
                return;

            var name = container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12];
            var entry = new ContainerMetricEntry(container.ID, name, DateTime.UtcNow, snapshot.MemoryStats.Usage / 1024, snapshot.MemoryStats.Limit / 1024, snapshot.CPUStats.CPUUsage.TotalUsage, snapshot.CPUStats.SystemUsage);

            await api.IndexContainerMetricAsync(entry, ct);
        }
        catch (DockerApiException ex)
        {
            logger.LogWarning("Docker API error for container {ShortId}: {StatusCode} {Body}", container.ID[..12], ex.StatusCode, ex.ResponseBody);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to collect metrics for container {ShortId}: {Message}", container.ID[..12], ex.Message);
        }
    }
}
