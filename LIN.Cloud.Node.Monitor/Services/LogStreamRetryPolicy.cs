using Docker.DotNet;
using Docker.DotNet.Models;

namespace LIN.Cloud.Node.Monitor.Services;

static class LogStreamRetryPolicy
{
    public static async Task<MultiplexedStream> GetLogStreamAsync(
        DockerClient docker,
        string containerId,
        bool tty,
        DateTime since,
        CancellationToken ct)
    {
        var sinceUnix = new DateTimeOffset(since).ToUnixTimeSeconds().ToString();

        ContainerLogsParameters[] strategies =
        [
            new() { Follow = true, ShowStdout = true, ShowStderr = true, Since = sinceUnix, Timestamps = true },
            new() { Follow = true, ShowStdout = true, ShowStderr = true, Since = "0",       Timestamps = true },
            new() { Follow = true, ShowStdout = true, ShowStderr = true,                    Timestamps = true },
            new() { Follow = true, ShowStdout = true, ShowStderr = true, Since = "0",       Timestamps = false },
        ];

        foreach (var parameters in strategies)
        {
            try
            {
                return await docker.Containers.GetContainerLogsAsync(containerId, tty, parameters, ct);
            }
            catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // try next strategy
            }
        }

        throw new InvalidOperationException($"All log stream strategies exhausted for container {containerId}");
    }
}
