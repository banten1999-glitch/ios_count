namespace RemoteDesktop.Platform.Windows.Capture;

/// <summary>
/// A captured frame's pixels in BGRA (8-8-8-8) with its row stride. Fed to the video encoder
/// pipeline. <paramref name="Data"/> is valid only for the duration of the raise; consumers
/// must copy if they retain it.
/// </summary>
public sealed record RawBgraFrame(
    byte[] Data,
    int Width,
    int Height,
    int Stride,
    long TimestampMs,
    int MonitorIndex);
