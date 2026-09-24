using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace RemoteDesktop.Core.Crypto;

/// <summary>
/// A single-use authentication challenge issued by the signaling service. The client
/// proves possession of its device private key by signing the canonical bytes of the
/// challenge (see <see cref="CanonicalBytes"/>).
///
/// Anti-replay (T3/T13): every challenge carries a random <see cref="Nonce"/> and an
/// <see cref="IssuedAtUnixMs"/> timestamp. The server rejects a nonce it has already
/// consumed and any challenge older than its validity window. The <see cref="Audience"/>
/// binds the signature to this specific service so a signature captured for one purpose
/// cannot be replayed against another.
/// </summary>
public sealed record AuthChallenge(
    byte[] Nonce,
    long IssuedAtUnixMs,
    string Audience,
    string Purpose)
{
    public const int NonceSize = 32;

    /// <summary>Domain-separation tag so these signatures can't collide with other uses.</summary>
    private static readonly byte[] Domain = "rdc-auth-challenge-v1"u8.ToArray();

    public static AuthChallenge Issue(string audience, string purpose, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        return new AuthChallenge(nonce, clock.GetUtcNow().ToUnixTimeMilliseconds(), audience, purpose);
    }

    /// <summary>
    /// Deterministic, length-prefixed encoding of the challenge that the client signs.
    /// Length prefixes make the encoding unambiguous, preventing field-boundary confusion.
    /// </summary>
    public byte[] CanonicalBytes()
    {
        var audience = Encoding.UTF8.GetBytes(Audience);
        var purpose = Encoding.UTF8.GetBytes(Purpose);

        using var ms = new MemoryStream();
        WriteChunk(ms, Domain);
        WriteChunk(ms, Nonce);

        Span<byte> ts = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(ts, IssuedAtUnixMs);
        WriteChunk(ms, ts);

        WriteChunk(ms, audience);
        WriteChunk(ms, purpose);
        return ms.ToArray();

        static void WriteChunk(Stream s, ReadOnlySpan<byte> chunk)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, chunk.Length);
            s.Write(len);
            s.Write(chunk);
        }
    }

    /// <summary>Lowercase hex of the nonce, used as the anti-replay store key.</summary>
    public string NonceKey() => Convert.ToHexString(Nonce).ToLowerInvariant();
}
