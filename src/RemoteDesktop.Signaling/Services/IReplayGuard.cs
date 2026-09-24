namespace RemoteDesktop.Signaling.Services;

/// <summary>
/// Single-use enforcement for challenge nonces (T13). A nonce may be consumed at most once
/// within its validity window; a second attempt to consume the same nonce fails, defeating
/// replay of a captured signed challenge.
/// </summary>
public interface IReplayGuard
{
    /// <summary>
    /// Atomically mark <paramref name="nonceKey"/> as used until <paramref name="expiresAt"/>.
    /// Returns true if this call consumed it (first use), false if it was already consumed.
    /// </summary>
    bool TryConsume(string nonceKey, DateTimeOffset expiresAt);
}
