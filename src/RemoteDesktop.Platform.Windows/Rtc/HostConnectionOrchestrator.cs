using System.Runtime.Versioning;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Input;
using RemoteDesktop.Core.Media;
using RemoteDesktop.Core.Protocol;
using RemoteDesktop.Core.Session;
using RemoteDesktop.Core.Trust;
using RemoteDesktop.Platform.Windows.Capture;
using RemoteDesktop.Platform.Windows.Input;
using RemoteDesktop.Platform.Windows.Signaling;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace RemoteDesktop.Platform.Windows.Rtc;

/// <summary>
/// Drives an unattended Host connection: it accepts an offer ONLY from a paired, non-revoked
/// device whose signed signal verifies against the stored key, then streams the screen and
/// executes verified remote input. The mandatory session indicator is shown for the session's
/// entire lifetime, and all held input is released on any teardown.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class HostConnectionOrchestrator : IAsyncDisposable
{
    private readonly DeviceIdentity _identity;
    private readonly TrustManager _trust;
    private readonly SignalingClient _signaling;
    private readonly MandatorySessionIndicator _indicator;
    private readonly string? _stunUrl;

    private WebRtcPeer? _peer;
    private DesktopDuplicationCapturer? _capturer;
    private ScreenVideoSource? _video;
    private HostControlSession? _control;
    private DevicePublicKey? _peerKey;
    private string? _peerId;

    public HostConnectionOrchestrator(
        DeviceIdentity identity,
        TrustManager trust,
        SignalingClient signaling,
        MandatorySessionIndicator indicator,
        string? stunUrl = "stun:stun.l.google.com:19302")
    {
        _identity = identity;
        _trust = trust;
        _signaling = signaling;
        _indicator = indicator;
        _stunUrl = stunUrl;
        _signaling.SignalReceived += OnSignal;
    }

    private async void OnSignal(SignedSignal signal)
    {
        try
        {
            var env = signal.Envelope;
            Diagnostics.FileLog.Info($"Host: signal received kind={env.Kind} from={env.FromDeviceId}");

            // Only paired, active devices may connect; unknown/revoked ids are ignored (T12/T15).
            var key = _trust.GetPublicKey(env.FromDeviceId);
            if (key is null)
            {
                Diagnostics.FileLog.Info($"Host: dropped {env.Kind} — device not trusted: {env.FromDeviceId}");
                return;
            }
            if (!signal.Verify(key))
            {
                Diagnostics.FileLog.Info($"Host: dropped {env.Kind} — signature verify failed");
                return; // authenticity + integrity (defeats MITM, T3)
            }

            switch (env.Kind)
            {
                case SignalKind.Offer:
                    await AcceptOfferAsync(env, key);
                    break;
                case SignalKind.IceCandidate when _peer is not null && env.FromDeviceId == _peerId:
                    _peer.ApplyRemoteIceCandidate(SignalPayloads.ToIce(env.Payload));
                    break;
                case SignalKind.Bye when env.FromDeviceId == _peerId:
                    Teardown();
                    break;
            }
        }
        catch (Exception ex)
        {
            // A malformed or hostile signal must never crash the Host; drop and stay available.
            Diagnostics.FileLog.Error("Host: error handling signal", ex);
        }
    }

    private async Task AcceptOfferAsync(SignalEnvelope env, DevicePublicKey key)
    {
        Teardown(); // one active session at a time

        _peerId = env.FromDeviceId;
        _peerKey = key;

        var turn = _signaling.Session!.Turn;
        _peer = new WebRtcPeer(IceServerFactory.Build(turn, _stunUrl));

        _capturer = new DesktopDuplicationCapturer();
        var quality = new AdaptiveQualityController(QualityLadder.Default(), startIndex: 1);
        _video = new ScreenVideoSource(_capturer, quality);
        _peer.AddVideoTrack(_video.Source, MediaStreamStatusEnum.SendOnly);

        var sink = new SendInputSink(DisplayEnumerator.Enumerate());
        _control = new HostControlSession(sink);
        _peer.InputReceived += json => _control.HandleDataChannelMessage(json);

        _peer.IceCandidate += c => SendSigned(SignalKind.IceCandidate, SignalPayloads.FromIce(c));
        _peer.ConnectionStateChanged += OnPeerState;

        _peer.ApplyRemoteDescription(SignalPayloads.ToSdp(env.Payload));
        var answer = await _peer.CreateAnswerAsync();
        SendSigned(SignalKind.Answer, SignalPayloads.FromSdp(answer));

        _capturer.Start();
        _indicator.OnSessionStarted(new SessionIndicatorInfo(_peerId,
            DisplayNameFor(_peerId), DateTimeOffset.UtcNow));
        Diagnostics.FileLog.Info($"Host: answered offer from {_peerId}, capture started");
    }

    private void OnPeerState(RTCPeerConnectionState state)
    {
        Diagnostics.FileLog.Info($"Host: peer state = {state}");
        if (state is RTCPeerConnectionState.disconnected
            or RTCPeerConnectionState.failed
            or RTCPeerConnectionState.closed)
        {
            Teardown();
        }
    }

    private string DisplayNameFor(string deviceId)
        => _trust.ListTrusted().FirstOrDefault(d => d.DeviceId == deviceId)?.DisplayName ?? deviceId;

    private void SendSigned(SignalKind kind, string payload)
    {
        if (_peerId is null) return;
        var env = new SignalEnvelope(kind, _identity.DeviceId, _peerId, payload);
        _ = _signaling.SendAsync(SignedSignal.Create(_identity, env));
    }

    private void Teardown()
    {
        var peerId = _peerId;

        _control?.Stop();          // releases all held keys/buttons
        _capturer?.Stop();
        _video?.Dispose();
        _ = _peer?.DisposeAsync();

        _control = null;
        _video = null;
        _capturer = null;
        _peer = null;
        _peerKey = null;
        _peerId = null;

        if (peerId is not null)
            _indicator.OnSessionEnded(peerId);
    }

    public ValueTask DisposeAsync()
    {
        _signaling.SignalReceived -= OnSignal;
        Teardown();
        return ValueTask.CompletedTask;
    }
}
