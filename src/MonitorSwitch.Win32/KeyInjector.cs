using System.Runtime.InteropServices;
using static MonitorSwitch.Win32.NativeMethods;

namespace MonitorSwitch.Win32;

/// <summary>Re-sends the Tab that the hook swallowed, after hiding is done.</summary>
public static class KeyInjector
{
    private const ushort VkTab = 0x09;
    private const ushort VkShift = 0x10;
    private const ushort VkMenu = 0x12;

    public static bool IsAltHeld() => (GetAsyncKeyState(VkMenu) & 0x8000) != 0;

    /// <summary>
    /// Alt still held: send Tab, which opens the system switcher over the remaining windows.
    /// Alt already released (a quick tap): replay the whole Alt(+Shift)+Tab so the quick switch
    /// still happens, now limited to the current monitor.
    /// </summary>
    public static void ReplayTab(bool reverse)
    {
        var keys = new List<INPUT>();
        var altHeld = IsAltHeld();
        if (!altHeld)
        {
            keys.Add(Key(VkMenu, up: false));
            if (reverse)
            {
                keys.Add(Key(VkShift, up: false));
            }
        }

        keys.Add(Key(VkTab, up: false));
        keys.Add(Key(VkTab, up: true));

        if (!altHeld)
        {
            if (reverse)
            {
                keys.Add(Key(VkShift, up: true));
            }

            keys.Add(Key(VkMenu, up: true));
        }

        SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
                dwExtraInfo = KeyboardHook.InjectionMarker,
            },
        },
    };
}
