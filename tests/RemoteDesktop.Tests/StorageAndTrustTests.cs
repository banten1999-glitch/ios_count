using FluentAssertions;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Identity;
using RemoteDesktop.Core.Protocol;
using RemoteDesktop.Core.Storage;
using RemoteDesktop.Core.Trust;
using Xunit;

namespace RemoteDesktop.Tests;

public class StorageAndTrustTests
{
    [Fact]
    public void InMemorySecretStore_stores_loads_and_deletes()
    {
        var store = new InMemorySecretStore();
        var secret = new byte[] { 1, 2, 3, 4 };

        store.TryLoad("k").Should().BeNull();
        store.Store("k", secret);
        store.TryLoad("k").Should().Equal(secret);
        store.Delete("k").Should().BeTrue();
        store.TryLoad("k").Should().BeNull();
        store.Delete("k").Should().BeFalse();
    }

    [Fact]
    public void DeviceIdentityProvider_creates_once_then_reloads_same_identity()
    {
        var store = new InMemorySecretStore();

        using var first = DeviceIdentityProvider.LoadOrCreate(store);
        using var second = DeviceIdentityProvider.LoadOrCreate(store);

        second.DeviceId.Should().Be(first.DeviceId, "the persisted key must be reused, not regenerated");
    }

    [Fact]
    public void JsonFileTrustedDeviceStore_persists_across_reopen_including_revocation()
    {
        var path = Path.Combine(Path.GetTempPath(), "rdc-tests", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using var id = DeviceIdentity.Create();
            var device = new TrustedDevice(id.DeviceId, id.Public.ToBase64(), "Laptop", DateTimeOffset.UnixEpoch);

            var store1 = new JsonFileTrustedDeviceStore(path);
            store1.Upsert(device);
            store1.Revoke(id.DeviceId, DateTimeOffset.UnixEpoch.AddDays(1)).Should().BeTrue();

            // Reopen from disk: the entry and its revocation survive.
            var store2 = new JsonFileTrustedDeviceStore(path);
            var reloaded = store2.Find(id.DeviceId);
            reloaded.Should().NotBeNull();
            reloaded!.IsActive.Should().BeFalse();
            reloaded.DisplayName.Should().Be("Laptop");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TrustManager_only_authorizes_active_trusted_devices()
    {
        var store = new InMemoryTrustedDeviceStore();
        var trust = new TrustManager(store);
        using var id = DeviceIdentity.Create();

        trust.IsAuthorized(id.DeviceId).Should().BeFalse();

        store.Upsert(new TrustedDevice(id.DeviceId, id.Public.ToBase64(), "PC", DateTimeOffset.UnixEpoch));
        trust.IsAuthorized(id.DeviceId).Should().BeTrue();

        trust.Revoke(id.DeviceId);
        trust.IsAuthorized(id.DeviceId).Should().BeFalse();
    }
}
