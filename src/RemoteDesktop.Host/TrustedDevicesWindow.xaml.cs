using System.Windows;
using RemoteDesktop.Core.Trust;

namespace RemoteDesktop.Host;

/// <summary>Lists trusted devices and lets the owner revoke any of them locally (T15).</summary>
public partial class TrustedDevicesWindow : Window
{
    private readonly TrustManager _trust;
    private readonly ITrustedDeviceStore _store;

    public TrustedDevicesWindow(TrustManager trust, ITrustedDeviceStore store)
    {
        InitializeComponent();
        _trust = trust;
        _store = store;
        Refresh();
    }

    private void Refresh()
    {
        Grid.ItemsSource = _store.List().Select(d => new
        {
            d.DisplayName,
            d.DeviceId,
            PairedAt = d.PairedAt.LocalDateTime.ToString("g"),
            Status = d.IsActive ? "Active" : "Revoked"
        }).ToList();
    }

    private void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is null) return;
        string deviceId = (string)Grid.SelectedItem.GetType().GetProperty("DeviceId")!.GetValue(Grid.SelectedItem)!;
        _trust.Revoke(deviceId);
        Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
