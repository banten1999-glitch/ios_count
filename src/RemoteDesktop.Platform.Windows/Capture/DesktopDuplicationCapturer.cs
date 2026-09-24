using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RemoteDesktop.Core.Media;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RemoteDesktop.Platform.Windows.Capture;

/// <summary>
/// Screen capturer using DXGI Desktop Duplication (a documented API). Captures the selected
/// monitor into CPU-readable BGRA frames on a background thread, coalescing to the latest
/// frame. Handles the two normal transient conditions — no new frame (timeout) and access
/// lost (resolution change / mode switch / secure desktop) — by retrying and recreating the
/// duplication as needed.
///
/// Note: while the secure desktop (UAC prompt, logon) is showing, duplication reports access
/// lost and no frames are produced. That is the documented, expected limitation; we surface a
/// blank/last frame rather than attempting any unsupported workaround.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class DesktopDuplicationCapturer : IScreenCapturer
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();

    private int _monitorIndex;
    private volatile bool _running;
    private Thread? _thread;

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private int _width, _height;

    public event Action<CapturedFrame>? FrameCaptured;
    public event Action<RawBgraFrame>? RawFrameReady;

    public IReadOnlyList<DisplayInfo> GetDisplays() => DisplayEnumerator.Enumerate().Displays;

    public void SelectDisplay(int monitorIndex)
    {
        lock (_gate)
        {
            _monitorIndex = monitorIndex;
            // Force re-init on the capture thread by dropping the current duplication.
            DisposeDuplication();
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "rdc-capture" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        lock (_gate)
        {
            DisposeDuplication();
            _context?.Dispose(); _context = null;
            _device?.Dispose(); _device = null;
        }
    }

    private void CaptureLoop()
    {
        while (_running)
        {
            try
            {
                if (_duplication is null && !TryInitDuplication())
                {
                    Thread.Sleep(200); // monitor not ready / access lost; back off and retry
                    continue;
                }
                CaptureOne();
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                // Access lost or device removed — rebuild on next iteration.
                lock (_gate) DisposeDuplication();
                Thread.Sleep(100);
            }
        }
    }

    private bool TryInitDuplication()
    {
        lock (_gate)
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            IDXGIAdapter1? chosenAdapter = null;
            IDXGIOutput? chosenOutput = null;
            int outputCounter = 0;

            for (int a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            {
                for (int o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    if (outputCounter == _monitorIndex)
                    {
                        chosenAdapter = adapter;
                        chosenOutput = output;
                    }
                    else
                    {
                        output.Dispose();
                    }
                    outputCounter++;
                }
                if (chosenAdapter is null) adapter.Dispose();
            }

            if (chosenAdapter is null || chosenOutput is null)
            {
                chosenOutput?.Dispose();
                chosenAdapter?.Dispose();
                return false;
            }

            try
            {
                if (_device is null)
                {
                    D3D11.D3D11CreateDevice(
                        chosenAdapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                        new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 },
                        out _device!, out _context!);
                }

                using var output1 = chosenOutput.QueryInterface<IDXGIOutput1>();
                var desc = chosenOutput.Description;
                _width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
                _height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

                _duplication = output1.DuplicateOutput(_device!);
                CreateStaging();
                return true;
            }
            finally
            {
                chosenOutput.Dispose();
                chosenAdapter.Dispose();
            }
        }
    }

    private void CreateStaging()
    {
        _staging?.Dispose();
        _staging = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });
    }

    private void CaptureOne()
    {
        IDXGIResource? desktopResource = null;
        try
        {
            var result = _duplication!.AcquireNextFrame(150, out _, out desktopResource);
            if (result == Vortice.DXGI.ResultCode.WaitTimeout)
                return; // no change since last frame — nothing to send
            result.CheckError();

            using var tex = desktopResource!.QueryInterface<ID3D11Texture2D>();
            _context!.CopyResource(_staging!, tex);

            var map = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                int stride = (int)map.RowPitch;
                var data = new byte[stride * _height];
                Marshal.Copy(map.DataPointer, data, 0, data.Length);

                long ts = _clock.ElapsedMilliseconds;
                RawFrameReady?.Invoke(new RawBgraFrame(data, _width, _height, stride, ts, _monitorIndex));
                FrameCaptured?.Invoke(new CapturedFrame(_width, _height, ts, _monitorIndex));
            }
            finally
            {
                _context.Unmap(_staging!, 0);
            }
        }
        finally
        {
            desktopResource?.Dispose();
            _duplication?.ReleaseFrame();
        }
    }

    private void DisposeDuplication()
    {
        _staging?.Dispose(); _staging = null;
        _duplication?.Dispose(); _duplication = null;
    }

    public void Dispose() => Stop();
}
