namespace RemoteDesktop.Core.Input;

/// <summary>
/// Platform abstraction for injecting input on the Host. The Windows implementation wraps
/// SendInput (a documented API) and runs within the app's granted permissions only — no
/// UAC bypass, no protected-desktop tricks. Kept as an interface so the orchestration logic
/// (sequencing, release-on-drop) is unit-testable without Windows.
/// </summary>
public interface IInputSink
{
    /// <summary>Move the cursor to a normalized [0,1] position on the given monitor.</summary>
    void MoveMouse(double normalizedX, double normalizedY, int monitorIndex);

    /// <summary>Press or release a mouse button.</summary>
    void MouseButton(MouseButton button, bool isDown);

    /// <summary>Scroll by the given deltas.</summary>
    void MouseWheel(int deltaX, int deltaY);

    /// <summary>Press or release a key by Windows virtual-key code.</summary>
    void Key(int virtualKey, bool isDown);
}
