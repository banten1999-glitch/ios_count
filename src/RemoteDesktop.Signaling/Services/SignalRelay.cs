using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RemoteDesktop.Core.Protocol;

namespace RemoteDesktop.Signaling.Services;

/// <summary>
/// Relays signed signaling envelopes between connected, authenticated devices. The relay never
/// inspects or trusts payload contents — peers sign envelopes end-to-end and verify against the
/// paired key — it only routes a <see cref="SignedSignal"/> to the socket of its ToDeviceId.
///
/// A device may have a single active signaling socket; a new connection replaces the old one.
/// </summary>
public sealed class SignalRelay
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new(StringComparer.Ordinal);
    private readonly ILogger<SignalRelay> _log;

    public SignalRelay(ILogger<SignalRelay> log) => _log = log;

    /// <summary>Serve one authenticated device socket until it closes.</summary>
    public async Task ServeAsync(string deviceId, WebSocket socket, CancellationToken ct)
    {
        Register(deviceId, socket);
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var payload = sb.ToString();
                sb.Clear();
                await RouteAsync(deviceId, payload, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _sockets.TryRemove(new KeyValuePair<string, WebSocket>(deviceId, socket));
        }
    }

    private async Task RouteAsync(string fromDeviceId, string payload, CancellationToken ct)
    {
        SignedSignal? signal;
        try { signal = JsonSerializer.Deserialize<SignedSignal>(payload, Json); }
        catch (JsonException) { return; }
        if (signal is null) return;

        // Bind the routed sender to the authenticated socket: a client can't spoof another's id.
        if (!string.Equals(signal.Envelope.FromDeviceId, fromDeviceId, StringComparison.Ordinal))
        {
            _log.LogWarning("Dropping signal with mismatched sender id on {DeviceId}", fromDeviceId);
            return;
        }

        if (_sockets.TryGetValue(signal.Envelope.ToDeviceId, out var target)
            && target.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            await target.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        // If the target is offline, the signal is dropped; the peers retry via ICE/session logic.
    }

    private void Register(string deviceId, WebSocket socket)
    {
        _sockets.AddOrUpdate(deviceId, socket, (_, old) =>
        {
            try { old.Abort(); } catch { }
            return socket;
        });
    }
}
