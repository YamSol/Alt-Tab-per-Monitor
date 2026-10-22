using MonitorSwitch.Core;
using static MonitorSwitch.Win32.NativeMethods;

namespace MonitorSwitch.Win32;

/// <summary>
/// <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c>, out of context: tells the app which window the
/// switcher activated so it can restore as soon as the switch lands instead of waiting for a timer.
/// Must be created on a thread with a message loop (the UI thread).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly WinEventProc _callback;
    private readonly Action<WindowHandle> _onForeground;
    private nint _hook;

    public ForegroundWatcher(Action<WindowHandle> onForeground)
    {
        _onForeground = onForeground;
        _callback = (_, _, hwnd, _, _, _, _) => _onForeground(new WindowHandle(hwnd));
        _hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, _callback, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWinEvent(_hook);
            _hook = 0;
        }
    }
}
