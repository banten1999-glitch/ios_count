namespace RemoteDesktop.Core.Media;

/// <summary>
/// Chooses a rung on the <see cref="QualityLadder"/> from the measured available bitrate
/// (e.g. WebRTC's REMB / transport-cc estimate). Uses hysteresis and a cooldown so quality
/// doesn't oscillate on noisy estimates:
///   - step DOWN quickly when the estimate falls below the current rung's need (protects
///     latency and avoids frame buildup),
///   - step UP cautiously, only when the estimate comfortably exceeds the next rung and a
///     cooldown has elapsed.
/// </summary>
public sealed class AdaptiveQualityController
{
    private readonly QualityLadder _ladder;
    private readonly double _upHeadroom;
    private readonly double _downMargin;
    private readonly TimeSpan _upCooldown;
    private readonly TimeProvider _clock;

    private int _index;
    private DateTimeOffset _lastChange;

    public AdaptiveQualityController(
        QualityLadder ladder,
        int startIndex = 0,
        double upHeadroom = 1.30,
        double downMargin = 0.90,
        TimeSpan? upCooldown = null,
        TimeProvider? clock = null)
    {
        _ladder = ladder;
        _index = Math.Clamp(startIndex, 0, ladder.Count - 1);
        _upHeadroom = upHeadroom;
        _downMargin = downMargin;
        _upCooldown = upCooldown ?? TimeSpan.FromSeconds(10);
        _clock = clock ?? TimeProvider.System;
        _lastChange = _clock.GetUtcNow();
    }

    public QualityRung Current => _ladder[_index];
    public int CurrentIndex => _index;

    /// <summary>Feed a new bandwidth estimate (kbps) and get the (possibly changed) rung.</summary>
    public QualityRung Update(double estimatedBitrateKbps)
    {
        var now = _clock.GetUtcNow();

        // Downshift immediately if we can't sustain the current rung.
        if (_index > 0 && estimatedBitrateKbps < Current.BitrateKbps * _downMargin)
        {
            _index--;
            _lastChange = now;
            return Current;
        }

        // Upshift only past a cooldown and with clear headroom over the next rung.
        if (_index < _ladder.Count - 1 && (now - _lastChange) >= _upCooldown)
        {
            var next = _ladder[_index + 1];
            if (estimatedBitrateKbps >= next.BitrateKbps * _upHeadroom)
            {
                _index++;
                _lastChange = now;
            }
        }

        return Current;
    }
}
