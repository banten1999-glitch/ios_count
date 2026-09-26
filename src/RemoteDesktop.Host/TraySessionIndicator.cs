using System.Windows.Threading;
using RemoteDesktop.Core.Session;
using Forms = System.Windows.Forms;

namespace RemoteDesktop.Host;

/// <summary>Local session visibility and notification without a desktop overlay.</summary>
internal sealed class TraySessionIndicator(Forms.NotifyIcon tray, Dispatcher dispatcher) : ISessionIndicatorView
{
    private bool _active;

    public void Show(IReadOnlyList<SessionIndicatorInfo> sessions) => dispatcher.Invoke(() =>
    {
        tray.Icon = System.Drawing.SystemIcons.Warning;
        tray.Text = "Remote Desktop Host — remote session active";
        if (!_active)
            tray.ShowBalloonTip(4000, "Remote session started",
                "A paired device is connected. Use the tray menu to disconnect.", Forms.ToolTipIcon.Info);
        _active = true;
    });

    public void Hide() => dispatcher.Invoke(() =>
    {
        tray.Icon = System.Drawing.SystemIcons.Application;
        tray.Text = "Remote Desktop Host — no active session";
        _active = false;
    });
}
