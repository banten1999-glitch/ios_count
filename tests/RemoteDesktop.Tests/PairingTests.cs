using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Pairing;
using RemoteDesktop.Core.Trust;
using Xunit;

namespace RemoteDesktop.Tests;

public class PairingTests
{
    private static (HostPairingService Host, DeviceIdentity HostId, InMemoryTrustedDeviceStore Store, FakeTimeProvider Clock)
        BuildHost(int maxAttempts = 5)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(55));
        var hostId = DeviceIdentity.Create();
        var store = new InMemoryTrustedDeviceStore();
        var host = new HostPairingService(hostId, store, clock, maxAttempts);
        return (host, hostId, store, clock);
    }

    [Fact]
    public void Full_pairing_flow_trusts_the_controller_only_after_confirmation()
    {
        var (host, hostId, store, clock) = BuildHost();
        using var controller = DeviceIdentity.Create();

        var code = host.Begin(TimeSpan.FromMinutes(2));
        var req = ControllerPairing.CreateRequest(controller, code.Value, hostId.DeviceId, clock);

        var submit = host.Submit(req.Code, DevicePublicKey.FromBase64(req.ControllerPublicKeyBase64),
            req.IssuedAtUnixMs, Convert.FromBase64String(req.SignatureBase64));

        submit.Verdict.Should().Be(PairingSubmitVerdict.AwaitingConfirmation);
        submit.ControllerDeviceId.Should().Be(controller.DeviceId);
        store.Find(controller.DeviceId).Should().BeNull("trust is granted only on explicit confirmation");

        var trusted = host.Confirm("My Laptop");
        trusted!.DeviceId.Should().Be(controller.DeviceId);

        new TrustManager(store).IsAuthorized(controller.DeviceId).Should().BeTrue();
    }

    [Fact]
    public void Wrong_code_is_rejected_and_does_not_pair()
    {
        var (host, hostId, _, clock) = BuildHost();
        using var controller = DeviceIdentity.Create();
        host.Begin(TimeSpan.FromMinutes(2));

        var req = ControllerPairing.CreateRequest(controller, "wrongwrong", hostId.DeviceId, clock);
        var submit = host.Submit("wrongwrong", controller.Public, req.IssuedAtUnixMs,
            Convert.FromBase64String(req.SignatureBase64));

        submit.Verdict.Should().Be(PairingSubmitVerdict.WrongCode);
        host.Confirm("x").Should().BeNull();
    }

    [Fact]
    public void Expired_code_is_rejected()
    {
        var (host, hostId, _, clock) = BuildHost();
        using var controller = DeviceIdentity.Create();
        var code = host.Begin(TimeSpan.FromSeconds(30));

        clock.Advance(TimeSpan.FromSeconds(31));
        var req = ControllerPairing.CreateRequest(controller, code.Value, hostId.DeviceId, clock);
        var submit = host.Submit(req.Code, controller.Public, req.IssuedAtUnixMs,
            Convert.FromBase64String(req.SignatureBase64));

        submit.Verdict.Should().Be(PairingSubmitVerdict.CodeExpired);
    }

    [Fact]
    public void Attempts_are_capped_to_defeat_guessing()
    {
        var (host, hostId, _, clock) = BuildHost(maxAttempts: 3);
        using var controller = DeviceIdentity.Create();
        host.Begin(TimeSpan.FromMinutes(5));

        for (int i = 0; i < 3; i++)
        {
            var bad = ControllerPairing.CreateRequest(controller, "aaaaabbbbb", hostId.DeviceId, clock);
            host.Submit("aaaaabbbbb", controller.Public, bad.IssuedAtUnixMs,
                Convert.FromBase64String(bad.SignatureBase64))
                .Verdict.Should().Be(PairingSubmitVerdict.WrongCode);
        }

        // Next attempt — even with a plausible code — is blocked.
        var again = ControllerPairing.CreateRequest(controller, "aaaaabbbbb", hostId.DeviceId, clock);
        host.Submit("aaaaabbbbb", controller.Public, again.IssuedAtUnixMs,
            Convert.FromBase64String(again.SignatureBase64))
            .Verdict.Should().Be(PairingSubmitVerdict.TooManyAttempts);
    }

    [Fact]
    public void Proof_signed_by_a_different_key_is_rejected()
    {
        var (host, hostId, _, clock) = BuildHost();
        using var controller = DeviceIdentity.Create();
        using var attacker = DeviceIdentity.Create();
        var code = host.Begin(TimeSpan.FromMinutes(2));

        // Controller's public key presented, but the signature is from the attacker's key.
        var req = ControllerPairing.CreateRequest(attacker, code.Value, hostId.DeviceId, clock);
        var submit = host.Submit(code.Value, controller.Public, req.IssuedAtUnixMs,
            Convert.FromBase64String(req.SignatureBase64));

        submit.Verdict.Should().Be(PairingSubmitVerdict.BadProof);
    }

    [Fact]
    public void Submit_without_a_pending_code_is_rejected()
    {
        var (host, hostId, _, clock) = BuildHost();
        using var controller = DeviceIdentity.Create();
        var req = ControllerPairing.CreateRequest(controller, "abcdeabcde", hostId.DeviceId, clock);

        host.Submit(req.Code, controller.Public, req.IssuedAtUnixMs,
            Convert.FromBase64String(req.SignatureBase64))
            .Verdict.Should().Be(PairingSubmitVerdict.NoPendingCode);
    }

    [Fact]
    public void Revoking_a_paired_device_removes_authorization()
    {
        var (host, hostId, store, clock) = BuildHost();
        using var controller = DeviceIdentity.Create();
        var code = host.Begin(TimeSpan.FromMinutes(2));
        var req = ControllerPairing.CreateRequest(controller, code.Value, hostId.DeviceId, clock);
        host.Submit(req.Code, controller.Public, req.IssuedAtUnixMs, Convert.FromBase64String(req.SignatureBase64));
        host.Confirm("Laptop");

        var trust = new TrustManager(store);
        trust.IsAuthorized(controller.DeviceId).Should().BeTrue();

        trust.Revoke(controller.DeviceId).Should().BeTrue();
        trust.IsAuthorized(controller.DeviceId).Should().BeFalse();
    }
}
