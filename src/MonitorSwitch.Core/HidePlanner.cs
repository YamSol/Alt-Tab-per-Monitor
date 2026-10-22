namespace MonitorSwitch.Core;

public sealed record HidePlan(MonitorHandle CurrentMonitor, IReadOnlyList<Classification> Items)
{
    public IReadOnlyList<WindowInfo> ToHide => Items.Where(c => c.Hide).Select(c => c.Window).ToList();

    public IReadOnlyList<WindowInfo> Kept => Items.Where(c => !c.Hide).Select(c => c.Window).ToList();
}

/// <summary>Decides, for every window at trigger time, whether it is hidden (spec §3, RF1, RF3).</summary>
public static class HidePlanner
{
    public static HidePlan Plan(IReadOnlyList<WindowInfo> windows, MonitorHandle currentMonitor, ClassifierOptions options)
    {
        var items = windows.Select(w => WindowClassifier.Classify(w, currentMonitor, options)).ToList();

        if (options.OwnedFollowsOwner)
        {
            FollowOwners(items);
        }

        return new HidePlan(currentMonitor, items);
    }

    /// <summary>
    /// Owned windows on the current monitor whose owner is hidden are hidden too, so they are not
    /// left orphaned (spec §7 question 1). Repeats until stable to cover chains of owners.
    /// Only windows kept for <see cref="Reason.CurrentMonitor"/> qualify: modal, untitled, excluded
    /// and similar windows keep their protection.
    /// </summary>
    private static void FollowOwners(List<Classification> items)
    {
        bool changed;
        do
        {
            changed = false;
            var hidden = items.Where(c => c.Hide).Select(c => c.Window.Handle).ToHashSet();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!item.Hide && item.Reason == Reason.CurrentMonitor && item.Window.IsOwned && hidden.Contains(item.Window.Owner))
                {
                    items[i] = item with { Hide = true, Reason = Reason.OwnerHidden };
                    changed = true;
                }
            }
        }
        while (changed);
    }
}
