using System.Buffers.Binary;
using System.Text;
using RemoteDesktop.Core.Crypto;

namespace RemoteDesktop.Core.Pairing;

/// <summary>
/// Proof, produced by the Controller during pairing, that it holds the private key matching
/// the public key it presents AND knows the current pairing code shown on the Host.
///
/// The Controller signs the canonical bytes below with its device private key. The Host
/// verifies the signature against the presented public key (proof of possession, so the
/// public key cannot be someone else's) and checks that the code and host id match its own
/// pending pairing. Physical confirmation on the Host is still required afterwards.
/// </summary>
public static class PairingProof
{
    private static readonly byte[] Domain = "rdc-pairing-proof-v1"u8.ToArray();

    /// <summary>
    /// Deterministic, length-prefixed encoding binding the code, the controller's public key,
    /// the target host id, and a timestamp.
    /// </summary>
    public static byte[] CanonicalBytes(
        string pairingCode,
        byte[] controllerSpki,
        string hostDeviceId,
        long issuedAtUnixMs)
    {
        using var ms = new MemoryStream();
        WriteChunk(ms, Domain);
        WriteChunk(ms, Encoding.UTF8.GetBytes(PairingCode.Normalize(pairingCode)));
        WriteChunk(ms, controllerSpki);
        WriteChunk(ms, Encoding.UTF8.GetBytes(hostDeviceId));

        Span<byte> ts = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(ts, issuedAtUnixMs);
        WriteChunk(ms, ts);
        return ms.ToArray();

        static void WriteChunk(Stream s, ReadOnlySpan<byte> chunk)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, chunk.Length);
            s.Write(len);
            s.Write(chunk);
        }
    }

    /// <summary>Controller side: sign the canonical pairing bytes.</summary>
    public static byte[] Sign(DeviceIdentity controller, string pairingCode, string hostDeviceId, long issuedAtUnixMs)
        => controller.Sign(CanonicalBytes(pairingCode, controller.Public.SpkiBytes, hostDeviceId, issuedAtUnixMs));

    /// <summary>Host side: verify the controller's signature over the pairing bytes.</summary>
    public static bool Verify(
        DevicePublicKey controllerKey,
        string pairingCode,
        string hostDeviceId,
        long issuedAtUnixMs,
        ReadOnlySpan<byte> signature)
        => controllerKey.Verify(
            CanonicalBytes(pairingCode, controllerKey.SpkiBytes, hostDeviceId, issuedAtUnixMs),
            signature);
}
