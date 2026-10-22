namespace MonitorSwitch.Core;

/// <summary>Why a window was hidden or kept. One value per row of the table in spec §3.</summary>
public enum Reason
{
    // Hidden
    OtherMonitor,
    OwnerHidden,

    // Kept
    CurrentMonitor,
    Child,
    AlreadyHidden,
    Cloaked,
    OtherVirtualDesktop,
    ToolWindow,
    Untitled,
    OwnedUntitled,
    OwnedModal,
    Excluded,
    Self,
}

public sealed record Classification(WindowInfo Window, bool Hide, Reason Reason);

public sealed record ClassifierOptions
{
    public IReadOnlyCollection<string> ExcludedProcesses { get; init; } = [];

    /// <summary>Spec §7 question 1: an owned window follows its owner when the owner is hidden.</summary>
    public bool OwnedFollowsOwner { get; init; } = true;

    /// <summary>The tool's own process id; its windows (tray menu, dialogs) are never hidden.</summary>
    public uint SelfProcessId { get; init; }
}

/// <summary>Applies the hiding rules of spec §3 to a single window, ignoring its owner's fate.</summary>
public static class WindowClassifier
{
    public static Classification Classify(WindowInfo window, MonitorHandle currentMonitor, ClassifierOptions options)
    {
        var keep = KeepReason(window, options);
        if (keep is { } reason)
        {
            return new Classification(window, Hide: false, reason);
        }

        return window.Monitor == currentMonitor
            ? new Classification(window, Hide: false, Reason.CurrentMonitor)
            : new Classification(window, Hide: true, Reason.OtherMonitor);
    }

    /// <summary>The first rule that protects the window from hiding, or null when it is a candidate.</summary>
    internal static Reason? KeepReason(WindowInfo window, ClassifierOptions options)
    {
        if (window.IsChild)
        {
            return Reason.Child;
        }

        // Already hidden before the trigger: never enters the snapshot (criterion 5).
        if (!window.IsVisible)
        {
            return Reason.AlreadyHidden;
        }

        if (window.ProcessId != 0 && window.ProcessId == options.SelfProcessId)
        {
            return Reason.Self;
        }

        if (!window.IsOnCurrentVirtualDesktop)
        {
            return Reason.OtherVirtualDesktop;
        }

        if (window.IsCloaked)
        {
            return Reason.Cloaked;
        }

        if (window.IsToolWindow)
        {
            return Reason.ToolWindow;
        }

        if (window.IsOwned && !window.IsOwnerEnabled)
        {
            return Reason.OwnedModal;
        }

        if (string.IsNullOrWhiteSpace(window.Title))
        {
            return window.IsOwned ? Reason.OwnedUntitled : Reason.Untitled;
        }

        if (ProcessNames.Contains(options.ExcludedProcesses, window.ProcessName))
        {
            return Reason.Excluded;
        }

        return null;
    }
}
