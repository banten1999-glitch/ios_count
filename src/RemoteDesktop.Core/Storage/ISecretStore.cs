namespace RemoteDesktop.Core.Storage;

/// <summary>
/// At-rest protection for sensitive material — above all the device private key (T7). The
/// production implementation on Windows is DPAPI-backed (<see cref="DpapiSecretStore"/>);
/// secrets are never written in plaintext.
/// </summary>
public interface ISecretStore
{
    /// <summary>Store (or replace) a secret under a logical name.</summary>
    void Store(string name, ReadOnlySpan<byte> secret);

    /// <summary>Load a secret, or null if not present.</summary>
    byte[]? TryLoad(string name);

    /// <summary>Delete a secret if present. Returns true if something was removed.</summary>
    bool Delete(string name);
}
