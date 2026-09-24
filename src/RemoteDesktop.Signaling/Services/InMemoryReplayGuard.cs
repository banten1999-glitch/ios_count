using System.Collections.Concurrent;

namespace RemoteDesktop.Signaling.Services;

/// <summary>
/// In-memory single-use nonce store with lazy expiry sweeping. Suitable for a single
/// instance; a distributed deployment would back this with a shared store (e.g. Redis)
/// behind the same interface.
/// </summary>
public sealed class InMemoryReplayGuard : IReplayGuard
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _used = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private long _lastSweepTicks;

    public InMemoryReplayGuard(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _lastSweepTicks = _clock.GetUtcNow().UtcTicks;
    }

    public bool TryConsume(string nonceKey, DateTimeOffset expiresAt)
    {
        SweepIfDue();
        // TryAdd is atomic: only the first caller for a given key succeeds.
        return _used.TryAdd(nonceKey, expiresAt);
    }

    private void SweepIfDue()
    {
        var now = _clock.GetUtcNow();
        long last = Interlocked.Read(ref _lastSweepTicks);
        if ((now.UtcTicks - last) < TimeSpan.FromMinutes(1).Ticks)
            return;
        if (Interlocked.CompareExchange(ref _lastSweepTicks, now.UtcTicks, last) != last)
            return; // another thread is sweeping

        foreach (var kvp in _used)
            if (kvp.Value <= now)
                _used.TryRemove(kvp.Key, out _);
    }
}
