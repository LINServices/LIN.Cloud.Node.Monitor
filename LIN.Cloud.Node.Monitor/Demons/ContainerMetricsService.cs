using Docker.DotNet;
using Docker.DotNet.Models;
using LIN.Cloud.Node.Monitor.Models;
using LIN.Cloud.Node.Monitor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LIN.Cloud.Node.Monitor.Demons;

class ContainerMetricsService(ApiService api, ILogger<ContainerMetricsService> logger) : BackgroundService
{
    const int PollInterval = 10_000;

    readonly DockerClient _docker = DockerClientFactory.Create();

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

            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    async Task CollectMetricsAsync(CancellationToken ct)
    {
        var containers = await _docker.Containers.ListContainersAsync(
            new ContainersListParameters { All = false }, ct);

        if (containers.Count == 0)
        {
            logger.LogDebug("No running containers found");
            return;
        }

        foreach (var container in containers)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                ContainerStatsResponse? snapshot = null;
                var progress = new Progress<ContainerStatsResponse>(s => snapshot = s);

                await _docker.Containers.GetContainerStatsAsync(
                    container.ID,
                    new ContainerStatsParameters { Stream = false },
                    progress,
                    ct);

                if (snapshot is null) continue;

                var name  = container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12];
                var entry = new ContainerMetricEntry(
                    container.ID,
                    name,
                    DateTime.UtcNow,
                    snapshot.MemoryStats.Usage / 1024,
                    snapshot.MemoryStats.Limit / 1024,
                    snapshot.CPUStats.CPUUsage.TotalUsage,
                    snapshot.CPUStats.SystemUsage);

                await api.IndexContainerMetricAsync(entry, ct);
            }
            catch (DockerApiException ex)
            {
                logger.LogWarning("Docker API error for container {ShortId}: {StatusCode} {Body}",
                    container.ID[..12], ex.StatusCode, ex.ResponseBody);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to collect metrics for container {ShortId}: {Message}",
                    container.ID[..12], ex.Message);
            }
        }
    }
}