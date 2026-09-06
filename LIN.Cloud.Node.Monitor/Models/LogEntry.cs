namespace LIN.Cloud.Node.Monitor.Models;

record LogEntry(string ContainerId, DateTime Timestamp, string Message);
