using System.Text;

namespace MonitorSwitch.Core;

/// <summary>Human-readable report of a plan, grouped by monitor (milestone M1, <c>--list</c>).</summary>
public static class PlanFormatter
{
    /// <param name="includeIgnored">
    /// Also list visible windows the rules keep (tool windows, modal dialogs, ...). Hidden and child
    /// windows are never listed: there are hundreds and none can appear in Alt+Tab.
    /// </param>
    public static string Format(IReadOnlyList<MonitorInfo> monitors, HidePlan plan, bool includeIgnored = false)
    {
        var text = new StringBuilder();
        var byMonitor = plan.Items
            .Where(c => c.Reason is not (Reason.AlreadyHidden or Reason.Child))
            .Where(c => includeIgnored || c.Reason is Reason.OtherMonitor or Reason.CurrentMonitor or Reason.OwnerHidden)
            .GroupBy(c => c.Window.Monitor)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var marker = monitor.Handle == plan.CurrentMonitor ? "  <- cursor" : "";
            var primary = monitor.IsPrimary ? " primary" : "";
            text.AppendLine($"Monitor {i + 1} {monitor.DeviceName} {monitor.Bounds}{primary}{marker}");

            foreach (var item in byMonitor.GetValueOrDefault(monitor.Handle) ?? [])
            {
                text.AppendLine(FormatItem(item));
            }

            text.AppendLine();
        }

        var orphans = byMonitor.Where(kv => monitors.All(m => m.Handle != kv.Key)).SelectMany(kv => kv.Value).ToList();
        if (orphans.Count > 0)
        {
            text.AppendLine("Unknown monitor");
            foreach (var item in orphans)
            {
                text.AppendLine(FormatItem(item));
            }

            text.AppendLine();
        }

        text.Append($"{plan.ToHide.Count} window(s) would be hidden.");
        return text.ToString();
    }

    private static string FormatItem(Classification item)
    {
        var w = item.Window;
        var action = item.Hide ? "HIDE" : "keep";
        var kind = w.IsOwned ? $"owned by {w.Owner}" : "top-level";
        var state = w.IsMinimized ? " minimized" : w.Placement.ShowCommand == ShowCommand.Maximized ? " maximized" : "";
        return $"  [{action}] {w.Handle} {w.ProcessName} \"{w.Title}\" ({kind}{state}; {item.Reason})";
    }
}
