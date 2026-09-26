namespace RemoteDesktop.Core.Media;

/// <summary>Metadata for one captured frame (pixels are handled by the platform pipeline).</summary>
public sealed record CapturedFrame(int Width, int Height, long TimestampMs, int MonitorIndex);

/// <summary>
/// Platform abstraction for screen capture on the Host. The Windows implementation uses
/// Windows.Graphics.Capture (preferred) or Desktop Duplication, both documented APIs. Kept
/// as an interface so display selection and pacing logic are testable without Windows.
/// </summary>
public interface IScreenCapturer : IDisposable
{
    /// <summary>Enumerate the currently attached displays.</summary>
    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>Select which display to capture.</summary>
    void SelectDisplay(int monitorIndex);

    /// <summary>Raised when a new frame is available. The pipeline decides whether to send it.</summary>
    event Action<CapturedFrame>? FrameCaptured;

    void Start();
    void Stop();
}
