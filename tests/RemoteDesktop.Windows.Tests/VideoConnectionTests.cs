using RemoteDesktop.Core.Media;
using RemoteDesktop.Platform.Windows.Capture;
using RemoteDesktop.Platform.Windows.Rtc;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Vpx.Net;
using Xunit;

namespace RemoteDesktop.Windows.Tests;

public class VideoConnectionTests
{
    [Fact]
    public async Task ProductionVideoPath_NegotiatesAndDeliversDecodedFrame()
    {
        // Use the production capture bridge and decoder with a synthetic frame: no desktop
        // access is needed, but SDP, ICE, DTLS, RTP, VP8 encoding and decoding all run.
        using var capturer = new DesktopDuplicationCapturer();
        using var source = new ScreenVideoSource(capturer,
            new AdaptiveQualityController(QualityLadder.Default(), startIndex: 1));
        using var decoder = new VideoDecodeBridge();
        Assert.Contains(source.Source.GetVideoSourceFormats(), f => f.Codec == VideoCodecsEnum.VP8);
        Assert.Contains(decoder.Source.GetVideoSourceFormats(), f => f.Codec == VideoCodecsEnum.VP8);
        await using var host = new WebRtcPeer(Array.Empty<RTCIceServer>());
        await using var controller = new WebRtcPeer(Array.Empty<RTCIceServer>());
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var decoded = new TaskCompletionSource<DecodedVideoFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool negotiated = false;
        host.AddVideoTrack(source.Source, MediaStreamStatusEnum.SendOnly, () => negotiated = true);
        controller.AddVideoTrack(decoder.Source, MediaStreamStatusEnum.RecvOnly);
        controller.OnEncodedVideo(decoder.OnEncodedFrame);
        decoder.DecodedFrame += f => decoded.TrySetResult(f);
        host.IceCandidate += c => controller.ApplyRemoteIceCandidate(new RTCIceCandidateInit
            { candidate = c.candidate, sdpMid = c.sdpMid, sdpMLineIndex = c.sdpMLineIndex });
        controller.IceCandidate += c => host.ApplyRemoteIceCandidate(new RTCIceCandidateInit
            { candidate = c.candidate, sdpMid = c.sdpMid, sdpMLineIndex = c.sdpMLineIndex });
        host.ConnectionStateChanged += s => { if (s == RTCPeerConnectionState.connected) connected.TrySetResult(true); };
        await controller.CreateInputChannelAsync();
        var offer = await controller.CreateOfferAsync();
        Assert.Contains("VP8/90000", offer.sdp);
        host.ApplyRemoteDescription(offer);
        Assert.True(negotiated, "Sending VP8 format must be negotiated before capture.");
        controller.ApplyRemoteDescription(await host.CreateAnswerAsync());
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(25));
        var endpoint = Assert.IsType<Vp8NetVideoEncoderEndPoint>(source.Source);
        var pixels = Enumerable.Repeat((byte)160, 64 * 64 * 4).ToArray();
        // Retry a keyframe to cover either peer completing DTLS a moment before the other.
        for (int i = 0; i < 10 && !decoded.Task.IsCompleted; i++)
        {
            endpoint.ForceKeyFrame();
            endpoint.ExternalVideoSourceRawSample(33, 64, 64, pixels, VideoPixelFormatsEnum.Bgra);
            await Task.WhenAny(decoded.Task, Task.Delay(200));
        }
        var frame = await decoded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(64, frame.Width);
        Assert.Equal(64, frame.Height);
        Assert.NotEmpty(frame.Sample);
    }

    [Fact]
    public async Task RejectedSdp_IsReportedInsteadOfSilentlyWaiting()
    {
        await using var peer = new WebRtcPeer(Array.Empty<RTCIceServer>());
        Assert.ThrowsAny<Exception>(() => peer.ApplyRemoteDescription(new RTCSessionDescriptionInit
            { type = RTCSdpType.offer, sdp = "v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n" }));
    }
}
