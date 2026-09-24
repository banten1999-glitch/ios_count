namespace RemoteDesktop.Core.Session;

/// <summary>Observable connection state, surfaced to the UI so status is always clear.</summary>
public enum SessionState
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Disconnected
}
