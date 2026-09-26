using System.Runtime.Versioning;
using static RemoteDesktop.Platform.Windows.Interop.NativeWindowing;

namespace RemoteDesktop.Platform.Windows.Input;

/// <summary>
/// Registers a system-wide hotkey (default Ctrl+Alt+9) on the Controller to immediately end
/// the active session. It runs a message-only window on its own thread so the hotkey works
/// regardless of which window has focus. Raising <see cref="Pressed"/> triggers the teardown
/// path (send Bye, release input remotely, close media).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalDisconnectHotkey : IDisposable
{
    private const int HotkeyId = 0xB9;   // arbitrary, unique within this process
    private const uint VK_9 = 0x39;

    private readonly uint _modifiers;
    private readonly uint _vk;
    private Thread? _thread;
    private IntPtr _hwnd;
    private volatile bool _running;
    // Keep the delegate alive for the lifetime of the window so the GC can't collect it.
    private WndProc? _wndProc;

    public event Action? Pressed;

    public GlobalDisconnectHotkey(uint modifiers = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, uint vk = VK_9)
    {
        _modifiers = modifiers;
        _vk = vk;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "rdc-hotkey" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void RunMessageLoop()
    {
        const string className = "RdcHotkeyWindow";
        _wndProc = WndProcImpl;
        var wc = new WNDCLASS
        {
            lpfnWndProc = _wndProc,
            hInstance = GetModuleHandleW(null),
            lpszClassName = className
        };
        RegisterClassW(ref wc);

        _hwnd = CreateWindowExW(0, className, null, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            return;

        if (!RegisterHotKey(_hwnd, HotkeyId, _modifiers, _vk))
        {
            // Another app may hold this combination; caller can surface this via a status message.
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            return;
        }

        while (_running && GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY && (int)msg.wParam == HotkeyId)
            {
                try { Pressed?.Invoke(); } catch { /* never let a handler kill the loop */ }
            }
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DESTROY)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        _running = false;
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
