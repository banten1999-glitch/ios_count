using RemoteDesktop.Core.Input;

namespace RemoteDesktop.Core.Session;

/// <summary>
/// Host-side control session: consumes remote input from the data channel and guarantees
/// that all held keys/buttons are released when the session ends for ANY reason — a remote
/// "Bye", the Controller's Ctrl+Alt+9 disconnect, or a network drop. Stopping is idempotent
/// and always releases input, so the Host can never be left with stuck input.
/// </summary>
public sealed class HostControlSession : IDisposable
{
    private readonly RemoteInputExecutor _executor;
    private readonly object _gate = new();
    private bool _stopped;

    public HostControlSession(IInputSink sink) => _executor = new RemoteInputExecutor(sink);

    public SessionState State { get; private set; } = SessionState.Connected;

    public event Action<SessionState>? StateChanged;

    /// <summary>Handle one raw data-channel message. Ignored after the session has stopped.</summary>
    public bool HandleDataChannelMessage(string json)
    {
        lock (_gate)
        {
            if (_stopped) return false;
            var evt = InputEventCodec.TryDecode(json);
            return evt is not null && _executor.Apply(evt);
        }
    }

    /// <summary>
    /// End the session and release all held input. Safe to call multiple times and from any
    /// teardown path.
    /// </summary>
    public void Stop(SessionState finalState = SessionState.Disconnected)
    {
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            _executor.ReleaseAll();
            State = finalState;
        }
        StateChanged?.Invoke(finalState);
    }

    public void Dispose() => Stop();
}
