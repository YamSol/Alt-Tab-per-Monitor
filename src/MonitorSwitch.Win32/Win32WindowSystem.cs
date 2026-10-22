using System.Diagnostics;
using MonitorSwitch.Core;
using static MonitorSwitch.Win32.NativeMethods;

namespace MonitorSwitch.Win32;

/// <summary>
/// <see cref="IWindowSystem"/> over user32. Calls that target other processes' windows use the
/// async variants (<c>ShowWindowAsync</c>, <c>SWP_ASYNCWINDOWPOS</c>) so a hung app cannot block the tool.
/// </summary>
public sealed class Win32WindowSystem : IWindowSystem
{
    private readonly VirtualDesktops _desktops;
    private readonly bool _detailed;

    /// <param name="detailed">
    /// Resolve the virtual desktop and process name of every visible window, not just of hiding
    /// candidates. Slower; for reports (<c>--list</c>, the tray's plan view), not for Alt+Tab.
    /// </param>
    public Win32WindowSystem(VirtualDesktops desktops, bool detailed = false)
    {
        _desktops = desktops;
        _detailed = detailed;
    }

    public IReadOnlyList<WindowInfo> EnumerateWindows()
    {
        var handles = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            handles.Add(hwnd);
            return true;
        }, 0);

        var result = new List<WindowInfo>(handles.Count);
        for (var i = 0; i < handles.Count; i++)
        {
            if (Describe(handles[i], i, i == 0 ? 0 : handles[i - 1]) is { } info)
            {
                result.Add(info);
            }
        }

        return result;
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint hMonitor, nint _, ref RECT _, nint _) =>
        {
            var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add(new MonitorInfo(
                    new MonitorHandle(hMonitor),
                    info.szDevice,
                    ToRect(info.rcMonitor),
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }

            return true;
        }, 0);
        return monitors;
    }

    public MonitorHandle GetCursorMonitor()
    {
        GetCursorPos(out var point);
        return new MonitorHandle(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST));
    }

    public bool IsWindow(WindowHandle window) => NativeMethods.IsWindow(window.Value);

    public uint GetProcessId(WindowHandle window)
    {
        GetWindowThreadProcessId(window.Value, out var pid);
        return pid;
    }

    public bool IsVisible(WindowHandle window) => IsWindowVisible(window.Value);

    public WindowPlacement? GetPlacement(WindowHandle window)
    {
        var wp = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(window.Value, ref wp))
        {
            return null;
        }

        return new WindowPlacement(
            (ShowCommand)wp.showCmd,
            new Core.Point(wp.ptMinPosition.X, wp.ptMinPosition.Y),
            new Core.Point(wp.ptMaxPosition.X, wp.ptMaxPosition.Y),
            ToRect(wp.rcNormalPosition),
            wp.flags);
    }

    public bool Hide(WindowHandle window) => ShowWindowAsync(window.Value, SW_HIDE);

    public bool WaitUntilHidden(IReadOnlyCollection<WindowHandle> windows, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (windows.All(w => !IsWindowVisible(w.Value)))
            {
                return true;
            }

            if (clock.Elapsed >= timeout)
            {
                return false;
            }

            Thread.Sleep(1);
        }
    }

    public bool Show(WindowHandle window, WindowPlacement placement, WindowHandle insertAfter)
    {
        var hwnd = window.Value;
        bool shown;
        if (GetPlacement(window) != placement)
        {
            // SetWindowPlacement also shows the window, so pick a non-activating show command.
            var wp = ToNative(placement);
            wp.showCmd = placement.ShowCommand switch
            {
                ShowCommand.Minimized => SW_SHOWMINNOACTIVE,
                ShowCommand.Maximized => SW_SHOWMAXIMIZED,
                _ => SW_SHOWNOACTIVATE,
            };
            shown = SetWindowPlacement(hwnd, ref wp);
        }
        else
        {
            // "Displays the window in its current state", so minimized and maximized are kept.
            shown = ShowWindowAsync(hwnd, SW_SHOWNA);
        }

        if (shown && !insertAfter.IsNull && NativeMethods.IsWindow(insertAfter.Value))
        {
            SetWindowPos(hwnd, insertAfter.Value, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
        }

        return shown;
    }

    public WindowHandle GetForegroundWindow() => new(NativeMethods.GetForegroundWindow());

    public bool Focus(WindowHandle window)
    {
        var hwnd = window.Value;
        if (NativeMethods.GetForegroundWindow() == hwnd || SetForegroundWindow(hwnd))
        {
            return true;
        }

        // Foreground lock: borrow the foreground thread's input state, then retry.
        var foregroundThread = GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        var ownThread = GetCurrentThreadId();
        if (foregroundThread == 0 || foregroundThread == ownThread)
        {
            return false;
        }

        AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            return SetForegroundWindow(hwnd);
        }
        finally
        {
            AttachThreadInput(ownThread, foregroundThread, false);
        }
    }

    /// <summary>
    /// Queries only what the classifier can use, cheapest first. Measured on a busy desktop
    /// (~400 windows), querying everything for every window cost ~120 ms; see docs/decisions.
    /// </summary>
    private WindowInfo? Describe(nint hwnd, int zOrder, nint above)
    {
        var style = GetWindowLong(hwnd, GWL_STYLE);
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        var owner = GetWindow(hwnd, GW_OWNER);
        GetWindowThreadProcessId(hwnd, out var pid);
        var info = new WindowInfo
        {
            Handle = new WindowHandle(hwnd),
            Owner = new WindowHandle(owner),
            ProcessId = pid,
            IsChild = (style & WS_CHILD) != 0,
            IsToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0,
            IsOwnerEnabled = owner == 0 || IsWindowEnabled(owner),
            Placement = WindowPlacement.Unknown,
            ZOrder = zOrder,
            InsertAfter = new WindowHandle(above),
        };

        // Already hidden: kept as-is and never restored, so nothing else about it matters.
        if (!IsWindowVisible(hwnd))
        {
            return info;
        }

        if (GetPlacement(info.Handle) is not { } placement)
        {
            return null; // closed while enumerating
        }

        var cloaked = IsCloaked(hwnd);
        var title = Title(hwnd);
        var candidate = !info.IsChild && !info.IsToolWindow && !cloaked && !string.IsNullOrWhiteSpace(title);

        return info with
        {
            IsVisible = true,
            Placement = placement,
            Title = title,
            IsCloaked = cloaked,
            Monitor = new MonitorHandle(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)),

            // Only needed to match the exclusion list, which only applies to candidates.
            ProcessName = candidate || _detailed ? ProcessName(pid) : "",

            // The shell cloaks windows on other virtual desktops, so a visible uncloaked window is
            // on screen; asking IVirtualDesktopManager about it is also wrong for a few ms after it
            // is shown. Cloaked windows are kept either way, so the COM round trip to Explorer
            // (~2 ms each) only runs in detailed mode, where it gives the precise reason.
            IsOnCurrentVirtualDesktop = !cloaked || !_detailed || _desktops.IsOnCurrentDesktop(hwnd),
        };
    }

    // GetWindowText reads the stored caption for other processes' windows instead of sending
    // WM_GETTEXT, so it cannot hang on an unresponsive app.
    private static string Title(nint hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return "";
        }

        var buffer = new char[length + 1];
        var copied = GetWindowTextW(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, copied);
    }

    private static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    internal static string ProcessName(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return "";
        }

        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageName(process, 0, buffer, ref size)
                ? Path.GetFileName(new string(buffer, 0, size))
                : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private static WINDOWPLACEMENT ToNative(WindowPlacement p) => new()
    {
        length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>(),
        flags = p.Flags,
        showCmd = (int)p.ShowCommand,
        ptMinPosition = new POINT { X = p.MinPosition.X, Y = p.MinPosition.Y },
        ptMaxPosition = new POINT { X = p.MaxPosition.X, Y = p.MaxPosition.Y },
        rcNormalPosition = new RECT { Left = p.NormalPosition.Left, Top = p.NormalPosition.Top, Right = p.NormalPosition.Right, Bottom = p.NormalPosition.Bottom },
    };
}
