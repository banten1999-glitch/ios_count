using System.Text.Json.Serialization;

namespace RemoteDesktop.Core.Protocol;

/// <summary>
/// Wire contracts for the signaling service. The service only brokers authentication and
/// relays opaque SDP/ICE payloads between two already-paired devices; it never sees media
/// or input, which travel peer-to-peer over WebRTC (DTLS-SRTP).
/// </summary>

// ---- Authentication (challenge/response) ----

/// <summary>Client asks the service to start authentication for a device id.</summary>
public sealed record AuthBeginRequest(
    [property: JsonPropertyName("deviceId")] string DeviceId);

/// <summary>Service returns a single-use challenge for the client to sign.</summary>
public sealed record AuthBeginResponse(
    [property: JsonPropertyName("nonce")] string NonceBase64,
    [property: JsonPropertyName("issuedAtUnixMs")] long IssuedAtUnixMs,
    [property: JsonPropertyName("audience")] string Audience,
    [property: JsonPropertyName("purpose")] string Purpose);

/// <summary>Client returns its signature over the challenge's canonical bytes.</summary>
public sealed record AuthCompleteRequest(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("nonce")] string NonceBase64,
    [property: JsonPropertyName("issuedAtUnixMs")] long IssuedAtUnixMs,
    [property: JsonPropertyName("audience")] string Audience,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("signature")] string SignatureBase64);

/// <summary>On success, a short-lived session token + TURN credentials.</summary>
public sealed record AuthCompleteResponse(
    [property: JsonPropertyName("sessionToken")] string SessionToken,
    [property: JsonPropertyName("expiresAtUnixMs")] long ExpiresAtUnixMs,
    [property: JsonPropertyName("turn")] TurnCredentials Turn);

/// <summary>Short-lived TURN credentials (T8): time-limited username + HMAC password.</summary>
public sealed record TurnCredentials(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("ttlSeconds")] int TtlSeconds,
    [property: JsonPropertyName("uris")] string[] Uris);

// ---- SDP/ICE relay (used in Phase 3, defined here for the shared contract) ----

public enum SignalKind
{
    Offer,
    Answer,
    IceCandidate,
    Bye,

    // Pairing bootstrap (pre-trust). PairRequest carries a ControllerPairingRequest as payload;
    // PairAccepted carries the Host's public key (base64 SPKI); PairRejected carries a reason.
    PairRequest,
    PairAccepted,
    PairRejected
}

/// <summary>An opaque signaling payload relayed between two paired peers.</summary>
public sealed record SignalEnvelope(
    [property: JsonPropertyName("kind")] SignalKind Kind,
    [property: JsonPropertyName("fromDeviceId")] string FromDeviceId,
    [property: JsonPropertyName("toDeviceId")] string ToDeviceId,
    [property: JsonPropertyName("payload")] string Payload);
