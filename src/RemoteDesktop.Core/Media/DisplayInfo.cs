namespace RemoteDesktop.Core.Media;

/// <summary>
/// A capturable display (monitor). Bounds are in virtual-desktop pixels; <see cref="ScaleFactor"/>
/// is the per-monitor DPI scale (1.0 = 96 DPI, 1.5 = 150%, …) so the Controller can map
/// normalized coordinates back to the correct physical pixel on multi-DPI setups.
/// </summary>
public sealed record DisplayInfo(
    int Index,
    string DeviceName,
    int X,
    int Y,
    int Width,
    int Height,
    double ScaleFactor,
    bool IsPrimary)
{
    /// <summary>Map a normalized [0,1] point on this display to an absolute virtual-desktop pixel.</summary>
    public (int X, int Y) ToPixel(double normalizedX, double normalizedY)
    {
        double cx = Math.Clamp(normalizedX, 0, 1);
        double cy = Math.Clamp(normalizedY, 0, 1);
        // width-1 so nx=1.0 lands on the last addressable column, not one past it.
        int px = X + (int)Math.Round(cx * (Width - 1));
        int py = Y + (int)Math.Round(cy * (Height - 1));
        return (px, py);
    }
}
