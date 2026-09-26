using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Protocol;
using Xunit;

namespace RemoteDesktop.Tests;

public class SignalingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SignalingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
            b.UseSetting("Signaling:SessionTokenKeyBase64",
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
    }

    [Fact]
    public async Task Register_then_authenticate_end_to_end()
    {
        var client = _factory.CreateClient();
        using var id = DeviceIdentity.Create();

        var reg = await client.PostAsJsonAsync("/api/devices/register",
            new { publicKeyBase64 = id.Public.ToBase64() });
        reg.StatusCode.Should().Be(HttpStatusCode.OK);

        var begin = await client.PostAsJsonAsync("/api/auth/begin",
            new AuthBeginRequest(id.DeviceId));
        begin.StatusCode.Should().Be(HttpStatusCode.OK);
        var challenge = await begin.Content.ReadFromJsonAsync<AuthBeginResponse>();

        var ch = new AuthChallenge(Convert.FromBase64String(challenge!.NonceBase64),
            challenge.IssuedAtUnixMs, challenge.Audience, challenge.Purpose);
        var complete = await client.PostAsJsonAsync("/api/auth/complete",
            new AuthCompleteRequest(id.DeviceId, challenge.NonceBase64, challenge.IssuedAtUnixMs,
                challenge.Audience, challenge.Purpose,
                Convert.ToBase64String(id.Sign(ch.CanonicalBytes()))));

        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await complete.Content.ReadFromJsonAsync<AuthCompleteResponse>();
        response!.SessionToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Begin_for_unknown_device_is_not_found()
    {
        var client = _factory.CreateClient();
        using var id = DeviceIdentity.Create(); // not registered

        var begin = await client.PostAsJsonAsync("/api/auth/begin",
            new AuthBeginRequest(id.DeviceId));

        begin.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Complete_with_forged_signature_is_unauthorized()
    {
        var client = _factory.CreateClient();
        using var id = DeviceIdentity.Create();
        await client.PostAsJsonAsync("/api/devices/register", new { publicKeyBase64 = id.Public.ToBase64() });

        var begin = await client.PostAsJsonAsync("/api/auth/begin", new AuthBeginRequest(id.DeviceId));
        var challenge = await begin.Content.ReadFromJsonAsync<AuthBeginResponse>();

        var complete = await client.PostAsJsonAsync("/api/auth/complete",
            new AuthCompleteRequest(id.DeviceId, challenge!.NonceBase64, challenge.IssuedAtUnixMs,
                challenge.Audience, challenge.Purpose, Convert.ToBase64String(new byte[64])));

        complete.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
