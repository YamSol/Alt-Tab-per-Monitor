using System.Diagnostics;

namespace MonitorSwitch.Core;

/// <summary>
/// One Alt+Tab session: plan and hide on <see cref="Begin"/>, restore on <see cref="Commit"/> or
/// <see cref="Cancel"/>. Owns the invariant that the snapshot is on disk before the first window is
/// hidden (RF3) and deleted only after every window is restored (RF7).
/// </summary>
public sealed class SwitchController
{
    private readonly IWindowSystem _windows;
    private readonly SnapshotStore _store;
    private readonly Func<AppSettings> _settings;
    private readonly ILog _log;
    private readonly uint _selfProcessId;
    private readonly WindowRestorer _restorer;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private Snapshot? _active;

    public SwitchController(
        IWindowSystem windows,
        SnapshotStore store,
        Func<AppSettings> settings,
        ILog log,
        uint selfProcessId,
        TimeProvider? time = null)
    {
        _windows = windows;
        _store = store;
        _settings = settings;
        _log = log;
        _selfProcessId = selfProcessId;
        _time = time ?? TimeProvider.System;
        _restorer = new WindowRestorer(windows, log);
    }

    /// <summary>True while windows are hidden and waiting to be restored.</summary>
    public bool IsSwitching
    {
        get
        {
            lock (_gate)
            {
                return _active is not null;
            }
        }
    }

    /// <summary>
    /// Runs the planning and serialization paths once without touching any window or file, so the
    /// first real Alt+Tab does not pay for JIT and serializer start-up (~200 ms measured).
    /// </summary>
    public void WarmUp()
    {
        var plan = Preview();
        _ = SnapshotStore.Serialize(Snapshot.From(plan.ToHide, _time.GetUtcNow()));
    }

    /// <summary>Computes the plan for the monitor under the cursor without changing anything.</summary>
    public HidePlan Preview()
    {
        var settings = _settings();
        var monitor = _windows.GetCursorMonitor();
        return HidePlanner.Plan(_windows.EnumerateWindows(), monitor, settings.ToClassifierOptions(_selfProcessId));
    }

    /// <summary>
    /// Starts a session. Returns the plan, or null when disabled or already switching.
    /// In <see cref="OperationMode.ViewOnly"/> the plan is only logged.
    /// </summary>
    public HidePlan? Begin()
    {
        lock (_gate)
        {
            var settings = _settings();
            if (!settings.Enabled || _active is not null)
            {
                return null;
            }

            var started = Stopwatch.GetTimestamp();
            var plan = Preview();
            var toHide = plan.ToHide;
            var planned = Stopwatch.GetElapsedTime(started);
            LogPlan(plan, settings.Mode);

            if (settings.Mode == OperationMode.ViewOnly)
            {
                _log.Info($"[view-only] monitor {plan.CurrentMonitor}: would hide {toHide.Count} of {plan.Items.Count} windows.");
                foreach (var item in plan.Items.Where(c => c.Hide))
                {
                    _log.Info($"[view-only]   {item.Window.Handle} {item.Window.ProcessName} ({item.Reason})");
                }

                return plan;
            }

            if (toHide.Count == 0)
            {
                return plan;
            }

            var snapshot = Snapshot.From(toHide, _time.GetUtcNow());
            _store.Save(snapshot);
            _active = snapshot;
            var saved = Stopwatch.GetElapsedTime(started);
            TimeSpan posted;

            try
            {
                foreach (var window in toHide)
                {
                    if (!_windows.Hide(window.Handle))
                    {
                        _log.Warn($"Could not hide {window.Handle} ({window.ProcessName}); possibly elevated (UIPI).");
                    }
                }

                posted = Stopwatch.GetElapsedTime(started);
                var handles = toHide.Select(w => w.Handle).ToList();
                if (!_windows.WaitUntilHidden(handles, TimeSpan.FromMilliseconds(settings.HideTimeoutMs)))
                {
                    _log.Warn($"Some windows were still visible after {settings.HideTimeoutMs} ms.");
                }
            }
            catch (Exception ex)
            {
                _log.Error("Hiding failed; restoring immediately.", ex);
                RestoreActive();
                throw;
            }

            // Spec §5 asks to measure the flow: these are cumulative milestones within Begin.
            _log.Info(
                $"Hid {toHide.Count} windows in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms " +
                $"(planned {planned.TotalMilliseconds:F1}, saved {saved.TotalMilliseconds:F1}, posted {posted.TotalMilliseconds:F1}).");
            return plan;
        }
    }

    /// <summary>Alt released: restore everything, then give focus to <paramref name="chosen"/> (RF4).</summary>
    public RestoreReport? Commit(WindowHandle chosen)
    {
        lock (_gate)
        {
            if (_active is null)
            {
                return null;
            }

            var report = RestoreActive();
            if (chosen.IsNull)
            {
                _log.Debug("No chosen window to focus.");
            }
            else if (_windows.Focus(chosen))
            {
                _log.Debug($"Focused {chosen}.");
            }
            else
            {
                _log.Warn($"Could not focus {chosen}.");
            }

            return report;
        }
    }

    /// <summary>Esc: restore everything without changing focus (RF4).</summary>
    public RestoreReport? Cancel()
    {
        lock (_gate)
        {
            return _active is null ? null : RestoreActive();
        }
    }

    /// <summary>
    /// Emergency hotkey, shutdown, logoff (RF7): restore the active session, or whatever snapshot
    /// is on disk when there is none in memory.
    /// </summary>
    public RestoreReport? RestoreAll()
    {
        lock (_gate)
        {
            if (_active is not null)
            {
                return RestoreActive();
            }

            return RecoverFromDisk();
        }
    }

    /// <summary>On startup: if a previous run left a snapshot, restore it and delete the file (RF7).</summary>
    public RestoreReport? RecoverOnStartup()
    {
        lock (_gate)
        {
            return RecoverFromDisk();
        }
    }

    /// <summary>Verbose: one line per visible window with the rule that decided it.</summary>
    private void LogPlan(HidePlan plan, OperationMode mode)
    {
        var visible = plan.Items.Where(c => c.Reason is not (Reason.AlreadyHidden or Reason.Child)).ToList();
        _log.Debug($"Plan ({mode}): cursor on monitor {plan.CurrentMonitor}, {visible.Count} visible windows, {plan.ToHide.Count} to hide.");
        foreach (var item in visible)
        {
            var w = item.Window;
            var owner = w.IsOwned ? $" owner={w.Owner}" : "";
            _log.Debug($"  {(item.Hide ? "hide" : "keep")} {w.Handle} {w.ProcessName} monitor={w.Monitor}{owner} state={w.Placement.ShowCommand} reason={item.Reason}");
        }
    }

    private RestoreReport? RecoverFromDisk()
    {
        var snapshot = _store.TryLoad();
        if (snapshot is null)
        {
            return null;
        }

        var report = _restorer.Restore(snapshot);
        _store.Delete();
        _log.Info($"Recovered snapshot from {snapshot.CreatedUtc:u}: {report}.");
        return report;
    }

    private RestoreReport RestoreActive()
    {
        var report = _restorer.Restore(_active!);
        _store.Delete();
        _active = null;
        _log.Info($"Restored: {report}.");
        return report;
    }
}
