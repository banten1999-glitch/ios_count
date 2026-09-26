using System.Windows;
using RemoteDesktop.Core.Session;

namespace RemoteDesktop.Host;

/// <summary>
/// The always-on-top session indicator badge. Implements <see cref="ISessionIndicatorView"/>;
/// it is shown for the entire lifetime of any active session and cannot be dismissed by the
/// connecting party — the transparency guarantee. It ignores hit-testing so it never blocks the
/// local user, but it is always visible.
/// </summary>
public partial class IndicatorWindow : Window, ISessionIndicatorView
{
    public IndicatorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PositionTopCenter();
    }

    public void Show(IReadOnlyList<SessionIndicatorInfo> activeSessions)
    {
        Dispatcher.Invoke(() =>
        {
            Label.Text = activeSessions.Count == 1
                ? $"Remote session active — {activeSessions[0].PeerDisplayName}"
                : $"Remote sessions active — {activeSessions.Count} connected";
            if (!IsVisible) Show();
            PositionTopCenter();
        });
    }

    public new void Hide()
    {
        Dispatcher.Invoke(() => base.Hide());
    }

    private void PositionTopCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + 8;
    }
}
