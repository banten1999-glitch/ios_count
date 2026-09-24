using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Serialization;
using RemoteDesktop.Core.Crypto;

namespace RemoteDesktop.Core.Protocol;

/// <summary>
/// A <see cref="SignalEnvelope"/> signed by the sending device's identity key. The receiver
/// verifies the signature against the PAIRED public key it already holds for that peer —
/// independently of the signaling server. Because the signed payload includes the SDP (and
/// thus the DTLS fingerprint), a malicious or compromised signaling service cannot swap in
/// its own fingerprint to man-in-the-middle the media/data channel (T3/T6).
/// </summary>
public sealed record SignedSignal(
    [property: JsonPropertyName("envelope")] SignalEnvelope Envelope,
    [property: JsonPropertyName("signature")] string SignatureBase64)
{
    private static readonly byte[] Domain = "rdc-signal-v1"u8.ToArray();

    private static byte[] CanonicalBytes(SignalEnvelope e)
    {
        using var ms = new MemoryStream();
        WriteChunk(ms, Domain);

        Span<byte> kind = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(kind, (int)e.Kind);
        WriteChunk(ms, kind);

        WriteChunk(ms, Encoding.UTF8.GetBytes(e.FromDeviceId));
        WriteChunk(ms, Encoding.UTF8.GetBytes(e.ToDeviceId));
        WriteChunk(ms, Encoding.UTF8.GetBytes(e.Payload));
        return ms.ToArray();

        static void WriteChunk(Stream s, ReadOnlySpan<byte> chunk)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, chunk.Length);
            s.Write(len);
            s.Write(chunk);
        }
    }

    /// <summary>Sender side: sign an envelope with the device identity key.</summary>
    public static SignedSignal Create(DeviceIdentity sender, SignalEnvelope envelope)
    {
        var sig = sender.Sign(CanonicalBytes(envelope));
        return new SignedSignal(envelope, Convert.ToBase64String(sig));
    }

    /// <summary>
    /// Receiver side: verify the signature against the expected (paired) sender key AND that
    /// the envelope's declared sender id matches that key. Returns false on any mismatch.
    /// </summary>
    public bool Verify(DevicePublicKey expectedSender)
    {
        if (!string.Equals(Envelope.FromDeviceId, expectedSender.DeviceId, StringComparison.Ordinal))
            return false;
        byte[] sig;
        try { sig = Convert.FromBase64String(SignatureBase64); }
        catch (FormatException) { return false; }
        return expectedSender.Verify(CanonicalBytes(Envelope), sig);
    }
}
