using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LIN.Cloud.Node.Monitor.Models;
using Microsoft.Extensions.Logging;

namespace LIN.Cloud.Node.Monitor.Services;

class ApiService(HttpClient http, ILogger<ApiService> logger)
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task IndexContainerMetricAsync(ContainerMetricEntry entry, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/metrics", entry, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to index metric for container {ContainerId}", entry.ContainerId);
        }
    }

    public async Task IndexLogAsync(LogEntry entry, CancellationToken ct)
    {
        var id = ComputeId(entry.ContainerId, entry.Timestamp.Ticks, entry.Message);
        var payload = new { id, entry.ContainerId, timestamp = entry.Timestamp, entry.Message };

        try
        {
            var response = await http.PostAsJsonAsync("/api/logs", payload, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to index log for container {ContainerId}", entry.ContainerId);
        }
    }

    public async Task UpdateCheckpointAsync(string containerId, DateTime lastTimestamp, CancellationToken ct)
    {
        var payload = new { containerId, lastTimestamp };

        try
        {
            var response = await http.PostAsJsonAsync($"/api/checkpoints/{containerId}", payload, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update checkpoint for container {ContainerId}", containerId);
        }
    }

    public async Task<DateTime?> GetCheckpointAsync(string containerId, CancellationToken ct)
    {
        try
        {
            var response = await http.GetAsync($"/api/checkpoints/{containerId}", ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            return doc.RootElement.TryGetProperty("lastTimestamp", out var prop)
                ? prop.GetDateTime()
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get checkpoint for container {ContainerId}", containerId);
            return null;
        }
    }

    static string ComputeId(string containerId, long ticks, string message)
    {
        var input = $"{containerId}{ticks}{message}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
