using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace RemoteDesktop.Core.Storage;

/// <summary>
/// Windows DPAPI-backed secret store. Each secret is encrypted with
/// <see cref="DataProtectionScope.CurrentUser"/> and written as a file under a per-app
/// directory, so the ciphertext is bound to the current Windows user account and cannot be
/// decrypted by another user or on another machine.
///
/// This is the production store for the device private key on both Host and Controller.
/// It is Windows-only; other platforms use a different <see cref="ISecretStore"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _directory;
    private readonly byte[]? _entropy;

    /// <param name="directory">Folder for encrypted secret files (e.g. under %LOCALAPPDATA%).</param>
    /// <param name="entropy">Optional extra entropy mixed into DPAPI, adding defense in depth.</param>
    public DpapiSecretStore(string directory, byte[]? entropy = null)
    {
        _directory = directory;
        _entropy = entropy;
        Directory.CreateDirectory(_directory);
    }

    public void Store(string name, ReadOnlySpan<byte> secret)
    {
        byte[] protectedBytes = ProtectedData.Protect(secret.ToArray(), _entropy, DataProtectionScope.CurrentUser);
        var tmp = PathFor(name) + ".tmp";
        File.WriteAllBytes(tmp, protectedBytes);
        File.Move(tmp, PathFor(name), overwrite: true);
    }

    public byte[]? TryLoad(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
            return null;
        byte[] protectedBytes = File.ReadAllBytes(path);
        return ProtectedData.Unprotect(protectedBytes, _entropy, DataProtectionScope.CurrentUser);
    }

    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    private string PathFor(string name)
    {
        // Constrain the name to a safe file-name so a logical name can't escape the directory.
        var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_directory, safe + ".dpapi");
    }
}
