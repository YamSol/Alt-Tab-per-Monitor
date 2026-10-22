using MonitorSwitch.Core;

namespace MonitorSwitch.Core.Tests;

/// <summary>
/// In-memory desktop. Windows are listed topmost first. Every mutating call is appended to
/// <see cref="Calls"/> so tests can assert ordering (e.g. snapshot saved before the first hide).
/// </summary>
internal sealed class FakeWindowSystem : IWindowSystem
{
    public static readonly MonitorHandle Left = new(1);
    public static readonly MonitorHandle Right = new(2);

    private readonly List<FakeWindow> _windows = [];

    public MonitorHandle CursorMonitor { get; set; } = Left;

    public WindowHandle Foreground { get; set; }

    public List<string> Calls { get; } = [];

    /// <summary>Called right before each Hide, e.g. to observe the snapshot file.</summary>
    public Action<WindowHandle>? BeforeHide { get; set; }

    public HashSet<WindowHandle> RefuseHide { get; } = [];

    public HashSet<WindowHandle> RefuseFocus { get; } = [];

    public bool ThrowOnHide { get; set; }

    public HashSet<WindowHandle> RefuseShow { get; } = [];

    public HashSet<WindowHandle> ThrowOnShow { get; } = [];

    public FakeWindow Add(
        int id,
        MonitorHandle? monitor = null,
        string title = "Window",
        ShowCommand state = ShowCommand.Normal,
        bool visible = true,
        int owner = 0,
        bool ownerEnabled = true,
        bool toolWindow = false,
        bool child = false,
        bool cloaked = false,
        bool otherDesktop = false,
        string process = "app.exe",
        uint pid = 100)
    {
        var window = new FakeWindow
        {
            Handle = new WindowHandle(id),
            Monitor = monitor ?? Left,
            Title = title,
            Visible = visible,
            Owner = new WindowHandle(owner),
            OwnerEnabled = ownerEnabled,
            ToolWindow = toolWindow,
            Child = child,
            Cloaked = cloaked,
            OtherDesktop = otherDesktop,
            Process = process,
            ProcessId = pid,
            Placement = Placement(state, id),
        };
        _windows.Add(window);
        return window;
    }

    public FakeWindow this[int id] => _windows.Single(w => w.Handle.Value == id);

    public void Close(int id) => _windows.Remove(this[id]);

    public static WindowPlacement Placement(ShowCommand state, int seed = 0) =>
        new(state, new Point(-1, -1), new Point(-1, -1), new Rect(seed * 10, seed * 10, seed * 10 + 800, seed * 10 + 600));

    /// <summary>Visibility, placement and Z order of every window: what RF6 says must survive a switch.</summary>
    public IReadOnlyList<(WindowHandle, bool, WindowPlacement)> State() =>
        _windows.Select(w => (w.Handle, w.Visible, w.Placement)).ToList();

    public IReadOnlyList<WindowInfo> EnumerateWindows() =>
        _windows.Select((w, i) => new WindowInfo
        {
            Handle = w.Handle,
            Owner = w.Owner,
            ProcessId = w.ProcessId,
            ProcessName = w.Process,
            Title = w.Title,
            IsVisible = w.Visible,
            IsChild = w.Child,
            IsToolWindow = w.ToolWindow,
            IsOwnerEnabled = w.OwnerEnabled,
            IsCloaked = w.Cloaked,
            IsOnCurrentVirtualDesktop = !w.OtherDesktop,
            Monitor = w.Monitor,
            Placement = w.Placement,
            ZOrder = i,
            InsertAfter = i == 0 ? WindowHandle.Null : _windows[i - 1].Handle,
        }).ToList();

    public IReadOnlyList<MonitorInfo> GetMonitors() =>
    [
        new MonitorInfo(Left, @"\\.\DISPLAY1", new Rect(0, 0, 1920, 1080), IsPrimary: true),
        new MonitorInfo(Right, @"\\.\DISPLAY2", new Rect(1920, 0, 3840, 1080), IsPrimary: false),
    ];

    public MonitorHandle GetCursorMonitor() => CursorMonitor;

    public bool IsWindow(WindowHandle window) => Find(window) is not null;

    public uint GetProcessId(WindowHandle window) => Find(window)?.ProcessId ?? 0;

    public bool IsVisible(WindowHandle window) => Find(window)?.Visible ?? false;

    public WindowPlacement? GetPlacement(WindowHandle window) => Find(window)?.Placement;

    public bool Hide(WindowHandle window)
    {
        BeforeHide?.Invoke(window);
        if (ThrowOnHide)
        {
            throw new InvalidOperationException("boom");
        }

        Calls.Add($"hide {window}");
        if (RefuseHide.Contains(window) || Find(window) is not { } w)
        {
            return false;
        }

        w.Visible = false;
        return true;
    }

    public bool WaitUntilHidden(IReadOnlyCollection<WindowHandle> windows, TimeSpan timeout) =>
        windows.All(h => !IsVisible(h));

    public bool Show(WindowHandle window, WindowPlacement placement, WindowHandle insertAfter)
    {
        Calls.Add($"show {window}");
        if (ThrowOnShow.Contains(window))
        {
            throw new InvalidOperationException("boom");
        }

        if (RefuseShow.Contains(window) || Find(window) is not { } w)
        {
            return false;
        }

        w.Visible = true;
        w.Placement = placement;
        return true;
    }

    public WindowHandle GetForegroundWindow() => Foreground;

    public bool Focus(WindowHandle window)
    {
        Calls.Add($"focus {window}");
        if (RefuseFocus.Contains(window))
        {
            return false;
        }

        Foreground = window;
        return true;
    }

    private FakeWindow? Find(WindowHandle handle) => _windows.FirstOrDefault(w => w.Handle == handle);
}

internal sealed class FakeWindow
{
    public required WindowHandle Handle { get; init; }

    public MonitorHandle Monitor { get; set; }

    public string Title { get; set; } = "";

    public bool Visible { get; set; }

    public WindowHandle Owner { get; set; }

    public bool OwnerEnabled { get; set; } = true;

    public bool ToolWindow { get; set; }

    public bool Child { get; set; }

    public bool Cloaked { get; set; }

    public bool OtherDesktop { get; set; }

    public string Process { get; set; } = "";

    public uint ProcessId { get; set; }

    public required WindowPlacement Placement { get; set; }
}
