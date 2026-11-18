using LIN.Cloud.Node.Monitor.Dtos;

namespace LIN.Cloud.Node.Monitor.Services;

public interface IApiService
{
    /// <summary>
    /// Envía una métrica de contenedor. Los fallos se registran y no se propagan.
    /// </summary>
    Task IndexContainerMetricAsync(ContainerMetricEntry entry, CancellationToken ct);

    /// <summary>
    /// Envía una línea de log. Devuelve false si no se pudo indexar, para que quien llama no avance el checkpoint y la línea se vuelva a leer al reconectar.
    /// </summary>
    Task<bool> IndexLogAsync(LogEntry entry, CancellationToken ct);

    /// <summary>
    /// Guarda el último timestamp procesado del contenedor. Los fallos se registran y no se propagan.
    /// </summary>
    Task UpdateCheckpointAsync(string containerId, DateTime lastTimestamp, CancellationToken ct);

    /// <summary>
    /// Devuelve el último checkpoint (en UTC) o null si el contenedor no tiene ninguno (404). Cualquier otro fallo (red, 5xx, JSON inválido) se propaga: tratarlo como "sin checkpoint" haría que el stream arrancase desde "ahora" y se perdieran los logs intermedios.
    /// </summary>
    Task<DateTime?> GetCheckpointAsync(string containerId, CancellationToken ct);
}
