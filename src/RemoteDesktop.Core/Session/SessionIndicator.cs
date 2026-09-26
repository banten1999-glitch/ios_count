namespace RemoteDesktop.Core.Session;

/// <summary>Info shown on the Host's mandatory session indicator while a connection is active.</summary>
public sealed record SessionIndicatorInfo(string PeerDeviceId, string PeerDisplayName, DateTimeOffset StartedAt);

/// <summary>
/// The Host's visible session indicator (a small always-on badge shown while someone is
/// connected). This interface is the view; the platform draws it. By design there is NO
/// method to suppress the indicator while a session is active.
/// </summary>
public interface ISessionIndicatorView
{
    void Show(IReadOnlyList<SessionIndicatorInfo> activeSessions);
    void Hide();
}

/// <summary>
/// Enforces the design rule that the Host ALWAYS shows a visible indicator whenever one or
/// more remote sessions are active (transparency requirement / abuse-prevention). Visibility
/// is derived purely from the active-session set — it cannot be turned off independently.
/// Hiding happens only when the last session ends.
/// </summary>
public sealed class MandatorySessionIndicator
{
    private readonly ISessionIndicatorView _view;
    private readonly Dictionary<string, SessionIndicatorInfo> _active = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public MandatorySessionIndicator(ISessionIndicatorView view) => _view = view;

    public bool IsVisible { get; private set; }

    public int ActiveCount
    {
        get { lock (_gate) return _active.Count; }
    }

    /// <summary>Register an active session; the indicator becomes/stays visible.</summary>
    public void OnSessionStarted(SessionIndicatorInfo info)
    {
        lock (_gate)
        {
            _active[info.PeerDeviceId] = info;
            Refresh();
        }
    }

    /// <summary>Deregister a session; the indicator hides only when none remain.</summary>
    public void OnSessionEnded(string peerDeviceId)
    {
        lock (_gate)
        {
            _active.Remove(peerDeviceId);
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_active.Count > 0)
        {
            IsVisible = true;
            _view.Show(_active.Values.OrderBy(s => s.StartedAt).ToList());
        }
        else
        {
            IsVisible = false;
            _view.Hide();
        }
    }
}
