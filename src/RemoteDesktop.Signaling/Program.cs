using System.Threading.RateLimiting;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Protocol;
using RemoteDesktop.Signaling;
using RemoteDesktop.Signaling.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.Configure<SignalingOptions>(builder.Configuration.GetSection(SignalingOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDeviceRegistry, InMemoryDeviceRegistry>();
builder.Services.AddSingleton<IReplayGuard, InMemoryReplayGuard>();
builder.Services.AddSingleton<SessionTokenService>();
builder.Services.AddSingleton<TurnCredentialIssuer>();
builder.Services.AddSingleton<AuthService>();

// Rate limiting (T9/T14): cap auth + registration attempts per client IP. Connection and
// pairing brute force is bounded here in addition to per-nonce single use.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Register a device's PUBLIC key so the service can look it up during auth. Public keys are
// not secrets and registration grants no access on its own — access is authorized by the
// Host's local trusted-device list, not by this registry.
app.MapPost("/api/devices/register", (RegisterRequest req, IDeviceRegistry registry) =>
{
    DevicePublicKey key;
    try { key = DevicePublicKey.FromBase64(req.PublicKeyBase64); }
    catch (Exception ex) when (ex is FormatException or ArgumentException or System.Security.Cryptography.CryptographicException)
    {
        return Results.BadRequest(new { error = "invalid_public_key" });
    }
    registry.Register(key);
    return Results.Ok(new { deviceId = key.DeviceId });
}).RequireRateLimiting("auth");

app.MapPost("/api/auth/begin", (AuthBeginRequest req, AuthService auth) =>
{
    var challenge = auth.Begin(req.DeviceId);
    // Uniform response shape whether or not the device is known would be ideal; here we
    // return 404 for unknown ids. (An enumeration-hardening pass is noted for later.)
    return challenge is null
        ? Results.NotFound(new { error = "unknown_device" })
        : Results.Ok(challenge);
}).RequireRateLimiting("auth");

app.MapPost("/api/auth/complete", (AuthCompleteRequest req, AuthService auth) =>
{
    var (result, response) = auth.Complete(req);
    return result == AuthResult.Success
        ? Results.Ok(response)
        : Results.Json(new { error = result.ToString() }, statusCode: StatusCodes.Status401Unauthorized);
}).RequireRateLimiting("auth");

app.Run();

internal sealed record RegisterRequest(string PublicKeyBase64);

// Exposed so the test project (WebApplicationFactory) can reference the entry point.
public partial class Program { }
