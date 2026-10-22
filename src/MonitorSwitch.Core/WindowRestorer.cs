namespace MonitorSwitch.Core;

public enum RestoreOutcome
{
    Restored,

    /// <summary>The app changed the window while hidden; its current placement was kept (RF5).</summary>
    RestoredWithAppChanges,

    /// <summary>The window was closed (or its handle reused by another process) while hidden (RF5).</summary>
    Closed,

    /// <summary>Something else showed the window already; the tool leaves it alone.</summary>
    AlreadyVisible,

    Failed,
}

public sealed record RestoreResult(SnapshotEntry Entry, RestoreOutcome Outcome);

public sealed record RestoreReport(IReadOnlyList<RestoreResult> Results)
{
    public int Count(RestoreOutcome outcome) => Results.Count(r => r.Outcome == outcome);

    public override string ToString() =>
        string.Join(", ", Enum.GetValues<RestoreOutcome>().Select(o => $"{o}={Count(o)}"));
}

/// <summary>Puts snapshot windows back, bottom of the Z order first (RF4, RF5, RF6).</summary>
public sealed class WindowRestorer
{
    private readonly IWindowSystem _windows;
    private readonly ILog _log;

    public WindowRestorer(IWindowSystem windows, ILog log)
    {
        _windows = windows;
        _log = log;
    }

    public RestoreReport Restore(Snapshot snapshot)
    {
        var results = new List<RestoreResult>(snapshot.Entries.Count);
        foreach (var entry in snapshot.Entries.OrderByDescending(e => e.ZOrder))
        {
            var outcome = RestoreOne(entry);
            results.Add(new RestoreResult(entry, outcome));
            if (outcome is RestoreOutcome.Failed)
            {
                _log.Warn($"Could not restore {entry.Handle} ({entry.ProcessName}).");
            }
        }

        return new RestoreReport(results);
    }

    private RestoreOutcome RestoreOne(SnapshotEntry entry)
    {
        try
        {
            if (!_windows.IsWindow(entry.Handle) || _windows.GetProcessId(entry.Handle) != entry.ProcessId)
            {
                return RestoreOutcome.Closed;
            }

            if (_windows.IsVisible(entry.Handle))
            {
                return RestoreOutcome.AlreadyVisible;
            }

            // Hiding does not change the placement, so a difference means the app changed it.
            // Keep the app's change instead of overwriting it with the snapshot.
            var current = _windows.GetPlacement(entry.Handle);
            var changedByApp = current is not null && current != entry.Placement;
            var target = changedByApp ? current! : entry.Placement;

            if (!_windows.Show(entry.Handle, target, entry.InsertAfter))
            {
                return RestoreOutcome.Failed;
            }

            return changedByApp ? RestoreOutcome.RestoredWithAppChanges : RestoreOutcome.Restored;
        }
        catch (Exception ex)
        {
            _log.Error($"Restoring {entry.Handle} threw.", ex);
            return RestoreOutcome.Failed;
        }
    }
}
