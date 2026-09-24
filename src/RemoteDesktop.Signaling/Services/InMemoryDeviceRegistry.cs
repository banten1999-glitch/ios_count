using System.Collections.Concurrent;
using RemoteDesktop.Core.Crypto;

namespace RemoteDesktop.Signaling.Services;

/// <summary>
/// In-memory registry for the initial working version. A durable store (e.g. SQLite or a
/// small DB) replaces this in a later phase; the interface stays the same.
/// </summary>
public sealed class InMemoryDeviceRegistry : IDeviceRegistry
{
    private readonly ConcurrentDictionary<string, DevicePublicKey> _devices = new(StringComparer.Ordinal);

    public void Register(DevicePublicKey publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        _devices[publicKey.DeviceId] = publicKey;
    }

    public DevicePublicKey? Find(string deviceId)
        => _devices.TryGetValue(deviceId, out var key) ? key : null;
}
