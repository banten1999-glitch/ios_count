namespace RemoteDesktop.Core.Media;

/// <summary>
/// The set of displays available on the Host, with helpers to pick a target and map remote
/// coordinates. Supports multi-monitor and mixed DPI (each display carries its own scale).
/// </summary>
public sealed class DisplayLayout
{
    private readonly IReadOnlyList<DisplayInfo> _displays;

    public DisplayLayout(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(displays));
        _displays = displays;
    }

    public IReadOnlyList<DisplayInfo> Displays => _displays;

    public DisplayInfo Primary => _displays.FirstOrDefault(d => d.IsPrimary) ?? _displays[0];

    /// <summary>Select a display by index, falling back to the primary if out of range.</summary>
    public DisplayInfo Select(int monitorIndex)
        => _displays.FirstOrDefault(d => d.Index == monitorIndex) ?? Primary;

    /// <summary>Map a normalized point on the chosen monitor to an absolute virtual-desktop pixel.</summary>
    public (int X, int Y) ToPixel(int monitorIndex, double normalizedX, double normalizedY)
        => Select(monitorIndex).ToPixel(normalizedX, normalizedY);
}
