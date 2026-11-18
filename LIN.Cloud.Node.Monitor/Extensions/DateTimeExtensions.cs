namespace LIN.Cloud.Node.Monitor.Extensions;

public static class DateTimeExtensions
{
    /// <summary>
    /// Normaliza a UTC. Un valor sin zona (Kind = Unspecified) se asume UTC, no hora local, para que el resultado no dependa de la zona horaria del servidor.
    /// </summary>
    public static DateTime ToUtc(this DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
