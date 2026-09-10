namespace Kadr.App.Editor;

public interface IEditorAction
{
    string Kind { get; }
    void Undo();
    void Redo();
    /// <summary>Попытаться слить со следующим действием того же рода (например, изменения толщины колесом).</summary>
    bool TryMerge(IEditorAction next);
}

public sealed class UndoManager
{
    private readonly Stack<IEditorAction> _undo = new();
    private readonly Stack<IEditorAction> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    public void Push(IEditorAction action)
    {
        _redo.Clear();
        if (_undo.Count > 0 && _undo.Peek().TryMerge(action)) { Changed?.Invoke(); return; }
        _undo.Push(action);
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var a = _undo.Pop();
        a.Undo();
        _redo.Push(a);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var a = _redo.Pop();
        a.Redo();
        _undo.Push(a);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}

/// <summary>Универсальное действие: снимок состояния объекта до и после.</summary>
public sealed class StateAction : IEditorAction
{
    private readonly Objects.VisualObject _target;
    private readonly object _before;
    private object _after;

    public string Kind { get; }

    public StateAction(string kind, Objects.VisualObject target, object before, object after)
    {
        Kind = kind;
        _target = target;
        _before = before;
        _after = after;
    }

    public void Undo() => _target.RestoreState(_before);
    public void Redo() => _target.RestoreState(_after);

    public bool TryMerge(IEditorAction next)
    {
        if (next is StateAction s && s.Kind == Kind && s._target == _target && Kind is "thickness" or "color")
        {
            _after = s._after;
            return true;
        }
        return false;
    }
}

public sealed class DelegateAction : IEditorAction
{
    private readonly Action _undo;
    private readonly Action _redo;
    public string Kind { get; }

    public DelegateAction(string kind, Action undo, Action redo)
    {
        Kind = kind;
        _undo = undo;
        _redo = redo;
    }

    public void Undo() => _undo();
    public void Redo() => _redo();
    public bool TryMerge(IEditorAction next) => false;
}
