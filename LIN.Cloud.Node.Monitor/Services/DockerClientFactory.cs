using Docker.DotNet;

namespace LIN.Cloud.Node.Monitor.Services;

static class DockerClientFactory
{
    public static DockerClient Create()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrEmpty(dockerHost))
            return TryCreate(new Uri(dockerHost));

        var platformSocket = OperatingSystem.IsWindows()
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");

        try { return TryCreate(platformSocket); }
        catch { }

        return TryCreate(new Uri("http://localhost:2375"));
    }

    static DockerClient TryCreate(Uri uri)
    {
        var client = new DockerClientConfiguration(uri).CreateClient();
        client.System.GetVersionAsync().GetAwaiter().GetResult();
        return client;
    }
}
