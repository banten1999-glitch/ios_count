using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RemoteDesktop.Core.Protocol;

namespace RemoteDesktop.Signaling.Services;

/// <summary>
/// Issues short-lived TURN credentials using coturn's REST auth scheme (T8):
///   username = "&lt;expiryUnix&gt;:&lt;deviceId&gt;"
///   password = base64(HMAC-SHA1(sharedSecret, username))
/// coturn validates the HMAC itself, so the shared secret never leaves the server side and
/// credentials expire automatically at the encoded time.
/// </summary>
public sealed class TurnCredentialIssuer
{
    private readonly TurnOptions _options;
    private readonly TimeProvider _clock;

    public TurnCredentialIssuer(IOptions<SignalingOptions> options, TimeProvider? clock = null)
    {
        _options = options.Value.Turn;
        _clock = clock ?? TimeProvider.System;
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_options.SharedSecret) && _options.Uris.Length > 0;

    public TurnCredentials Issue(string deviceId)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("TURN is not configured (shared secret / URIs missing).");

        long expiry = _clock.GetUtcNow().Add(_options.CredentialTtl).ToUnixTimeSeconds();
        string username = $"{expiry}:{deviceId}";

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_options.SharedSecret));
        string password = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));

        return new TurnCredentials(
            username,
            password,
            (int)_options.CredentialTtl.TotalSeconds,
            _options.Uris);
    }
}
