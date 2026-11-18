namespace LIN.Cloud.Node.Monitor.Dtos;

public record ContainerMetricEntry(string ContainerId, string ContainerName, DateTime Timestamp, ulong MemoryUsageKb, ulong MemoryLimitKb, ulong CpuUsageCurrent, ulong CpuUsageMax);
