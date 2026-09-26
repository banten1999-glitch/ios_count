using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Configuration;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Identity;
using RemoteDesktop.Core.Input;
using RemoteDesktop.Core.Storage;
using RemoteDesktop.Core.Trust;
using RemoteDesktop.Platform.Windows.Input;
using RemoteDesktop.Platform.Windows.Pairing;
using RemoteDesktop.Platform.Windows.Rtc;
using RemoteDesktop.Platform.Windows.Signaling;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using CoreMouseButton = RemoteDesktop.Core.Input.MouseButton;

namespace RemoteDesktop.Controller;

public partial class MainWindow : Window
{
    private readonly string _signalingUrl;
    private readonly string? _stun;
    private readonly DeviceIdentity _identity;
    private readonly ITrustedDeviceStore _trustStore;
    private readonly GlobalDisconnectHotkey _hotkey;

    private SignalingClient? _signaling;
    private ControllerConnectionOrchestrator? _connection;
    private WriteableBitmap? _bitmap;
    private int _monitorIndex;

    public MainWindow()
    {
        InitializeComponent();

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        _signalingUrl = config["Controller:SignalingBaseUrl"] ?? "https://localhost:5001";
        _stun = config["Controller:StunUrl"];

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteDesktopControl", "Controller");
        Directory.CreateDirectory(dataDir);

        ISecretStore secrets = new DpapiSecretStore(Path.Combine(dataDir, "secrets"));
        _identity = DeviceIdentityProvider.LoadOrCreate(secrets);
        _trustStore = new JsonFileTrustedDeviceStore(Path.Combine(dataDir, "known-hosts.json"));

        RemoteDesktop.Platform.Windows.Diagnostics.FileLog.Init(
            Path.Combine(dataDir, "logs", $"controller-{DateTime.Now:yyyyMMdd-HHmmss}.log"));

        _hotkey = new GlobalDisconnectHotkey();
        _hotkey.Pressed += () => Dispatcher.Invoke(() => _ = DisconnectAsync());
        _hotkey.Start();

        PreviewKeyDown += OnKeyDownForward;
        PreviewKeyUp += OnKeyUpForward;
        Closed += (_, _) => { _hotkey.Dispose(); _ = DisconnectAsync(); };
    }

