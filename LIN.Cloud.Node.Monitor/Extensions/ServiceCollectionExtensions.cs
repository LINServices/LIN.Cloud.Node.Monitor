using System.Net.Sockets;
using Docker.DotNet;
using LIN.Cloud.Node.Monitor.Demons;
using LIN.Cloud.Node.Monitor.Services;
using LIN.Cloud.Node.Monitor.Services.Implementations;
using Microsoft.Extensions.DependencyInjection;

namespace LIN.Cloud.Node.Monitor.Extensions;

public static class ServiceCollectionExtensions
{
    private const string DefaultApiUri = "https://api.linplatform.com/airplane/telemetry/";

    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        var apiUri = Environment.GetEnvironmentVariable("API_URI");
        if (string.IsNullOrWhiteSpace(apiUri))
            apiUri = DefaultApiUri;

        // Las rutas del ApiService son relativas: la base debe acabar en "/" para conservar su prefijo.
        if (!apiUri.EndsWith('/'))
            apiUri += "/";
        var apiKey = Environment.GetEnvironmentVariable("API_KEY");

        services.AddHttpClient<IApiService, ApiService>(client =>
        {
            client.BaseAddress = new Uri(apiUri);
            if (!string.IsNullOrEmpty(apiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        });

        return services;
    }

    public static IServiceCollection AddDocker(this IServiceCollection services)
    {
        return services.AddSingleton<IDockerClient>(_ => CreateDockerClient());
    }

    public static IServiceCollection AddMonitors(this IServiceCollection services)
    {
        return services
            .AddHostedService<DockerLogService>()
            .AddHostedService<ContainerMetricsService>();
    }

    private static IDockerClient CreateDockerClient()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrEmpty(dockerHost))
            return ConnectDockerClient(new Uri(dockerHost));

        var platformSocket = OperatingSystem.IsWindows()
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");

        try
        {
            return ConnectDockerClient(platformSocket);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            // Sin socket local accesible se recurre al daemon expuesto por TCP
            return ConnectDockerClient(new Uri("http://localhost:2375"));
        }
    }

    private static IDockerClient ConnectDockerClient(Uri uri)
    {
        var client = new DockerClientConfiguration(uri).CreateClient();

        try
        {
            client.System.GetVersionAsync().GetAwaiter().GetResult();
            return client;
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            client.Dispose();
            throw;
        }
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        return exception is HttpRequestException or IOException or SocketException or TimeoutException or DockerApiException;
    }
}
