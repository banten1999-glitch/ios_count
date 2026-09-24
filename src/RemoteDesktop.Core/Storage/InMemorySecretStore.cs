using System.Collections.Concurrent;

namespace RemoteDesktop.Core.Storage;

/// <summary>
/// Non-persistent secret store for tests and non-Windows development ONLY. It keeps secrets
/// in process memory with no at-rest protection and must never be used to hold a real device
/// key in production — that is what <see cref="DpapiSecretStore"/> is for.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, byte[]> _secrets = new(StringComparer.Ordinal);

    public void Store(string name, ReadOnlySpan<byte> secret) => _secrets[name] = secret.ToArray();

    public byte[]? TryLoad(string name)
        => _secrets.TryGetValue(name, out var v) ? (byte[])v.Clone() : null;

    public bool Delete(string name) => _secrets.TryRemove(name, out _);
}
