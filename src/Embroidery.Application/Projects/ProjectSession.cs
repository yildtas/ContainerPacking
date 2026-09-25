using Embroidery.Core.Model;

namespace Embroidery.Application.Projects;

public sealed class RevisionConflictException(long expected, long actual)
    : Exception($"Design changed (expected revision {expected}, current {actual}). Reload and retry.")
{
    public long Expected { get; } = expected;
    public long Actual { get; } = actual;
}

/// <summary>
/// One open design. The design is immutable, so undo/redo keep whole snapshots cheaply
/// (unchanged objects are shared). Every change, including undo and redo, increases the
/// revision, so clients can detect stale state.
/// </summary>
public sealed class ProjectSession
{
    private const int MaxHistory = 200;
    private readonly object _gate = new();
    private readonly LinkedList<Design> _undo = new();
    private readonly Stack<Design> _redo = new();

    public ProjectSession(Design design) => Current = design;

    public Design Current { get; private set; }
    public bool CanUndo { get { lock (_gate) return _undo.Count > 0; } }
    public bool CanRedo { get { lock (_gate) return _redo.Count > 0; } }

    /// <summary>Applies <paramref name="change"/> if the client saw the current revision.</summary>
    public Design Apply(long? expectedRevision, Func<Design, Design> change)
    {
        lock (_gate)
        {
            if (expectedRevision is { } expected && expected != Current.Revision)
            {
                throw new RevisionConflictException(expected, Current.Revision);
            }

            var next = change(Current);
            if (ReferenceEquals(next, Current)) return Current;
            _undo.AddLast(Current);
            if (_undo.Count > MaxHistory) _undo.RemoveFirst();
            _redo.Clear();
            Current = next with { Revision = Current.Revision + 1 };
            return Current;
        }
    }

    public Design Undo()
    {
        lock (_gate)
        {
            if (_undo.Count == 0) return Current;
            var previous = _undo.Last!.Value;
            _undo.RemoveLast();
            _redo.Push(Current);
            Current = previous with { Revision = Current.Revision + 1 };
            return Current;
        }
    }

    public Design Redo()
    {
        lock (_gate)
        {
            if (_redo.Count == 0) return Current;
            var next = _redo.Pop();
            _undo.AddLast(Current);
            Current = next with { Revision = Current.Revision + 1 };
            return Current;
        }
    }
}
