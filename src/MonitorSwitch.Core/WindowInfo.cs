namespace MonitorSwitch.Core;

/// <summary>
/// Everything the classifier needs to know about one top-level <c>HWND</c> (owned or not),
/// captured at trigger time. Titles stay in memory only; they are never persisted (spec §5, privacy).
/// </summary>
public sealed record WindowInfo
{
    public required WindowHandle Handle { get; init; }

    /// <summary><c>GetWindow(GW_OWNER)</c>. <see cref="WindowHandle.Null"/> for unowned top-level windows.</summary>
    public WindowHandle Owner { get; init; }

    public uint ProcessId { get; init; }

    /// <summary>Executable file name, e.g. <c>notepad.exe</c>. Empty when the process cannot be opened (UIPI).</summary>
    public string ProcessName { get; init; } = "";

    public string Title { get; init; } = "";

    public bool IsVisible { get; init; }

    /// <summary><c>WS_CHILD</c>. Never hidden.</summary>
    public bool IsChild { get; init; }

    /// <summary><c>WS_EX_TOOLWINDOW</c>.</summary>
    public bool IsToolWindow { get; init; }

    /// <summary><c>IsWindowEnabled(owner)</c>. False means this owned window is modal.</summary>
    public bool IsOwnerEnabled { get; init; } = true;

    /// <summary><c>DWMWA_CLOAKED</c> (suspended UWP frames, other virtual desktops).</summary>
    public bool IsCloaked { get; init; }

    /// <summary>
    /// <c>IVirtualDesktopManager::IsWindowOnCurrentVirtualDesktop</c> (RF9). Windows on other
    /// desktops are also cloaked, so the fast path may leave this true and rely on <see cref="IsCloaked"/>.
    /// </summary>
    public bool IsOnCurrentVirtualDesktop { get; init; } = true;

    /// <summary><c>MonitorFromWindow(MONITOR_DEFAULTTONEAREST)</c>.</summary>
    public MonitorHandle Monitor { get; init; }

    /// <summary><see cref="WindowPlacement.Unknown"/> for windows that were not visible.</summary>
    public required WindowPlacement Placement { get; init; }

    /// <summary>Position in <c>EnumWindows</c> order; 0 is the top of the Z order.</summary>
    public int ZOrder { get; init; }

    /// <summary>The window directly above this one in Z order, used to put it back in place.</summary>
    public WindowHandle InsertAfter { get; init; }

    public bool IsOwned => !Owner.IsNull;

    public bool IsMinimized => Placement.ShowCommand == ShowCommand.Minimized;
}
