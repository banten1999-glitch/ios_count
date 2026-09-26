using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Identity;
using RemoteDesktop.Core.Pairing;
using RemoteDesktop.Core.Session;
using RemoteDesktop.Core.Storage;
using RemoteDesktop.Core.Trust;
using RemoteDesktop.Platform.Windows.Pairing;
using RemoteDesktop.Platform.Windows.Rtc;
using RemoteDesktop.Platform.Windows.Signaling;
using Forms = System.Windows.Forms;

namespace RemoteDesktop.Host;

/// <summary>
/// Host application entry point. Runs in the background (tray only): loads the device identity
/// from DPAPI-protected storage, connects to the signaling service, and serves unattended
/// connections from paired devices while always showing the mandatory session indicator.
/// </summary>
public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _tray;
    private IndicatorWindow? _indicatorWindow;
    private MandatorySessionIndicator? _indicator;
    private SignalingClient? _signaling;
    private HostConnectionOrchestrator? _connection;
    private HostPairingCoordinator? _pairingCoordinator;
    private HostPairingService? _pairingService;
    private DeviceIdentity? _identity;
    private TrustManager? _trust;
    private ITrustedDeviceStore? _trustStore;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Diagnostics + global exception handling so a background error is logged, not fatal.
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteDesktopControl", "Host", "logs");
        RemoteDesktop.Platform.Windows.Diagnostics.FileLog.Init(
            Path.Combine(logDir, $"host-{DateTime.Now:yyyyMMdd-HHmmss}.log"));

        DispatcherUnhandledException += (_, ex) =>
        {
            RemoteDesktop.Platform.Windows.Diagnostics.FileLog.Error("UI-thread exception", ex.Exception);
            ex.Handled = true; // keep the tray app alive
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            RemoteDesktop.Platform.Windows.Diagnostics.FileLog.Error("Domain exception", ex.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            RemoteDesktop.Platform.Windows.Diagnostics.FileLog.Error("Unobserved task exception", ex.Exception);
            ex.SetObserved();
        };

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        string signalingUrl = config["Host:SignalingBaseUrl"] ?? "https://localhost:5001";
        string? stun = config["Host:StunUrl"];

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteDesktopControl", "Host");
        Directory.CreateDirectory(dataDir);

        // Identity (private key protected by DPAPI) + trusted-device list.
        ISecretStore secrets = new DpapiSecretStore(Path.Combine(dataDir, "secrets"));
        _identity = DeviceIdentityProvider.LoadOrCreate(secrets);
        _trustStore = new JsonFileTrustedDeviceStore(Path.Combine(dataDir, "trusted-devices.json"));
        _trust = new TrustManager(_trustStore);
        _pairingService = new HostPairingService(_identity, _trustStore);

        // Mandatory, always-visible session indicator.
        _indicatorWindow = new IndicatorWindow();
        _indicator = new MandatorySessionIndicator(_indicatorWindow);

        // Signaling: register public key, authenticate (proof of possession), open relay socket.
        _signaling = new SignalingClient(new Uri(signalingUrl));
        try
        {
            await _signaling.RegisterAsync(_identity.Public);
            await _signaling.AuthenticateAsync(_identity);
            await _signaling.ConnectAsync();
        }
        catch (Exception ex)
        {
            Forms.MessageBox.Show($"Could not reach the signaling service:\n{ex.Message}",
                "Remote Desktop Host", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
        }

        _connection = new HostConnectionOrchestrator(_identity, _trust, _signaling, _indicator, stun);
        _pairingCoordinator = new HostPairingCoordinator(_identity, _pairingService, _signaling, ConfirmPairingAsync);

        SetupTray();
    }

    private Task<bool> ConfirmPairingAsync(string controllerDeviceId)
    {
        // The mandatory local confirmation: the owner must approve on THIS machine.
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.Invoke(() =>
        {
            RemoteDesktop.Platform.Windows.Diagnostics.FileLog.Info("Host: showing pairing confirmation dialog");
            // Use a hidden top-most owner so the prompt always comes to the foreground and is
            // never buried behind the Controller/other windows.
            using var owner = new Forms.Form
            {
                TopMost = true,
                ShowInTaskbar = false,
                StartPosition = Forms.FormStartPosition.CenterScreen,
                Width = 1,
                Height = 1,
                FormBorderStyle = Forms.FormBorderStyle.None
            };
            owner.Show();
            owner.BringToFront();
            owner.Activate();

            var result = Forms.MessageBox.Show(owner,
                $"Allow this device to pair for unattended access?\n\nDevice: {controllerDeviceId}",
                "Confirm Pairing", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Question);
            owner.Hide();
            tcs.SetResult(result == Forms.DialogResult.Yes);
        });
        return tcs.Task;
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = $"Remote Desktop Host ({_identity!.DeviceId})"
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Pair new device…", null, (_, _) => ShowPairingWindow());
        menu.Items.Add("Trusted devices…", null, (_, _) => ShowTrustedDevices());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
    }

    private void ShowPairingWindow()
    {
        var code = _pairingCoordinator!.BeginPairing(TimeSpan.FromMinutes(3));
        var window = new PairingWindow(_identity!.DeviceId, code.Display());
        window.Show();
    }

    private void ShowTrustedDevices()
    {
        var window = new TrustedDevicesWindow(_trust!, _trustStore!);
        window.Show();
    }

    private async void ExitApp()
    {
        if (_tray is not null) _tray.Visible = false;
        if (_connection is not null) await _connection.DisposeAsync();
        if (_signaling is not null) await _signaling.DisposeAsync();
        _identity?.Dispose();
        Shutdown();
    }
}
