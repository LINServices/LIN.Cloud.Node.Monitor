using LIN.Cloud.Node.Monitor.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Integra el host con systemd: notifica el arranque (Type=notify) y envía latidos.
// En Windows o sin systemd es un no-op, así que se deja siempre activado.
builder.Services.AddSystemd();

builder.Services.AddApi().AddDocker().AddMonitors();

var host = builder.Build();
await host.RunAsync();
