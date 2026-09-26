using System.Runtime.Versioning;
using RemoteDesktop.Core.Protocol;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace RemoteDesktop.Platform.Windows.Rtc;

/// <summary>
/// Thin wrapper over a SIPSorcery <see cref="RTCPeerConnection"/> that carries:
///   - a reliable, ordered data channel for input events (Controller → Host),
///   - a video track (Host sends, Controller receives).
/// Signaling payloads (SDP/ICE) are surfaced as events for the caller to wrap in a
/// <see cref="SignedSignal"/> and relay; incoming ones are applied via the Apply* methods after
/// the caller has verified the signature against the paired key.
///
/// This targets SIPSorcery 8.x; a couple of member names can shift between minor versions, so
/// verify against the referenced package when building on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WebRtcPeer : IAsyncDisposable
{
    public const string InputChannelLabel = "input";

    private readonly RTCPeerConnection _pc;
    private RTCDataChannel? _inputChannel;

    public WebRtcPeer(IReadOnlyList<RTCIceServer> iceServers)
    {
        _pc = new RTCPeerConnection(new RTCConfiguration { iceServers = iceServers.ToList() });

        _pc.onicecandidate += c =>
        {
            if (c is not null) IceCandidate?.Invoke(c);
        };
        _pc.onconnectionstatechange += s => ConnectionStateChanged?.Invoke(s);

        // Host creates the channel; Controller receives it.
        _pc.ondatachannel += ch =>
        {
            if (ch.label == InputChannelLabel)
                WireInputChannel(ch);
        };
    }

    public event Action<RTCIceCandidate>? IceCandidate;
    public event Action<RTCPeerConnectionState>? ConnectionStateChanged;
    public event Action<string>? InputReceived;

    public RTCPeerConnectionState State => _pc.connectionState;

    /// <summary>Attach a video track. Host: SendOnly + push encoded samples via the source.</summary>
    public void AddVideoTrack(IVideoSource source, MediaStreamStatusEnum direction)
    {
        var track = new MediaStreamTrack(source.GetVideoSourceFormats(), direction);
        _pc.addTrack(track);

        if (direction is MediaStreamStatusEnum.SendOnly or MediaStreamStatusEnum.SendRecv)
        {
            source.OnVideoSourceEncodedSample += (durationRtpUnits, sample) =>
                _pc.SendVideo(durationRtpUnits, sample);
            _pc.OnVideoFormatsNegotiated += formats =>
            {
                // Bind to VP8 explicitly. The source already restricts to VP8, but never pass
                // whatever landed first (H263, etc.) to the VP8 encoder — it throws on every frame.
                var vp8 = formats.Where(f => f.Codec == VideoCodecsEnum.VP8).ToList();
                if (vp8.Count > 0)
                    source.SetVideoSourceFormat(vp8[0]);
            };
        }
    }

    /// <summary>Forward received encoded video frames to a sink (Controller side).</summary>
    public void OnEncodedVideo(Action<uint, VideoFormat, byte[]> handler)
    {
        _pc.OnVideoFrameReceived += (rep, timestamp, frame, format) => handler(timestamp, format, frame);
    }

    /// <summary>Host side: create the input data channel before making the offer.</summary>
    public async Task CreateInputChannelAsync()
    {
        var ch = await _pc.createDataChannel(InputChannelLabel,
            new RTCDataChannelInit { ordered = true });
        WireInputChannel(ch);
    }

    private void WireInputChannel(RTCDataChannel channel)
    {
        _inputChannel = channel;
        channel.onmessage += (_, _, data) => InputReceived?.Invoke(System.Text.Encoding.UTF8.GetString(data));
    }

    public void SendInput(string json)
    {
        if (_inputChannel is { readyState: RTCDataChannelState.open })
            _inputChannel.send(json);
    }

    public async Task<RTCSessionDescriptionInit> CreateOfferAsync()
    {
        var offer = _pc.createOffer();
        await _pc.setLocalDescription(offer);
        return offer;
    }

    public async Task<RTCSessionDescriptionInit> CreateAnswerAsync()
    {
        var answer = _pc.createAnswer();
        await _pc.setLocalDescription(answer);
        return answer;
    }

    public void ApplyRemoteDescription(RTCSessionDescriptionInit sdp) => _pc.setRemoteDescription(sdp);

    public void ApplyRemoteIceCandidate(RTCIceCandidateInit candidate) => _pc.addIceCandidate(candidate);

    public void Close(string reason) => _pc.Close(reason);

    public ValueTask DisposeAsync()
    {
        _pc.Close("disposed");
        _pc.Dispose();
        return ValueTask.CompletedTask;
    }
}
