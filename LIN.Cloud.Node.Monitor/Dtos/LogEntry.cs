namespace LIN.Cloud.Node.Monitor.Dtos;

public record LogEntry(string ContainerId, DateTime Timestamp, string Message);
