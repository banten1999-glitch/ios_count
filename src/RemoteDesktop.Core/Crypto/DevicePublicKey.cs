using System.Security.Cryptography;

namespace RemoteDesktop.Core.Crypto;

/// <summary>
/// A device's public identity. Wraps an ECDSA P-256 public key (SubjectPublicKeyInfo
/// encoding) and can verify signatures produced by the matching private key.
///
/// Threat-model note (T1/T12): the device id is derived from the public key, so knowing
/// the id alone does NOT let an attacker authenticate — authentication requires proving
/// possession of the private key by signing a fresh challenge. See AuthChallenge.
/// </summary>
public sealed class DevicePublicKey
{
    /// <summary>SubjectPublicKeyInfo (SPKI) DER bytes of the P-256 public key.</summary>
    public byte[] SpkiBytes { get; }

    /// <summary>
    /// Stable device identifier: base32 of SHA-256(SPKI), lowercase, grouped for
    /// readability. Public keys are not secret; the id is a display/lookup handle.
    /// </summary>
    public string DeviceId { get; }

    public DevicePublicKey(byte[] spkiBytes)
    {
        ArgumentNullException.ThrowIfNull(spkiBytes);
        // Validate that the bytes really are an importable P-256 public key.
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(spkiBytes, out _);
        if (ecdsa.KeySize != 256)
            throw new ArgumentException("Expected a P-256 (256-bit) public key.", nameof(spkiBytes));

        SpkiBytes = (byte[])spkiBytes.Clone();
        DeviceId = DeriveDeviceId(SpkiBytes);
    }

    /// <summary>Parse a device public key from its base64 SPKI wire form.</summary>
    public static DevicePublicKey FromBase64(string base64Spki)
        => new(Convert.FromBase64String(base64Spki));

    /// <summary>Base64 SPKI wire form (used in pairing exchange and registration).</summary>
    public string ToBase64() => Convert.ToBase64String(SpkiBytes);

    /// <summary>
    /// Verify an ECDSA-P256-SHA256 signature over <paramref name="data"/>.
    /// Signature is IEEE P1363 (r||s) fixed-length encoding.
    /// </summary>
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(SpkiBytes, out _);
        return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static string DeriveDeviceId(byte[] spki)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(spki, hash);
        // Use the first 15 bytes -> 24 base32 chars, grouped in blocks of 4.
        string b32 = Base32.Encode(hash[..15]);
        return string.Join('-', Enumerable.Range(0, b32.Length / 4)
            .Select(i => b32.Substring(i * 4, 4)));
    }

    public override bool Equals(object? obj)
        => obj is DevicePublicKey other && SpkiBytes.AsSpan().SequenceEqual(other.SpkiBytes);

    public override int GetHashCode() => DeviceId.GetHashCode();
}