    private async Task<SignalingClient> EnsureSignalingAsync()
    {
        if (_signaling is not null) return _signaling;
        var client = new SignalingClient(new Uri(_signalingUrl));
        await client.RegisterAsync(_identity.Public);
        await client.AuthenticateAsync(_identity);
        await client.ConnectAsync();
        _signaling = client;
        return client;
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        string hostId = HostIdBox.Text.Trim();
        string code = CodeBox.Text.Trim();
        if (hostId.Length == 0 || code.Length == 0) return;

        try
        {
            var signaling = await EnsureSignalingAsync();
            var coordinator = new ControllerPairingCoordinator(_identity, signaling, _trustStore);
            SetStatus("Pairing…");
            var outcome = await coordinator.PairAsync(hostId, code, TimeSpan.FromMinutes(1));
            SetStatus(outcome.Success ? "Paired. You can now Connect." : $"Pairing failed: {outcome.Reason}");
        }
        catch (Exception ex)
        {
            SetStatus("Pairing error: " + ex.Message);
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        string hostId = HostIdBox.Text.Trim();
        var trusted = _trustStore.Find(hostId);
        if (trusted is not { IsActive: true })
        {
            SetStatus("Unknown host. Pair first.");
            return;
        }

        try
        {
            var signaling = await EnsureSignalingAsync();
            var hostKey = DevicePublicKey.FromBase64(trusted.PublicKeyBase64);
            _connection = new ControllerConnectionOrchestrator(_identity, hostKey, signaling, _stun);
            _connection.FrameReceived += OnFrame;
            _connection.ConnectionStateChanged += OnConnectionState;

            SetStatus("Connecting…");
            await _connection.ConnectAsync();
            ConnectBtn.IsEnabled = false;
            DisconnectBtn.IsEnabled = true;
            VideoHost.Focus();
        }
        catch (Exception ex)
        {
            SetStatus("Connect error: " + ex.Message);
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e) => await DisconnectAsync();

    private async Task DisconnectAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        Dispatcher.Invoke(() =>
        {
            ConnectBtn.IsEnabled = true;
            DisconnectBtn.IsEnabled = false;
            SetStatus("Disconnected");
        });
    }

    private void OnConnectionState(RTCPeerConnectionState state)
    {
        RemoteDesktop.Platform.Windows.Diagnostics.FileLog.Info($"Controller: peer state = {state}");
        Dispatcher.Invoke(() => SetStatus(state.ToString()));
    }

    // ---- Video rendering ----

    private bool _firstFrameLogged;
    private void OnFrame(DecodedVideoFrame frame)
    {
        if (!_firstFrameLogged)
        {
            _firstFrameLogged = true;
            RemoteDesktop.Platform.Windows.Diagnostics.FileLog.Info(
                $"Controller: first video frame {frame.Width}x{frame.Height} fmt={frame.PixelFormat}");
        }
        Dispatcher.Invoke(() => Render(frame));
    }

    private void Render(DecodedVideoFrame frame)
    {
        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            VideoImage.Source = _bitmap;
        }

        byte[] bgra = ToBgra32(frame);
        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), bgra, frame.Width * 4, 0);
    }

    private static byte[] ToBgra32(DecodedVideoFrame f)
    {
        // Normalize the decoder's output to BGRA32 for WriteableBitmap.
        if (f.PixelFormat == VideoPixelFormatsEnum.Bgra)
            return f.Sample;

        int px = f.Width * f.Height;
        var outBuf = new byte[px * 4];
        switch (f.PixelFormat)
        {
            case VideoPixelFormatsEnum.Rgb: // 24bpp RGB
                for (int i = 0; i < px; i++)
                {
                    outBuf[i * 4 + 0] = f.Sample[i * 3 + 2];
                    outBuf[i * 4 + 1] = f.Sample[i * 3 + 1];
                    outBuf[i * 4 + 2] = f.Sample[i * 3 + 0];
                    outBuf[i * 4 + 3] = 255;
                }
                break;
            case VideoPixelFormatsEnum.Bgr: // 24bpp BGR
                for (int i = 0; i < px; i++)
                {
                    outBuf[i * 4 + 0] = f.Sample[i * 3 + 0];
                    outBuf[i * 4 + 1] = f.Sample[i * 3 + 1];
                    outBuf[i * 4 + 2] = f.Sample[i * 3 + 2];
                    outBuf[i * 4 + 3] = 255;
                }
                break;
            default:
                return f.Sample; // best effort
        }
        return outBuf;
    }

    // ---- Input capture (Controller -> Host) ----

    private bool TryNormalized(Point p, out double nx, out double ny)
    {
        nx = ny = 0;
        if (_bitmap is null) return false;

        double elemW = VideoImage.ActualWidth, elemH = VideoImage.ActualHeight;
        if (elemW <= 0 || elemH <= 0) return false;

        // Account for Uniform letterboxing: compute the displayed image rectangle.
        double srcAR = (double)_bitmap.PixelWidth / _bitmap.PixelHeight;
        double dstAR = elemW / elemH;
        double dispW, dispH, offX, offY;
        if (srcAR > dstAR) { dispW = elemW; dispH = elemW / srcAR; offX = 0; offY = (elemH - dispH) / 2; }
        else { dispH = elemH; dispW = elemH * srcAR; offY = 0; offX = (elemW - dispW) / 2; }

        double x = (p.X - offX) / dispW;
        double y = (p.Y - offY) / dispH;
        if (x is < 0 or > 1 || y is < 0 or > 1) return false;
        nx = x; ny = y;
        return true;
    }

    private void Video_MouseMove(object sender, MouseEventArgs e)
    {
        if (_connection is null) return;
        if (TryNormalized(e.GetPosition(VideoImage), out double nx, out double ny))
            _connection.SendInput(new MouseMoveEvent { X = nx, Y = ny, MonitorIndex = _monitorIndex });
    }

    private void Video_MouseDown(object sender, MouseButtonEventArgs e)
        => SendButton(e, isDown: true);

    private void Video_MouseUp(object sender, MouseButtonEventArgs e)
        => SendButton(e, isDown: false);

    private void SendButton(MouseButtonEventArgs e, bool isDown)
    {
        if (_connection is null) return;
        VideoHost.Focus();
        var btn = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Left => CoreMouseButton.Left,
            System.Windows.Input.MouseButton.Right => CoreMouseButton.Right,
            System.Windows.Input.MouseButton.Middle => CoreMouseButton.Middle,
            System.Windows.Input.MouseButton.XButton1 => CoreMouseButton.XButton1,
            System.Windows.Input.MouseButton.XButton2 => CoreMouseButton.XButton2,
            _ => (CoreMouseButton?)null
        };
        if (btn is null) return;
        _connection.SendInput(new MouseButtonEvent { Button = btn.Value, IsDown = isDown });
    }

    private void Video_MouseWheel(object sender, MouseWheelEventArgs e)
        => _connection?.SendInput(new MouseWheelEvent { DeltaY = e.Delta });

    private void OnKeyDownForward(object sender, KeyEventArgs e)
    {
        if (_connection is null || IsTypingInBox()) return;
        e.Handled = true;
        _connection.SendInput(new KeyEvent { VirtualKey = KeyInterop.VirtualKeyFromKey(RealKey(e)), IsDown = true });
    }

    private void OnKeyUpForward(object sender, KeyEventArgs e)
    {
        if (_connection is null || IsTypingInBox()) return;
        e.Handled = true;
        _connection.SendInput(new KeyEvent { VirtualKey = KeyInterop.VirtualKeyFromKey(RealKey(e)), IsDown = false });
    }

    private static Key RealKey(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    private bool IsTypingInBox()
        => HostIdBox.IsKeyboardFocusWithin || CodeBox.IsKeyboardFocusWithin;

    private void SetStatus(string text) => StatusText.Text = text;
}
