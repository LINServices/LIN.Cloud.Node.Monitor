using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LIN.Cloud.Node.Monitor.Dtos;
using LIN.Cloud.Node.Monitor.Extensions;
using Microsoft.Extensions.Logging;

namespace LIN.Cloud.Node.Monitor.Services.Implementations;

internal class ApiService(HttpClient http, ILogger<ApiService> logger) : IApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task IndexContainerMetricAsync(ContainerMetricEntry entry, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/api/metrics", entry, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to index metric for container {ContainerId}", entry.ContainerId);
        }
    }

    public async Task<bool> IndexLogAsync(LogEntry entry, CancellationToken ct)
    {
        var id = ComputeId(entry.ContainerId, entry.Timestamp.Ticks, entry.Message);
        var payload = new { id, entry.ContainerId, timestamp = entry.Timestamp, entry.Message };

        try
        {
            using var response = await http.PostAsJsonAsync("/api/logs", payload, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to index log for container {ContainerId}", entry.ContainerId);
            return false;
        }
    }

    public async Task UpdateCheckpointAsync(string containerId, DateTime lastTimestamp, CancellationToken ct)
    {
        var payload = new { containerId, lastTimestamp };

        try
        {
            using var response = await http.PostAsJsonAsync($"/api/checkpoints/{containerId}", payload, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update checkpoint for container {ContainerId}", containerId);
        }
    }

    public async Task<DateTime?> GetCheckpointAsync(string containerId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/api/checkpoints/{containerId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!document.RootElement.TryGetProperty("lastTimestamp", out var lastTimestamp) || lastTimestamp.ValueKind == JsonValueKind.Null)
            return null;

        return lastTimestamp.GetDateTime().ToUtc();
    }

    private static string ComputeId(string containerId, long ticks, string message)
    {
        var input = $"{containerId}{ticks}{message}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
