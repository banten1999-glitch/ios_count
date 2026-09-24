namespace RemoteDesktop.Core.Input;

/// <summary>
/// Tracks which keys and mouse buttons are currently held down on the Host as a result of
/// remote input. When a session ends for any reason (manual disconnect, Ctrl+Alt+9, network
/// drop), every tracked press must be released so the Host is never left with a "stuck" key
/// or button — a core stability requirement of the threat/UX model.
/// </summary>
public sealed class PressedInputTracker
{
    private readonly HashSet<int> _keys = new();
    private readonly HashSet<MouseButton> _buttons = new();

    public IReadOnlyCollection<int> PressedKeys => _keys;
    public IReadOnlyCollection<MouseButton> PressedButtons => _buttons;

    public void OnKey(int virtualKey, bool isDown)
    {
        if (isDown) _keys.Add(virtualKey);
        else _keys.Remove(virtualKey);
    }

    public void OnButton(MouseButton button, bool isDown)
    {
        if (isDown) _buttons.Add(button);
        else _buttons.Remove(button);
    }

    /// <summary>
    /// Return every currently-held key and button as release actions and clear the tracker.
    /// The caller applies these to the input sink to guarantee nothing stays pressed.
    /// </summary>
    public (IReadOnlyList<int> Keys, IReadOnlyList<MouseButton> Buttons) DrainForRelease()
    {
        var keys = _keys.ToList();
        var buttons = _buttons.ToList();
        _keys.Clear();
        _buttons.Clear();
        return (keys, buttons);
    }
}
