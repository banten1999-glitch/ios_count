namespace RemoteDesktop.Core.Media;

/// <summary>
/// One rung of the adaptive quality ladder: a resolution scale (relative to the source),
/// a frame rate, and a target bitrate. The encoder is configured from the active rung.
/// </summary>
public sealed record QualityRung(string Name, double ResolutionScale, int FrameRate, int BitrateKbps);

/// <summary>
/// A quality ladder ordered from lowest to highest bitrate. Adaptive control moves between
/// adjacent rungs based on the measured available bandwidth. Text clarity is favored by
/// keeping resolution scale high before dropping it — readable text is a stated priority, so
/// frame rate is sacrificed before resolution.
/// </summary>
public sealed class QualityLadder
{
    private readonly IReadOnlyList<QualityRung> _rungs;

    public QualityLadder(IReadOnlyList<QualityRung> rungs)
    {
        if (rungs.Count == 0) throw new ArgumentException("Ladder needs at least one rung.", nameof(rungs));
        // Enforce ascending bitrate so index order == quality order.
        for (int i = 1; i < rungs.Count; i++)
            if (rungs[i].BitrateKbps < rungs[i - 1].BitrateKbps)
                throw new ArgumentException("Rungs must be ordered by ascending bitrate.", nameof(rungs));
        _rungs = rungs;
    }

    public int Count => _rungs.Count;
    public QualityRung this[int index] => _rungs[index];

    /// <summary>Default ladder: text-friendly (full resolution held; fps then bitrate scale down).</summary>
    public static QualityLadder Default() => new(new[]
    {
        new QualityRung("Low",       1.00, 10, 1200),
        new QualityRung("Balanced",  1.00, 20, 3000),
        new QualityRung("High",      1.00, 30, 6000),
        new QualityRung("Max",       1.00, 60, 12000),
    });
}
