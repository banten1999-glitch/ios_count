using RemoteDesktop.Core.Protocol;

namespace RemoteDesktop.Core.Trust;

/// <summary>
/// Local store of devices the owner has paired and trusts (on the Host). Holds only public
/// keys, so plain persistence is acceptable; the owner can list and revoke entries (T15).
/// </summary>
public interface ITrustedDeviceStore
{
    IReadOnlyList<TrustedDevice> List();

    TrustedDevice? Find(string deviceId);

    /// <summary>Add or replace a trusted device entry.</summary>
    void Upsert(TrustedDevice device);

    /// <summary>Mark a device revoked. Returns false if the device is unknown.</summary>
    bool Revoke(string deviceId, DateTimeOffset when);
}
