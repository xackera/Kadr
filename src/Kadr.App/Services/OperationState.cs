namespace Kadr.App.Services;

public enum OperationType { None, Screenshot, Video }

/// <summary>Одна операция за раз: пока идёт скриншот, прокрутка или запись, другие команды игнорируются.</summary>
public sealed class OperationState
{
    private readonly object _sync = new();

    public OperationType Current { get; private set; }
    public bool IsRecording { get; private set; }

    public event Action? Changed;

    public bool TryStart(OperationType type)
    {
        lock (_sync)
        {
            if (Current != OperationType.None) return false;
            Current = type;
        }
        Changed?.Invoke();
        return true;
    }

    public void End()
    {
        lock (_sync)
        {
            Current = OperationType.None;
            IsRecording = false;
        }
        Changed?.Invoke();
        // Снимки мониторов и рендер редактора занимают десятки мегабайт: освободить, не дожидаясь GC.
        _ = Task.Delay(1500).ContinueWith(_ =>
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        });
    }

    public void SetRecording(bool recording)
    {
        lock (_sync) IsRecording = recording;
        Changed?.Invoke();
    }
}
