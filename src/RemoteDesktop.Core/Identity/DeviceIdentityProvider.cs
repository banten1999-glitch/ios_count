using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Storage;

namespace RemoteDesktop.Core.Identity;

/// <summary>
/// Loads the device's long-term identity from the secret store, generating and persisting a
/// new keypair on first run. The PKCS#8 private key lives only inside the <see cref="ISecretStore"/>
/// (DPAPI-protected in production); the caller works with the returned <see cref="DeviceIdentity"/>.
/// </summary>
public static class DeviceIdentityProvider
{
    public const string DefaultKeyName = "device-identity";

    public static DeviceIdentity LoadOrCreate(ISecretStore store, string keyName = DefaultKeyName)
    {
        var existing = store.TryLoad(keyName);
        if (existing is not null)
        {
            try
            {
                return DeviceIdentity.FromPkcs8(existing);
            }
            finally
            {
                Array.Clear(existing);
            }
        }

        var identity = DeviceIdentity.Create();
        var pkcs8 = identity.ExportPkcs8PrivateKey();
        try
        {
            store.Store(keyName, pkcs8);
        }
        finally
        {
            Array.Clear(pkcs8);
        }
        return identity;
    }
}
