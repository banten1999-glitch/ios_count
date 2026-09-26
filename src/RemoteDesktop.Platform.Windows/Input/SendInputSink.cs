using System.Runtime.Versioning;
using RemoteDesktop.Core.Input;
using RemoteDesktop.Core.Media;
using static RemoteDesktop.Platform.Windows.Interop.NativeInput;
// Alias the enum: the IInputSink method is also named MouseButton, so a bare
// "MouseButton.Left" inside this class resolves to the method, not the enum.
using CoreMouseButton = RemoteDesktop.Core.Input.MouseButton;

namespace RemoteDesktop.Platform.Windows.Input;

/// <summary>
/// <see cref="IInputSink"/> implementation using the documented SendInput API. Absolute mouse
/// moves are expressed in the 0..65535 virtual-desktop coordinate space so multi-monitor
/// layouts and per-monitor DPI are handled by Windows itself.
///
/// Runs within the process's granted permissions only — no UAC bypass and no attempt to drive
/// protected/secure-desktop surfaces (e.g. the UAC prompt or the logon screen); those simply
/// won't receive injected input, which is the documented, expected limitation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SendInputSink : IInputSink
{
    private readonly DisplayLayout _layout;

    public SendInputSink(DisplayLayout layout) => _layout = layout;

    public void MoveMouse(double normalizedX, double normalizedY, int monitorIndex)
    {
        var (px, py) = _layout.ToPixel(monitorIndex, normalizedX, normalizedY);

        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 1 || vh <= 1) return;

        // Convert absolute pixel to the 0..65535 normalized absolute space over the virtual desktop.
        int ax = (int)Math.Round((px - vx) * 65535.0 / (vw - 1));
        int ay = (int)Math.Round((py - vy) * 65535.0 / (vh - 1));

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = ax,
                    dy = ay,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
                }
            }
        };
        Send(input);
    }

    public void MouseButton(MouseButton button, bool isDown)
    {
        uint flags;
        uint mouseData = 0;
        switch (button)
        {
            case CoreMouseButton.Left: flags = isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
            case CoreMouseButton.Right: flags = isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
            case CoreMouseButton.Middle: flags = isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
            case CoreMouseButton.XButton1:
                flags = isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; mouseData = XBUTTON1; break;
            case CoreMouseButton.XButton2:
                flags = isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; mouseData = XBUTTON2; break;
            default: return;
        }

        Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = mouseData } }
        });
    }

    public void MouseWheel(int deltaX, int deltaY)
    {
        if (deltaY != 0)
            Send(new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL, mouseData = (uint)deltaY } }
            });
        if (deltaX != 0)
            Send(new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_HWHEEL, mouseData = (uint)deltaX } }
            });
    }

    public void Key(int virtualKey, bool isDown)
    {
        uint flags = isDown ? 0 : KEYEVENTF_KEYUP;
        if (IsExtendedKey(virtualKey))
            flags |= KEYEVENTF_EXTENDEDKEY;

        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)virtualKey, dwFlags = flags } }
        });
    }

    private static void Send(INPUT input)
    {
        var arr = new[] { input };
        SendInput(1, arr, INPUT.Size);
    }

    // Extended keys need the extended flag for correct behavior (arrows, nav cluster, etc.).
    private static bool IsExtendedKey(int vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or        // PageUp/PageDown/End/Home
        0x25 or 0x26 or 0x27 or 0x28 or        // arrows
        0x2D or 0x2E or                        // Insert/Delete
        0x5B or 0x5C or 0x5D or                // LWin/RWin/Apps
        0xA3 or 0xA5;                          // RControl/RMenu
}
