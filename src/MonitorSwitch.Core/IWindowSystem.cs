namespace MonitorSwitch.Core;

/// <summary>
/// The seam between the pure logic in Core and the Win32 implementation.
/// Tests use an in-memory fake; production uses <c>Win32WindowSystem</c>.
/// </summary>
public interface IWindowSystem
{
    /// <summary>All top-level windows (owned and unowned) in Z order, topmost first.</summary>
    IReadOnlyList<WindowInfo> EnumerateWindows();

    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary><c>GetCursorPos</c> + <c>MonitorFromPoint</c> (RF1).</summary>
    MonitorHandle GetCursorMonitor();

    bool IsWindow(WindowHandle window);

    uint GetProcessId(WindowHandle window);

    bool IsVisible(WindowHandle window);

    WindowPlacement? GetPlacement(WindowHandle window);

    /// <summary>Requests <c>SW_HIDE</c>. May complete asynchronously; see <see cref="WaitUntilHidden"/>.</summary>
    bool Hide(WindowHandle window);

    /// <summary>Waits until every window is hidden or the timeout passes. Returns false on timeout.</summary>
    bool WaitUntilHidden(IReadOnlyCollection<WindowHandle> windows, TimeSpan timeout);

    /// <summary>
    /// Shows a window the tool hid, without activating it, applying <paramref name="placement"/>
    /// when it differs from the current one and placing it below <paramref name="insertAfter"/>.
    /// </summary>
    bool Show(WindowHandle window, WindowPlacement placement, WindowHandle insertAfter);

    WindowHandle GetForegroundWindow();

    /// <summary><c>SetForegroundWindow</c>, falling back to <c>AttachThreadInput</c> (RF4).</summary>
    bool Focus(WindowHandle window);
}
