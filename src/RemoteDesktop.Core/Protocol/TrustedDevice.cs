namespace RemoteDesktop.Core.Protocol;

/// <summary>
/// A device the owner has explicitly paired and trusts. Stored locally on the Host
/// (the controlled machine) and used to authorize unattended connections. The owner can
/// list and revoke these at any time (T15).
///
/// Trust is by public key, not by device id: the id is derived from the key, so revoking
/// or matching always compares the actual key material.
/// </summary>
public sealed record TrustedDevice(
    string DeviceId,
    string PublicKeyBase64,
    string DisplayName,
    DateTimeOffset PairedAt,
    DateTimeOffset? RevokedAt = null)
{
    public bool IsActive => RevokedAt is null;
}
