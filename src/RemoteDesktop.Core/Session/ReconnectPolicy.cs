namespace RemoteDesktop.Core.Session;

/// <summary>
/// Bounded reconnect policy for the Controller: a capped number of attempts with exponential
/// backoff and jitter. Bounding attempts keeps a dropped session from retrying forever and
/// lets the UI show a clear "disconnected" state instead of a perpetual spinner.
/// </summary>
public sealed class ReconnectPolicy
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly Random _rng;

    public ReconnectPolicy(
        int maxAttempts = 5,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        Random? rng = null)
    {
        _maxAttempts = maxAttempts;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(15);
        _rng = rng ?? Random.Shared;
    }

    public int MaxAttempts => _maxAttempts;

    /// <summary>
    /// Delay before attempt number <paramref name="attempt"/> (1-based), or null when attempts
    /// are exhausted and the session should transition to Disconnected.
    /// </summary>
    public TimeSpan? NextDelay(int attempt)
    {
        if (attempt < 1 || attempt > _maxAttempts)
            return null;

        // Exponential backoff: base * 2^(attempt-1), capped, with up to ±20% jitter.
        double factor = Math.Pow(2, attempt - 1);
        double ms = Math.Min(_baseDelay.TotalMilliseconds * factor, _maxDelay.TotalMilliseconds);
        double jitter = ms * (0.2 * (_rng.NextDouble() * 2 - 1));
        return TimeSpan.FromMilliseconds(Math.Max(0, ms + jitter));
    }
}
