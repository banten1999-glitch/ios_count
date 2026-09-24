using Microsoft.Extensions.Options;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Protocol;

namespace RemoteDesktop.Signaling.Services;

public enum AuthResult
{
    Success,
    UnknownDevice,
    Replayed,
    Expired,
    NotYetValid,
    WrongAudience,
    BadSignature
}

/// <summary>
/// Orchestrates challenge/response authentication: issues challenges, then verifies a
/// signed response against the registered public key, enforces single-use nonces
/// (anti-replay), and on success issues a session token + TURN credentials.
/// </summary>
public sealed class AuthService
{
    private readonly IDeviceRegistry _registry;
    private readonly IReplayGuard _replayGuard;
    private readonly SessionTokenService _tokens;
    private readonly TurnCredentialIssuer _turn;
    private readonly SignalingOptions _options;
    private readonly TimeProvider _clock;

    public AuthService(
        IDeviceRegistry registry,
        IReplayGuard replayGuard,
        SessionTokenService tokens,
        TurnCredentialIssuer turn,
        IOptions<SignalingOptions> options,
        TimeProvider? clock = null)
    {
        _registry = registry;
        _replayGuard = replayGuard;
        _tokens = tokens;
        _turn = turn;
        _options = options.Value;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Issue a fresh challenge for a known device. Returns null for unknown ids.</summary>
    public AuthBeginResponse? Begin(string deviceId)
    {
        if (_registry.Find(deviceId) is null)
            return null; // do not reveal more than "unknown"; caller returns a uniform error

        var challenge = AuthChallenge.Issue(_options.Audience, purpose: "connect", _clock);
        return new AuthBeginResponse(
            Convert.ToBase64String(challenge.Nonce),
            challenge.IssuedAtUnixMs,
            challenge.Audience,
            challenge.Purpose);
    }

    /// <summary>Verify a signed challenge and, on success, produce a session token + TURN creds.</summary>
    public (AuthResult Result, AuthCompleteResponse? Response) Complete(AuthCompleteRequest req)
    {
        var key = _registry.Find(req.DeviceId);
        if (key is null)
            return (AuthResult.UnknownDevice, null);

        byte[] nonce, signature;
        try
        {
            nonce = Convert.FromBase64String(req.NonceBase64);
            signature = Convert.FromBase64String(req.SignatureBase64);
        }
        catch (FormatException)
        {
            return (AuthResult.BadSignature, null);
        }

        var challenge = new AuthChallenge(nonce, req.IssuedAtUnixMs, req.Audience, req.Purpose);

        var verdict = ChallengeVerifier.Verify(
            key, challenge, signature,
            _options.Audience, _options.ChallengeValidity, _options.ClockSkew, _clock);

        if (verdict != ChallengeVerdict.Valid)
            return (Map(verdict), null);

        // Consume the nonce only AFTER the signature checks out, so an attacker cannot burn a
        // victim's nonce with a bogus signature. The consume window matches challenge validity.
        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(req.IssuedAtUnixMs)
            .Add(_options.ChallengeValidity + _options.ClockSkew);
        if (!_replayGuard.TryConsume(challenge.NonceKey(), expiresAt))
            return (AuthResult.Replayed, null);

        var (token, expiry) = _tokens.Issue(req.DeviceId);
        var turn = _turn.IsConfigured ? _turn.Issue(req.DeviceId) : EmptyTurn();
        return (AuthResult.Success, new AuthCompleteResponse(token, expiry, turn));
    }

    private static TurnCredentials EmptyTurn() => new(string.Empty, string.Empty, 0, Array.Empty<string>());

    private static AuthResult Map(ChallengeVerdict v) => v switch
    {
        ChallengeVerdict.Expired => AuthResult.Expired,
        ChallengeVerdict.NotYetValid => AuthResult.NotYetValid,
        ChallengeVerdict.WrongAudience => AuthResult.WrongAudience,
        _ => AuthResult.BadSignature
    };
}
