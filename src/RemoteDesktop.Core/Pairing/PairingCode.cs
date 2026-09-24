using System.Security.Cryptography;
using RemoteDesktop.Core.Crypto;

namespace RemoteDesktop.Core.Pairing;

/// <summary>
/// A short, human-transferable pairing code generated on the Host and read aloud/typed by
/// the owner onto the Controller. Short-lived and rate-limited on use (T14).
///
/// Entropy: 10 base32 chars ≈ 50 bits, displayed as two groups of five. Combined with a
/// short validity window and an attempt cap, guessing is infeasible while the code stays
/// easy to transcribe once.
/// </summary>
public sealed record PairingCode(string Value, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt)
{
    private const int EntropyBytes = 7; // 56 bits -> 12 base32 chars, we keep 10

    public static PairingCode Generate(TimeSpan validity, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        var raw = RandomNumberGenerator.GetBytes(EntropyBytes);
        string code = Base32.Encode(raw)[..10];
        var now = clock.GetUtcNow();
        return new PairingCode(code, now, now.Add(validity));
    }

    public bool IsExpired(TimeProvider? clock = null)
        => (clock ?? TimeProvider.System).GetUtcNow() >= ExpiresAt;

    /// <summary>Grouped display form, e.g. "abcde-fghij".</summary>
    public string Display() => Value.Length == 10 ? $"{Value[..5]}-{Value[5..]}" : Value;

    /// <summary>Normalize user input (case, spaces, dashes) before comparison.</summary>
    public static string Normalize(string input)
        => new(input.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Constant-time comparison of a candidate against this code.</summary>
    public bool Matches(string candidate)
    {
        var a = System.Text.Encoding.ASCII.GetBytes(Value);
        var b = System.Text.Encoding.ASCII.GetBytes(Normalize(candidate));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
