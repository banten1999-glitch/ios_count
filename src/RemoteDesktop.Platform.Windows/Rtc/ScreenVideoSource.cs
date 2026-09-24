using System.Runtime.Versioning;
using RemoteDesktop.Core.Media;
using RemoteDesktop.Platform.Windows.Capture;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace RemoteDesktop.Platform.Windows.Rtc;

/// <summary>
/// Bridges the Desktop Duplication capturer to a SIPSorcery video source. Raw BGRA frames are
/// paced (<see cref="FramePacer"/>) to avoid backlog and repacked to tightly-packed rows before
/// being handed to the encoder. Frame rate follows the <see cref="AdaptiveQualityController"/>.
///
/// Encoder: VP8 via SIPSorceryMedia.Encoders for this initial working version — it is managed
/// and avoids native FFmpeg build friction. docs/LIBRARIES.md records this as a deliberate
/// deviation from the H.264/Media-Foundation first choice, to be revisited for hardware accel.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenVideoSource : IDisposable
{
    private readonly DesktopDuplicationCapturer _capturer;
    private readonly VideoEncoderEndPoint _encoder = new();
    private readonly FramePacer _pacer;
    private readonly AdaptiveQualityController _quality;
    private long _lastFrameMs;

    public ScreenVideoSource(DesktopDuplicationCapturer capturer, AdaptiveQualityController quality)
    {
        _capturer = capturer;
        _quality = quality;
        _pacer = new FramePacer(quality.Current.FrameRate);
        _capturer.RawFrameReady += OnRawFrame;
    }

    /// <summary>The SIPSorcery video source to attach to the peer connection (SendOnly).</summary>
    public IVideoSource Source => _encoder;

    /// <summary>Feed the latest bandwidth estimate to adapt frame rate/bitrate.</summary>
    public void ReportBandwidth(double estimatedKbps)
    {
        var rung = _quality.Update(estimatedKbps);
        _pacer.SetTargetFrameRate(rung.FrameRate);
    }

    private void OnRawFrame(RawBgraFrame frame)
    {
        if (!_pacer.TryAdmit(frame.TimestampMs))
            return; // dropped to prevent buildup

        try
        {
            long durationMs = _lastFrameMs == 0 ? 33 : Math.Max(1, frame.TimestampMs - _lastFrameMs);
            _lastFrameMs = frame.TimestampMs;

            byte[] packed = Pack(frame);
            _encoder.ExternalVideoSourceRawSample(
                (uint)durationMs, frame.Width, frame.Height, packed, VideoPixelFormatsEnum.Bgra);
        }
        finally
        {
            _pacer.CompleteSend();
        }
    }

    /// <summary>Repack to stride == width*4 when the capture stride has padding.</summary>
    private static byte[] Pack(RawBgraFrame frame)
    {
        int tight = frame.Width * 4;
        if (frame.Stride == tight)
            return frame.Data;

        var packed = new byte[tight * frame.Height];
        for (int y = 0; y < frame.Height; y++)
            Buffer.BlockCopy(frame.Data, y * frame.Stride, packed, y * tight, tight);
        return packed;
    }

    public void Dispose()
    {
        _capturer.RawFrameReady -= OnRawFrame;
        _encoder.Dispose();
    }
}
