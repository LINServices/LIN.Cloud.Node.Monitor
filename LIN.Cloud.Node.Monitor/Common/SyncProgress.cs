namespace LIN.Cloud.Node.Monitor.Common;

// IProgress que ejecuta el callback en el mismo hilo que llama a Report (Progress<T> lo encola en el ThreadPool).
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value)
    {
        handler(value);
    }
}
