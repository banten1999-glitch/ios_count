using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace RemoteDesktop.Signaling.Services;

/// <summary>
/// Issues and validates short-lived, HMAC-signed session tokens granted after successful
/// challenge authentication. Stateless: the token carries its own device id + expiry and is
/// verified by recomputing the HMAC, so no server-side session table is needed.
///
/// Format (base64url): deviceIdBytes-len(2) | deviceId | expiryUnixMs(8) | HMAC-SHA256(16).
/// </summary>
public sealed class SessionTokenService
{
    private readonly byte[] _key;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _clock;

    public SessionTokenService(IOptions<SignalingOptions> options, TimeProvider? clock = null)
    {
        var o = options.Value;
        if (string.IsNullOrEmpty(o.SessionTokenKeyBase64))
        {
            // No key configured: generate an ephemeral per-process key so local/first-run works
            // without setup. Tokens then reset on restart and can't be shared across instances —
            // configure Signaling:SessionTokenKeyBase64 for any real/multi-instance deployment.
            _key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            _key = Convert.FromBase64String(o.SessionTokenKeyBase64);
            if (_key.Length < 32)
                throw new InvalidOperationException("Session token key must be at least 32 bytes.");
        }
        _ttl = o.SessionTokenTtl;
        _clock = clock ?? TimeProvider.System;
    }

    public (string Token, long ExpiresAtUnixMs) Issue(string deviceId)
    {
        var idBytes = Encoding.UTF8.GetBytes(deviceId);
        long expiry = _clock.GetUtcNow().Add(_ttl).ToUnixTimeMilliseconds();

        var body = new byte[2 + idBytes.Length + 8];
        BinaryPrimitives.WriteUInt16BigEndian(body, (ushort)idBytes.Length);
        idBytes.CopyTo(body.AsSpan(2));
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(2 + idBytes.Length), expiry);

        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(_key, body, mac);

        var token = new byte[body.Length + 16];
        body.CopyTo(token, 0);
        mac[..16].CopyTo(token.AsSpan(body.Length));

        return (Base64Url(token), expiry);
    }

    public bool TryValidate(string token, out string deviceId)
    {
        deviceId = string.Empty;
        byte[] raw;
        try { raw = FromBase64Url(token); }
        catch { return false; }

        if (raw.Length < 2 + 8 + 16) return false;
        int idLen = BinaryPrimitives.ReadUInt16BigEndian(raw);
        int bodyLen = 2 + idLen + 8;
        if (raw.Length != bodyLen + 16) return false;

        Span<byte> expected = stackalloc byte[32];
        HMACSHA256.HashData(_key, raw.AsSpan(0, bodyLen), expected);
        if (!CryptographicOperations.FixedTimeEquals(expected[..16], raw.AsSpan(bodyLen, 16)))
            return false;

        long expiry = BinaryPrimitives.ReadInt64BigEndian(raw.AsSpan(2 + idLen, 8));
        if (_clock.GetUtcNow().ToUnixTimeMilliseconds() > expiry) return false;

        deviceId = Encoding.UTF8.GetString(raw, 2, idLen);
        return true;
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        string b64 = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64.PadRight((b64.Length + 3) / 4 * 4, '='));
    }
}
