namespace RemoteDesktop.Signaling;

/// <summary>
/// Configuration for the signaling service. Secrets (TURN shared secret, token signing
/// key) are supplied via environment/user-secrets, never committed.
/// </summary>
public sealed class SignalingOptions
{
    public const string SectionName = "Signaling";

    /// <summary>Audience string bound into every challenge (T3). Must be stable per deployment.</summary>
    public string Audience { get; set; } = "rdc-signaling";

    /// <summary>How long an issued challenge stays valid.</summary>
    public TimeSpan ChallengeValidity { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Allowed clock skew between client and server.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Lifetime of a session token issued on successful auth.</summary>
    public TimeSpan SessionTokenTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Base64 HMAC key used to sign session tokens. REQUIRED in production.</summary>
    public string SessionTokenKeyBase64 { get; set; } = string.Empty;

    public TurnOptions Turn { get; set; } = new();
}

public sealed class TurnOptions
{
    /// <summary>coturn `static-auth-secret` (REST auth). REQUIRED to hand out TURN creds.</summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>TTL for issued TURN credentials.</summary>
    public TimeSpan CredentialTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>TURN/STUN URIs handed to clients, e.g. turns:host:5349?transport=tcp.</summary>
    public string[] Uris { get; set; } = Array.Empty<string>();
}
