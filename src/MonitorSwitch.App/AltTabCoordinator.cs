using MonitorSwitch.Core;
using MonitorSwitch.Win32;

namespace MonitorSwitch.App;

/// <summary>
/// Glue between the keyboard hook thread and the UI thread (RF2-RF5).
/// <list type="bullet">
/// <item>Hook thread: feed keys to <see cref="AltTabDetector"/>, swallow the first Tab, post actions.</item>
/// <item>UI thread: hide, re-inject Tab, and on Alt release wait for the system to activate the
/// chosen window (foreground event or timeout) before restoring.</item>
/// </list>
/// </summary>
internal sealed class AltTabCoordinator : IDisposable
{
    private readonly SwitchController _controller;
    private readonly IWindowSystem _windows;
    private readonly Func<AppSettings> _settings;
    private readonly ILog _log;
    private readonly SynchronizationContext _ui;
    private readonly AltTabDetector _detector = new(deferFirstTab: true);
    private readonly KeyboardHook _hook;
    private readonly ForegroundWatcher _foreground;
    private readonly System.Windows.Forms.Timer _commitTimer = new();
    private bool _commitPending;
    private WindowHandle _chosen;

    public AltTabCoordinator(SwitchController controller, IWindowSystem windows, Func<AppSettings> settings, ILog log, Action<Exception> onHookFailure)
    {
        _controller = controller;
        _windows = windows;
        _settings = settings;
        _log = log;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create on the UI thread.");
        _commitTimer.Tick += (_, _) => FinishCommit();
        _foreground = new ForegroundWatcher(OnForeground);
        _hook = new KeyboardHook(OnKey, ex => _ui.Post(_ => onHookFailure(ex), null));
    }

    public void Start() => _hook.Start();

    public void Dispose()
    {
        _hook.Dispose();
        _foreground.Dispose();
        _commitTimer.Dispose();
    }

    // Hook thread. Must be fast and must never block.
    private bool OnKey(KeyEvent key)
    {
        var settings = _settings();
        if (!settings.Enabled)
        {
            _detector.Reset();
            return false;
        }

        // In view-only mode nothing is hidden, so there is nothing to wait for.
        _detector.DeferFirstTab = settings.DeferFirstTab && settings.Mode == OperationMode.Active;
        var decision = _detector.Process(key);
        switch (decision.Action)
        {
            case KeyAction.Begin:
                _ui.Post(_ => OnBegin(decision.Swallow, decision.Reverse), null);
                break;
            case KeyAction.Commit:
                _ui.Post(_ => OnCommit(), null);
                break;
            case KeyAction.Cancel:
                _ui.Post(_ => OnCancel(), null);
                break;
        }

        return decision.Swallow;
    }

    private void OnBegin(bool tabWasSwallowed, bool reverse)
    {
        _log.Debug($"Alt+{(reverse ? "Shift+" : "")}Tab: begin (first Tab {(tabWasSwallowed ? "held back" : "passed through")}).");
        if (_commitPending)
        {
            _log.Debug("Previous switch still pending; finishing it first.");
            FinishCommit();
        }

        try
        {
            _controller.Begin();
        }
        catch (Exception ex)
        {
            _log.Error("Begin failed.", ex);
        }
        finally
        {
            if (tabWasSwallowed)
            {
                var altHeld = KeyInjector.IsAltHeld();
                KeyInjector.ReplayTab(reverse);
                _log.Debug(altHeld ? "Re-sent Tab (Alt still held)." : "Alt already released: replayed the whole Alt+Tab.");
            }
        }
    }

    private void OnCommit()
    {
        if (!_controller.IsSwitching)
        {
            _log.Debug("Alt released: nothing hidden, nothing to restore.");
            return;
        }

        _commitPending = true;
        _chosen = WindowHandle.Null;
        _commitTimer.Interval = Math.Max(1, _settings().RestoreDelayMs);
        _commitTimer.Start();
        _log.Debug($"Alt released: waiting up to {_commitTimer.Interval} ms for the switch to land.");
    }

    private void OnCancel()
    {
        _log.Debug("Esc: cancel.");
        _controller.Cancel();
    }

    private void OnForeground(WindowHandle window)
    {
        if (_commitPending)
        {
            _log.Debug($"Foreground changed to {window}.");
            _chosen = window;
            FinishCommit();
        }
    }

    private void FinishCommit()
    {
        _commitTimer.Stop();
        if (!_commitPending)
        {
            return;
        }

        _commitPending = false;
        if (_chosen.IsNull)
        {
            _log.Debug("No foreground change before the timeout; using the current foreground window.");
        }

        var chosen = _chosen.IsNull ? _windows.GetForegroundWindow() : _chosen;
        _controller.Commit(chosen);
    }
}
