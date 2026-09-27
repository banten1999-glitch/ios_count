namespace RemoteDesktop.Platform.Windows.Capture;

/// <summary>
/// A captured frame's pixels in BGRA (8-8-8-8) with its row stride. Fed to the video encoder
/// pipeline. <paramref name="Data"/> owns an immutable managed byte array; consumers may retain
/// the frame while encoding asynchronously.
/// </summary>
public sealed record RawBgraFrame(
    byte[] Data,
    int Width,
    int Height,
    int Stride,
    long TimestampMs,
    int MonitorIndex);
