using RemoteDesktop.Core.Protocol;

namespace RemoteDesktop.Core.Trust;

/// <summary>
/// Authorization decisions for the Host. A connection is allowed only if the requesting
/// device is present in the trusted store AND not revoked. This is the local gate that
/// makes "knowing the device id" insufficient — the peer must also be explicitly trusted
/// here and prove key possession during signaling auth.
/// </summary>
public sealed class TrustManager
{
    private readonly ITrustedDeviceStore _store;

    public TrustManager(ITrustedDeviceStore store) => _store = store;

    public bool IsAuthorized(string deviceId)
        => _store.Find(deviceId) is { IsActive: true };

    public IReadOnlyList<TrustedDevice> ListTrusted() => _store.List();

    public bool Revoke(string deviceId, TimeProvider? clock = null)
        => _store.Revoke(deviceId, (clock ?? TimeProvider.System).GetUtcNow());
}
