using LIN.Cloud.Node.Monitor.Demons;
using LIN.Cloud.Node.Monitor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Integra el host con systemd: notifica el arranque (Type=notify) y envía latidos.
// En Windows o sin systemd es un no-op, así que se deja siempre activado.
builder.Services.AddSystemd();

var apiUri = Environment.GetEnvironmentVariable("API_URI") ?? "http://datalake.linplatform.com:5007";
var apiKey = Environment.GetEnvironmentVariable("API_KEY");

builder.Services.AddHttpClient<ApiService>(client =>
{
    client.BaseAddress = new Uri(apiUri);
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

builder.Services.AddHostedService<DockerLogService>();
builder.Services.AddHostedService<ContainerMetricsService>();

var host = builder.Build();
await host.RunAsync();