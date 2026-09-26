using System.Runtime.Versioning;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace RemoteDesktop.Platform.Windows.Rtc;

/// <summary>
/// Controller-side decoder: takes encoded video frames received on the peer connection and
/// raises decoded BGRA frames for the WPF viewer to draw. Wraps SIPSorcery's video endpoint in
/// decode mode.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VideoDecodeBridge : IDisposable
{
    private readonly VideoEncoderEndPoint _decoder = new();

    public VideoDecodeBridge()
    {
        // Advertise VP8 only so SDP negotiation converges on VP8 (matching the Host encoder) and
        // never falls back to a codec this managed endpoint can't handle (e.g. H263).
        _decoder.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);
        _decoder.OnVideoSinkDecodedSample += (sample, width, height, stride, pixelFormat) =>
            DecodedFrame?.Invoke(new DecodedVideoFrame(sample, (int)width, (int)height, stride, pixelFormat));
    }

    /// <summary>Raised for each decoded frame, in the endpoint's decoded pixel format.</summary>
    public event Action<DecodedVideoFrame>? DecodedFrame;

    /// <summary>The source formats to advertise on the receive-only track.</summary>
    public IVideoSource Source => _decoder;

    /// <summary>Feed an encoded frame received from the peer connection.</summary>
    public void OnEncodedFrame(uint timestamp, VideoFormat format, byte[] encoded)
        => _decoder.GotVideoFrame(null, timestamp, encoded, format);

    public void Dispose() => _decoder.Dispose();
}

/// <summary>A decoded frame ready to render.</summary>
public sealed record DecodedVideoFrame(byte[] Sample, int Width, int Height, int Stride, VideoPixelFormatsEnum PixelFormat);
