namespace RemoteDesktop.Core.Input;

/// <summary>
/// Applies remote input events to an <see cref="IInputSink"/> on the Host, in order, while
/// tracking held keys/buttons so they can all be released when the session ends
/// (<see cref="ReleaseAll"/>).
///
/// Ordering: events carry a monotonic sequence number; anything not strictly newer than the
/// last applied event is dropped, so a delayed/duplicated packet can't resurrect stale input.
/// The executor is single-consumer (the input pump); callers serialize access.
/// </summary>
public sealed class RemoteInputExecutor
{
    private readonly IInputSink _sink;
    private readonly PressedInputTracker _tracker = new();
    private long _lastSequence = long.MinValue;

    public RemoteInputExecutor(IInputSink sink) => _sink = sink;

    public PressedInputTracker Tracker => _tracker;

    /// <summary>Apply one event. Returns false if it was dropped as stale/out-of-order.</summary>
    public bool Apply(InputEvent evt)
    {
        if (evt.Sequence <= _lastSequence)
            return false;
        _lastSequence = evt.Sequence;

        switch (evt)
        {
            case MouseMoveEvent m:
                _sink.MoveMouse(m.X, m.Y, m.MonitorIndex);
                break;
            case MouseButtonEvent b:
                _sink.MouseButton(b.Button, b.IsDown);
                _tracker.OnButton(b.Button, b.IsDown);
                break;
            case MouseWheelEvent w:
                _sink.MouseWheel(w.DeltaX, w.DeltaY);
                break;
            case KeyEvent k:
                _sink.Key(k.VirtualKey, k.IsDown);
                _tracker.OnKey(k.VirtualKey, k.IsDown);
                break;
        }
        return true;
    }

    /// <summary>
    /// Release every currently-held key and button. Idempotent and safe to call on any
    /// teardown path (manual disconnect, hotkey, network loss). Must not throw so that
    /// cleanup always completes even if the sink faults on one action.
    /// </summary>
    public void ReleaseAll()
    {
        var (keys, buttons) = _tracker.DrainForRelease();
        foreach (var vk in keys)
            TrySink(() => _sink.Key(vk, isDown: false));
        foreach (var btn in buttons)
            TrySink(() => _sink.MouseButton(btn, isDown: false));
    }

    private static void TrySink(Action action)
    {
        try { action(); }
        catch { /* best-effort release: continue releasing the rest */ }
    }
}
