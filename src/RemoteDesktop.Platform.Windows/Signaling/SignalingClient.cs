using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Protocol;

namespace RemoteDesktop.Platform.Windows.Signaling;

/// <summary>
/// Client for the ASP.NET Core signaling service: registers the device public key,
/// authenticates via the challenge/response flow (proof of key possession), then opens a WSS
/// connection to relay <see cref="SignedSignal"/> envelopes between the two paired peers.
///
/// The client verifies nothing about peers here — signature verification against the paired
/// key happens in the session orchestrator before any SDP/ICE is applied.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SignalingClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Uri _baseUri;
    private readonly HttpClient _http;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;

    public SignalingClient(Uri baseUri, HttpClient? http = null)
    {
        _baseUri = baseUri;
        _http = http ?? new HttpClient();
    }

    public event Action<SignedSignal>? SignalReceived;
    public AuthCompleteResponse? Session { get; private set; }

    public async Task RegisterAsync(DevicePublicKey publicKey, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(new Uri(_baseUri, "/api/devices/register"),
            new { publicKeyBase64 = publicKey.ToBase64() }, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Run the full challenge/response and store the resulting session token + TURN creds.</summary>
    public async Task<AuthCompleteResponse> AuthenticateAsync(DeviceIdentity identity, CancellationToken ct = default)
    {
        var begin = await _http.PostAsJsonAsync(new Uri(_baseUri, "/api/auth/begin"),
            new AuthBeginRequest(identity.DeviceId), ct);
        begin.EnsureSuccessStatusCode();
        var challenge = (await begin.Content.ReadFromJsonAsync<AuthBeginResponse>(Json, ct))!;

        var chal = new AuthChallenge(
            Convert.FromBase64String(challenge.NonceBase64),
            challenge.IssuedAtUnixMs, challenge.Audience, challenge.Purpose);
        var signature = identity.Sign(chal.CanonicalBytes());

        var complete = await _http.PostAsJsonAsync(new Uri(_baseUri, "/api/auth/complete"),
            new AuthCompleteRequest(identity.DeviceId, challenge.NonceBase64, challenge.IssuedAtUnixMs,
                challenge.Audience, challenge.Purpose, Convert.ToBase64String(signature)), ct);
        complete.EnsureSuccessStatusCode();

        Session = (await complete.Content.ReadFromJsonAsync<AuthCompleteResponse>(Json, ct))!;
        return Session;
    }

    /// <summary>Open the signaling WebSocket using the session token from authentication.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (Session is null) throw new InvalidOperationException("Authenticate before connecting.");

        var wsUri = new UriBuilder(_baseUri)
        {
            Scheme = _baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/ws/signal",
            Query = "token=" + Uri.EscapeDataString(Session.SessionToken)
        }.Uri;

        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(wsUri, ct);

        _receiveCts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoop(_receiveCts.Token));
    }

    public async Task SendAsync(SignedSignal signal, CancellationToken ct = default)
    {
        if (_ws is not { State: WebSocketState.Open })
            throw new InvalidOperationException("Signaling socket is not open.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(signal, Json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var json = sb.ToString();
                sb.Clear();
                var signal = JsonSerializer.Deserialize<SignedSignal>(json, Json);
                if (signal is not null)
                    SignalReceived?.Invoke(signal);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (WebSocketException) { /* connection dropped; orchestrator handles reconnect */ }
    }

    public async ValueTask DisposeAsync()
    {
        _receiveCts?.Cancel();
        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { /* best effort */ }
            _ws.Dispose();
        }
    }
}
