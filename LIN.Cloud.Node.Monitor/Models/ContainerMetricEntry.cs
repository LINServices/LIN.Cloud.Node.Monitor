namespace LIN.Cloud.Node.Monitor.Models;

record ContainerMetricEntry(
    string   ContainerId,
    string   ContainerName,
    DateTime Timestamp,
    ulong    MemoryUsageKb,
    ulong    MemoryLimitKb,
    ulong    CpuUsageCurrent,
    ulong    CpuUsageMax);
