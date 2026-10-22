using MonitorSwitch.Core;

namespace MonitorSwitch.Win32;

/// <summary><c>RegisterHotKey</c> on a window the caller owns; the window receives <see cref="Message"/>.</summary>
public static class GlobalHotkey
{
    public const int Message = NativeMethods.WM_HOTKEY;

    public const int QueryEndSession = NativeMethods.WM_QUERYENDSESSION;

    public const int EndSession = NativeMethods.WM_ENDSESSION;

    private const uint ModNoRepeat = 0x4000;

    public static bool Register(nint hwnd, int id, Hotkey hotkey) =>
        NativeMethods.RegisterHotKey(hwnd, id, (uint)hotkey.Modifiers | ModNoRepeat, (uint)hotkey.VirtualKey);

    public static void Unregister(nint hwnd, int id) => NativeMethods.UnregisterHotKey(hwnd, id);
}
