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

    /// <summary>
    /// Envía una línea de log. Devuelve <c>false</c> si no se pudo indexar, para que quien
    /// llama no avance el checkpoint y la línea se vuelva a leer al reconectar.
    /// </summary>
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
            var response = await http.PostAsJsonAsync($"/api/checkpoints/{containerId}", payload, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update checkpoint for container {ContainerId}", containerId);
        }
    }

    /// <summary>
    /// Devuelve el último checkpoint (en UTC) o <c>null</c> si el contenedor no tiene ninguno (404).
    /// Cualquier otro fallo (red, 5xx, JSON inválido) se propaga: tratarlo como "sin checkpoint"
    /// haría que el stream arrancase desde "ahora" y se perdieran los logs intermedios.
    /// </summary>
    public async Task<DateTime?> GetCheckpointAsync(string containerId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/api/checkpoints/{containerId}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("lastTimestamp", out var prop) ||
            prop.ValueKind == JsonValueKind.Null)
            return null;

        return ToUtc(prop.GetDateTime());
    }

    /// <summary>
    /// Normaliza a UTC. Un valor sin zona (Kind = Unspecified) se asume UTC, no hora local:
    /// así el resultado no depende de la zona horaria del servidor.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc   => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    static string ComputeId(string containerId, long ticks, string message)
    {
        var input = $"{containerId}{ticks}{message}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
