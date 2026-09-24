using System.Runtime.Versioning;
using RemoteDesktop.Core.Media;
using static RemoteDesktop.Platform.Windows.Interop.NativeDisplay;

namespace RemoteDesktop.Platform.Windows.Capture;

/// <summary>
/// Enumerates attached monitors into <see cref="DisplayInfo"/>, including per-monitor effective
/// DPI so mixed-DPI multi-monitor setups map coordinates correctly.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class DisplayEnumerator
{
    public static DisplayLayout Enumerate()
    {
        var displays = new List<DisplayInfo>();
        int index = 0;

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref RECT lprc, IntPtr data)
        {
            var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfoW(hMonitor, ref mi))
                return true;

            double scale = 1.0;
            if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                scale = dpiX / 96.0;

            var r = mi.rcMonitor;
            displays.Add(new DisplayInfo(
                index++,
                mi.szDevice,
                r.left,
                r.top,
                r.right - r.left,
                r.bottom - r.top,
                scale,
                (mi.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);

        if (displays.Count == 0)
            displays.Add(new DisplayInfo(0, "PRIMARY", 0, 0, 1920, 1080, 1.0, true));

        return new DisplayLayout(displays);
    }
}
