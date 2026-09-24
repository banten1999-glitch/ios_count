namespace RemoteDesktop.Core.Crypto;

public enum ChallengeVerdict
{
    Valid,
    Expired,
    NotYetValid,
    WrongAudience,
    BadSignature
}

/// <summary>
/// Pure verification of a signed challenge, with no replay state (the caller layers the
/// single-use nonce check on top). Kept in Core so it is unit-testable in isolation.
/// </summary>
public static class ChallengeVerifier
{
    /// <summary>
    /// Verify that <paramref name="signature"/> is a valid signature by <paramref name="key"/>
    /// over the given challenge, that the challenge targets <paramref name="expectedAudience"/>,
    /// and that its timestamp is within [-<paramref name="clockSkew"/>, <paramref name="validity"/>].
    /// </summary>
    public static ChallengeVerdict Verify(
        DevicePublicKey key,
        AuthChallenge challenge,
        ReadOnlySpan<byte> signature,
        string expectedAudience,
        TimeSpan validity,
        TimeSpan clockSkew,
        TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;

        if (!string.Equals(challenge.Audience, expectedAudience, StringComparison.Ordinal))
            return ChallengeVerdict.WrongAudience;

        long nowMs = clock.GetUtcNow().ToUnixTimeMilliseconds();
        long ageMs = nowMs - challenge.IssuedAtUnixMs;
        if (ageMs < -clockSkew.TotalMilliseconds)
            return ChallengeVerdict.NotYetValid;
        if (ageMs > validity.TotalMilliseconds)
            return ChallengeVerdict.Expired;

        // Signature is verified last: an expired/misdirected challenge is rejected before
        // spending a verification, and the result never depends on secret-dependent timing.
        return key.Verify(challenge.CanonicalBytes(), signature)
            ? ChallengeVerdict.Valid
            : ChallengeVerdict.BadSignature;
    }
}
