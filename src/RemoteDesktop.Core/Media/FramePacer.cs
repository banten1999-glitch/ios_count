namespace RemoteDesktop.Core.Media;

/// <summary>
/// Prevents video-frame accumulation under load. Only one frame may be in flight
/// (encode+send) at a time, and frames are admitted no faster than the target frame
/// interval. When a new frame arrives while one is in flight, it is dropped — only the latest
/// frame matters — so a slow encoder or network can't build a growing backlog that adds
/// latency. Control input travels on a separate data channel and is never gated by this.
/// </summary>
public sealed class FramePacer
{
    private readonly object _gate = new();
    private double _minIntervalMs;
    private long _lastAdmittedMs = long.MinValue;
    private bool _inFlight;

    public FramePacer(int targetFrameRate) => SetTargetFrameRate(targetFrameRate);

    /// <summary>Adjust pacing when the adaptive controller changes the frame rate.</summary>
    public void SetTargetFrameRate(int targetFrameRate)
    {
        if (targetFrameRate <= 0) throw new ArgumentOutOfRangeException(nameof(targetFrameRate));
        lock (_gate)
            _minIntervalMs = 1000.0 / targetFrameRate;
    }

    /// <summary>
    /// Try to admit the frame captured at <paramref name="nowMs"/>. Returns true if the caller
    /// should encode+send it (and must later call <see cref="CompleteSend"/>); false to drop it.
    /// </summary>
    public bool TryAdmit(long nowMs)
    {
        lock (_gate)
        {
            if (_inFlight)
                return false;
            if (_lastAdmittedMs != long.MinValue && (nowMs - _lastAdmittedMs) < _minIntervalMs)
                return false;

            _inFlight = true;
            _lastAdmittedMs = nowMs;
            return true;
        }
    }

    /// <summary>Signal that the in-flight frame finished encoding/sending.</summary>
    public void CompleteSend()
    {
        lock (_gate)
            _inFlight = false;
    }
}
