namespace LIN.Cloud.Node.Monitor.Dtos;

internal record struct ContainerLogTask(Task Task, CancellationTokenSource Cts);
