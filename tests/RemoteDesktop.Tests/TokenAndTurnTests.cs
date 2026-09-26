using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RemoteDesktop.Signaling;
using RemoteDesktop.Signaling.Services;
using Xunit;

namespace RemoteDesktop.Tests;

public class TokenAndTurnTests
{
    private static IOptions<SignalingOptions> Options(Action<SignalingOptions>? tweak = null)
    {
        var o = new SignalingOptions
        {
            SessionTokenKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            SessionTokenTtl = TimeSpan.FromMinutes(5),
            Turn = new TurnOptions
            {
                SharedSecret = "test-secret",
                CredentialTtl = TimeSpan.FromMinutes(5),
                Uris = new[] { "turns:turn.example:5349?transport=tcp" }
            }
        };
        tweak?.Invoke(o);
        return Microsoft.Extensions.Options.Options.Create(o);
    }

    [Fact]
    public void Session_token_roundtrips_and_carries_device_id()
    {
        var clock = new FakeTimeProvider();
        var svc = new SessionTokenService(Options(), clock);

        var (token, _) = svc.Issue("dev-123");
        svc.TryValidate(token, out var deviceId).Should().BeTrue();
        deviceId.Should().Be("dev-123");
    }

    [Fact]
    public void Tampered_session_token_is_rejected()
    {
        var svc = new SessionTokenService(Options(), new FakeTimeProvider());
        var (token, _) = svc.Issue("dev-123");
        var tampered = token[..^2] + (token[^1] == 'A' ? "B" : "A");

        svc.TryValidate(tampered, out _).Should().BeFalse();
    }

    [Fact]
    public void Expired_session_token_is_rejected()
    {
        var clock = new FakeTimeProvider();
        var svc = new SessionTokenService(Options(o => o.SessionTokenTtl = TimeSpan.FromMinutes(1)), clock);
        var (token, _) = svc.Issue("dev-123");

        clock.Advance(TimeSpan.FromMinutes(2));
        svc.TryValidate(token, out _).Should().BeFalse();
    }

    [Fact]
    public void Turn_credentials_use_coturn_rest_hmac_scheme()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(55));
        var issuer = new TurnCredentialIssuer(Options(), clock);

        var creds = issuer.Issue("dev-123");

        creds.Username.Should().EndWith(":dev-123");
        long expiry = long.Parse(creds.Username.Split(':')[0]);
        expiry.Should().Be(clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds());

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("test-secret"));
        var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(creds.Username)));
        creds.Password.Should().Be(expected);
        creds.Uris.Should().ContainSingle();
    }
}
