using System.Collections.Concurrent;
using RemoteDesktop.Core.Protocol;

namespace RemoteDesktop.Core.Trust;

/// <summary>In-memory trusted-device store, used by tests and as the base for file-backed stores.</summary>
public sealed class InMemoryTrustedDeviceStore : ITrustedDeviceStore
{
    private readonly ConcurrentDictionary<string, TrustedDevice> _devices = new(StringComparer.Ordinal);

    public IReadOnlyList<TrustedDevice> List()
        => _devices.Values.OrderBy(d => d.PairedAt).ToList();

    public TrustedDevice? Find(string deviceId)
        => _devices.TryGetValue(deviceId, out var d) ? d : null;

    public void Upsert(TrustedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _devices[device.DeviceId] = device;
    }

    public bool Revoke(string deviceId, DateTimeOffset when)
    {
        if (!_devices.TryGetValue(deviceId, out var existing))
            return false;
        _devices[deviceId] = existing with { RevokedAt = when };
        return true;
    }
}
