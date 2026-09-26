using System.Windows;

namespace RemoteDesktop.Host;

/// <summary>Displays the Host id and the current pairing code for the owner to relay.</summary>
public partial class PairingWindow : Window
{
    public PairingWindow(string hostDeviceId, string code)
    {
        InitializeComponent();
        HostIdBox.Text = hostDeviceId;
        CodeBox.Text = code;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
