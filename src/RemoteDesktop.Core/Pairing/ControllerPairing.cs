using System.Text.Json.Serialization;
using RemoteDesktop.Core.Crypto;

namespace RemoteDesktop.Core.Pairing;

/// <summary>
/// What the Controller sends to the Host to pair: its public key, the code the owner typed,
/// a timestamp, and a signature proving possession of its private key over those fields.
/// </summary>
public sealed record ControllerPairingRequest(
    [property: JsonPropertyName("controllerPublicKey")] string ControllerPublicKeyBase64,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("hostDeviceId")] string HostDeviceId,
    [property: JsonPropertyName("issuedAtUnixMs")] long IssuedAtUnixMs,
    [property: JsonPropertyName("signature")] string SignatureBase64);

/// <summary>Controller-side helper to assemble a signed pairing request.</summary>
public static class ControllerPairing
{
    public static ControllerPairingRequest CreateRequest(
        DeviceIdentity controller,
        string code,
        string hostDeviceId,
        TimeProvider? clock = null)
    {
        long issuedAt = (clock ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds();
        var signature = PairingProof.Sign(controller, code, hostDeviceId, issuedAt);
        return new ControllerPairingRequest(
            controller.Public.ToBase64(),
            code,
            hostDeviceId,
            issuedAt,
            Convert.ToBase64String(signature));
    }
}
