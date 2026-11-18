using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace LIN.Cloud.Node.Monitor.Extensions;

public static class DockerClientExtensions
{
    /// <summary>
    /// Abre el stream de logs del contenedor desde <paramref name="since"/>, probando parámetros cada vez más simples si el daemon rechaza los anteriores con 400.
    /// </summary>
    public static async Task<MultiplexedStream> GetLogStreamAsync(this IDockerClient docker, string containerId, bool tty, DateTime since, CancellationToken ct)
    {
        // new DateTimeOffset(DateTime) trata Kind = Unspecified como hora local: se normaliza antes
        var sinceUnix = new DateTimeOffset(since.ToUtc()).ToUnixTimeSeconds().ToString();

        ContainerLogsParameters[] strategies =
        [
            new() { Follow = true, ShowStdout = true, ShowStderr = true, Since = sinceUnix, Timestamps = true },
            new() { Follow = true, ShowStdout = true, ShowStderr = true, Since = "0", Timestamps = true },
            new() { Follow = true, ShowStdout = true, ShowStderr = true, Timestamps = true },
            new() { Follow = true, ShowStdout = true, ShowStderr = true, Since = "0", Timestamps = false }
        ];

        foreach (var parameters in strategies)
        {
            try
            {
                return await docker.Containers.GetContainerLogsAsync(containerId, tty, parameters, ct);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                // El daemon no admite esta combinación de parámetros: se prueba la siguiente estrategia
                continue;
            }
        }

        throw new InvalidOperationException($"All log stream strategies exhausted for container {containerId}");
    }
}
