using System.Runtime.Versioning;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Input;
using RemoteDesktop.Core.Protocol;
using RemoteDesktop.Core.Session;
using RemoteDesktop.Platform.Windows.Signaling;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace RemoteDesktop.Platform.Windows.Rtc;

/// <summary>
/// Drives the Controller side: creates the offer to a specific paired Host, sends signed
/// signals, receives and decodes video, and streams input events. Verifies every incoming
/// signal against the Host's paired key. Disconnect (including via Ctrl+Alt+9) sends a Bye and
/// tears the session down; the Host then releases any held input on its end.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ControllerConnectionOrchestrator : IAsyncDisposable
{
    private readonly DeviceIdentity _identity;
    private readonly DevicePublicKey _hostKey;
    private readonly string _hostDeviceId;
    private readonly SignalingClient _signaling;
    private readonly string? _stunUrl;

    private WebRtcPeer? _peer;
    private VideoDecodeBridge? _decoder;
    private long _sequence;

    public ControllerConnectionOrchestrator(
        DeviceIdentity identity,
        DevicePublicKey hostKey,
        SignalingClient signaling,
        string? stunUrl = "stun:stun.l.google.com:19302")
    {
        _identity = identity;
        _hostKey = hostKey;
        _hostDeviceId = hostKey.DeviceId;
        _signaling = signaling;
        _stunUrl = stunUrl;
        _signaling.SignalReceived += OnSignal;
    }

    public event Action<DecodedVideoFrame>? FrameReceived;
    public event Action<RTCPeerConnectionState>? ConnectionStateChanged;

    public async Task ConnectAsync()
    {
        var turn = _signaling.Session!.Turn;
        _peer = new WebRtcPeer(IceServerFactory.Build(turn, _stunUrl));

        _decoder = new VideoDecodeBridge();
        _decoder.DecodedFrame += f => FrameReceived?.Invoke(f);
        _peer.AddVideoTrack(_decoder.Source, MediaStreamStatusEnum.RecvOnly);
        _peer.OnEncodedVideo((ts, fmt, data) => _decoder.OnEncodedFrame(ts, fmt, data));

        _peer.IceCandidate += c => SendSigned(SignalKind.IceCandidate, SignalPayloads.FromIce(c));
        _peer.ConnectionStateChanged += s => ConnectionStateChanged?.Invoke(s);

        await _peer.CreateInputChannelAsync();
        var offer = await _peer.CreateOfferAsync();
        SendSigned(SignalKind.Offer, SignalPayloads.FromSdp(offer));
        Diagnostics.FileLog.Info($"Controller: offer sent to host {_hostDeviceId}");
    }

    private void OnSignal(SignedSignal signal)
    {
        try
        {
            var env = signal.Envelope;
            if (env.FromDeviceId != _hostDeviceId) return;
            if (!signal.Verify(_hostKey)) return;

            switch (env.Kind)
            {
                case SignalKind.Answer when _peer is not null:
                    _peer.ApplyRemoteDescription(SignalPayloads.ToSdp(env.Payload));
                    break;
                case SignalKind.IceCandidate when _peer is not null:
                    _peer.ApplyRemoteIceCandidate(SignalPayloads.ToIce(env.Payload));
                    break;
                case SignalKind.Bye:
                    _ = DisconnectAsync();
                    break;
            }
        }
        catch { /* drop malformed/hostile signals */ }
    }

    /// <summary>Send one input event with a monotonic sequence number.</summary>
    public void SendInput(InputEvent evt)
    {
        var seq = Interlocked.Increment(ref _sequence);
        _peer?.SendInput(InputEventCodec.Encode(evt with { Sequence = seq }));
    }

    /// <summary>Disconnect: notify the Host (so it releases input) and tear down locally.</summary>
    public async Task DisconnectAsync()
    {
        SendSigned(SignalKind.Bye, string.Empty);
        if (_peer is not null) await _peer.DisposeAsync();
        _decoder?.Dispose();
        _peer = null;
        _decoder = null;
        ConnectionStateChanged?.Invoke(RTCPeerConnectionState.closed);
    }

    private void SendSigned(SignalKind kind, string payload)
    {
        var env = new SignalEnvelope(kind, _identity.DeviceId, _hostDeviceId, payload);
        _ = _signaling.SendAsync(SignedSignal.Create(_identity, env));
    }

    public async ValueTask DisposeAsync()
    {
        _signaling.SignalReceived -= OnSignal;
        await DisconnectAsync();
    }
}
