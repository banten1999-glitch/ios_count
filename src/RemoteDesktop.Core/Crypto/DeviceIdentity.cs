using System.Security.Cryptography;

namespace RemoteDesktop.Core.Crypto;

/// <summary>
/// A device's full identity including its private key. Lives only on the device that
/// owns it. The PRIVATE KEY must be persisted through OS-protected storage (DPAPI /
/// Credential Manager) by the Host/Controller apps — never in plaintext (T7).
///
/// This type is in Core only to generate keys and produce signatures; the signaling
/// service never sees a private key and only ever works with <see cref="DevicePublicKey"/>.
/// </summary>
public sealed class DeviceIdentity : IDisposable
{
    private readonly ECDsa _ecdsa;

    private DeviceIdentity(ECDsa ecdsa)
    {
        _ecdsa = ecdsa;
        Public = new DevicePublicKey(ecdsa.ExportSubjectPublicKeyInfo());
    }

    public DevicePublicKey Public { get; }

    public string DeviceId => Public.DeviceId;

    /// <summary>Generate a fresh P-256 identity keypair.</summary>
    public static DeviceIdentity Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>
    /// Restore an identity from a PKCS#8 private key blob. The caller is responsible for
    /// having stored/loaded <paramref name="pkcs8PrivateKey"/> via OS-protected storage.
    /// </summary>
    public static DeviceIdentity FromPkcs8(ReadOnlySpan<byte> pkcs8PrivateKey)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(pkcs8PrivateKey, out _);
        return new DeviceIdentity(ecdsa);
    }

    /// <summary>
    /// Export the PKCS#8 private key so the caller can hand it to DPAPI for at-rest
    /// protection. Keep the returned buffer in memory only as long as needed.
    /// </summary>
    public byte[] ExportPkcs8PrivateKey() => _ecdsa.ExportPkcs8PrivateKey();

    /// <summary>Sign data with ECDSA-P256-SHA256, IEEE P1363 (r||s) signature form.</summary>
    public byte[] Sign(ReadOnlySpan<byte> data)
        => _ecdsa.SignData(data, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public void Dispose() => _ecdsa.Dispose();
}
