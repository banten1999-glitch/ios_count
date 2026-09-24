using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Protocol;
using RemoteDesktop.Signaling;
using RemoteDesktop.Signaling.Services;
using Xunit;

namespace RemoteDesktop.Tests;

public class AuthServiceTests
{
    private static (AuthService Auth, IDeviceRegistry Registry, FakeTimeProvider Clock) Build()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(55));
        var options = Options.Create(new SignalingOptions
        {
            Audience = "rdc-signaling",
            ChallengeValidity = TimeSpan.FromSeconds(30),
            ClockSkew = TimeSpan.FromSeconds(5),
            SessionTokenTtl = TimeSpan.FromMinutes(5),
            SessionTokenKeyBase64 = Convert.ToBase64String(new byte[32]),
            Turn = new TurnOptions()
        });
        var registry = new InMemoryDeviceRegistry();
        var replay = new InMemoryReplayGuard(clock);
        var tokens = new SessionTokenService(options, clock);
        var turn = new TurnCredentialIssuer(options, clock);
        var auth = new AuthService(registry, replay, tokens, turn, options, clock);
        return (auth, registry, clock);
    }

    private static AuthCompleteRequest Sign(DeviceIdentity id, AuthBeginResponse begin)
    {
        var challenge = new AuthChallenge(
            Convert.FromBase64String(begin.NonceBase64),
            begin.IssuedAtUnixMs, begin.Audience, begin.Purpose);
        var sig = id.Sign(challenge.CanonicalBytes());
        return new AuthCompleteRequest(id.DeviceId, begin.NonceBase64, begin.IssuedAtUnixMs,
            begin.Audience, begin.Purpose, Convert.ToBase64String(sig));
    }

    [Fact]
    public void Paired_device_authenticates_successfully()
    {
        var (auth, registry, _) = Build();
        using var id = DeviceIdentity.Create();
        registry.Register(id.Public);

        var begin = auth.Begin(id.DeviceId);
        begin.Should().NotBeNull();

        var (result, response) = auth.Complete(Sign(id, begin!));
        result.Should().Be(AuthResult.Success);
        response!.SessionToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Unknown_device_cannot_begin_or_complete()
    {
        var (auth, _, _) = Build();
        using var id = DeviceIdentity.Create(); // never registered

        auth.Begin(id.DeviceId).Should().BeNull();

        var req = new AuthCompleteRequest(id.DeviceId, Convert.ToBase64String(new byte[32]),
            0, "rdc-signaling", "connect", Convert.ToBase64String(new byte[64]));
        auth.Complete(req).Result.Should().Be(AuthResult.UnknownDevice);
    }

    [Fact]
    public void Knowing_the_device_id_without_the_key_is_rejected()
    {
        // Simulates threat C/T1: attacker knows the target's device id but not its private key.
        var (auth, registry, _) = Build();
        using var victim = DeviceIdentity.Create();
        registry.Register(victim.Public);

        var begin = auth.Begin(victim.DeviceId)!; // the id is public, so this succeeds
        using var attacker = DeviceIdentity.Create(); // different key
        var challenge = new AuthChallenge(Convert.FromBase64String(begin.NonceBase64),
            begin.IssuedAtUnixMs, begin.Audience, begin.Purpose);
        var forged = new AuthCompleteRequest(victim.DeviceId, begin.NonceBase64,
            begin.IssuedAtUnixMs, begin.Audience, begin.Purpose,
            Convert.ToBase64String(attacker.Sign(challenge.CanonicalBytes())));

        auth.Complete(forged).Result.Should().Be(AuthResult.BadSignature);
    }

    [Fact]
    public void Replayed_challenge_is_rejected_on_second_use()
    {
        var (auth, registry, _) = Build();
        using var id = DeviceIdentity.Create();
        registry.Register(id.Public);

        var req = Sign(id, auth.Begin(id.DeviceId)!);
        auth.Complete(req).Result.Should().Be(AuthResult.Success);
        // Same signed challenge presented again:
        auth.Complete(req).Result.Should().Be(AuthResult.Replayed);
    }

    [Fact]
    public void Expired_challenge_is_rejected()
    {
        var (auth, registry, clock) = Build();
        using var id = DeviceIdentity.Create();
        registry.Register(id.Public);

        var req = Sign(id, auth.Begin(id.DeviceId)!);
        clock.Advance(TimeSpan.FromSeconds(31));
        auth.Complete(req).Result.Should().Be(AuthResult.Expired);
    }

    [Fact]
    public void Failed_signature_does_not_burn_the_nonce()
    {
        // An attacker must not be able to consume a victim's nonce with a bogus signature.
        var (auth, registry, _) = Build();
        using var id = DeviceIdentity.Create();
        registry.Register(id.Public);

        var begin = auth.Begin(id.DeviceId)!;
        var bad = Sign(id, begin) with { SignatureBase64 = Convert.ToBase64String(new byte[64]) };
        auth.Complete(bad).Result.Should().Be(AuthResult.BadSignature);

        // The legitimate holder can still use the same challenge:
        auth.Complete(Sign(id, begin)).Result.Should().Be(AuthResult.Success);
    }
}
