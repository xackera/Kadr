using System.IO;
using System.IO.Pipes;
using Kadr.Common.Ipc;

namespace Kadr.App.Startup;

/// <summary>Единственный экземпляр через mutex; второй экземпляр передаёт команду первому по named pipe.</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, Ipc.MainMutexName, out var createdNew);
        if (createdNew) return new SingleInstance(mutex);
        mutex.Dispose();
        return null;
    }

    public static bool SendCommand(string command, int timeoutMs = 5000)
    {
        if (string.IsNullOrEmpty(command)) return true;
        try
        {
            using var client = new NamedPipeClientStream(".", Ipc.MainPipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try { _mutex.ReleaseMutex(); } catch { /* уже освобождён */ }
        _mutex.Dispose();
    }
}
