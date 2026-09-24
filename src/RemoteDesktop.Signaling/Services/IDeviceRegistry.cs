using RemoteDesktop.Core.Crypto;

namespace RemoteDesktop.Signaling.Services;

/// <summary>
/// Maps a device id to its registered public key. A device is registered when it first
/// pairs. The registry stores only PUBLIC keys — the service never holds private keys.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>Register (or re-register) a device's public key. Idempotent per key.</summary>
    void Register(DevicePublicKey publicKey);

    /// <summary>Look up a device's public key by id, or null if unknown.</summary>
    DevicePublicKey? Find(string deviceId);
}
