using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using RemoteDesktop.Core.Crypto;
using Xunit;

namespace RemoteDesktop.Tests;

public class CryptoTests
{
    [Fact]
    public void DeviceId_is_derived_from_public_key_and_is_stable()
    {
        using var id = DeviceIdentity.Create();
        var reparsed = DevicePublicKey.FromBase64(id.Public.ToBase64());
        reparsed.DeviceId.Should().Be(id.DeviceId);
    }

    [Fact]
    public void Identity_survives_pkcs8_roundtrip()
    {
        using var id = DeviceIdentity.Create();
        var pkcs8 = id.ExportPkcs8PrivateKey();
        using var restored = DeviceIdentity.FromPkcs8(pkcs8);
        restored.DeviceId.Should().Be(id.DeviceId);
    }

    [Fact]
    public void Signature_verifies_with_matching_key_and_fails_with_another()
    {
        using var alice = DeviceIdentity.Create();
        using var mallory = DeviceIdentity.Create();
        var data = "hello"u8.ToArray();

        var sig = alice.Sign(data);
        alice.Public.Verify(data, sig).Should().BeTrue();
        mallory.Public.Verify(data, sig).Should().BeFalse();
    }

    [Fact]
    public void ChallengeVerifier_accepts_a_fresh_valid_signature()
    {
        var clock = new FakeTimeProvider();
        using var id = DeviceIdentity.Create();
        var challenge = AuthChallenge.Issue("aud", "connect", clock);
        var sig = id.Sign(challenge.CanonicalBytes());

        var verdict = ChallengeVerifier.Verify(id.Public, challenge, sig, "aud",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), clock);

        verdict.Should().Be(ChallengeVerdict.Valid);
    }

    [Fact]
    public void ChallengeVerifier_rejects_expired_challenge()
    {
        var clock = new FakeTimeProvider();
        using var id = DeviceIdentity.Create();
        var challenge = AuthChallenge.Issue("aud", "connect", clock);
        var sig = id.Sign(challenge.CanonicalBytes());

        clock.Advance(TimeSpan.FromSeconds(31));

        ChallengeVerifier.Verify(id.Public, challenge, sig, "aud",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), clock)
            .Should().Be(ChallengeVerdict.Expired);
    }

    [Fact]
    public void ChallengeVerifier_rejects_wrong_audience()
    {
        var clock = new FakeTimeProvider();
        using var id = DeviceIdentity.Create();
        var challenge = AuthChallenge.Issue("other-service", "connect", clock);
        var sig = id.Sign(challenge.CanonicalBytes());

        ChallengeVerifier.Verify(id.Public, challenge, sig, "aud",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), clock)
            .Should().Be(ChallengeVerdict.WrongAudience);
    }

    [Fact]
    public void ChallengeVerifier_rejects_tampered_signature()
    {
        var clock = new FakeTimeProvider();
        using var id = DeviceIdentity.Create();
        var challenge = AuthChallenge.Issue("aud", "connect", clock);
        var sig = id.Sign(challenge.CanonicalBytes());
        sig[0] ^= 0xFF;

        ChallengeVerifier.Verify(id.Public, challenge, sig, "aud",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), clock)
            .Should().Be(ChallengeVerdict.BadSignature);
    }
}
