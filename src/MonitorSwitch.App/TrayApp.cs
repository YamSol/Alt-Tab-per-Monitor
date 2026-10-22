using System.Diagnostics;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using MonitorSwitch.Core;
using MonitorSwitch.Win32;

namespace MonitorSwitch.App;

/// <summary>Tray icon, settings and lifetime (RF7, RF8). Everything here runs on the UI thread.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly FileLog _log;
    private readonly bool _verboseFromCommandLine;
    private readonly SettingsStore _settingsStore;
    private readonly VirtualDesktops _desktops = new();
    private readonly Win32WindowSystem _windows;
    private readonly SwitchController _controller;
    private readonly MessageWindow _messages;
    private readonly AltTabCoordinator _coordinator;
    private readonly NotifyIcon _tray;
    private readonly SynchronizationContext _ui;
    private volatile AppSettings _settings;
    private ToolStripMenuItem _enabledItem = null!;
    private ToolStripMenuItem _viewOnlyItem = null!;
    private ToolStripMenuItem _activeItem = null!;
    private ToolStripMenuItem _restoreItem = null!;
    private ToolStripMenuItem _verboseItem = null!;

    public TrayApp(FileLog log, bool verboseFromCommandLine)
    {
        var started = Stopwatch.GetTimestamp();
        _log = log;
        _verboseFromCommandLine = verboseFromCommandLine;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settingsStore = new SettingsStore(SettingsStore.DefaultPath, log);
        _settings = _settingsStore.Load();
        _log.Verbose = verboseFromCommandLine || _settings.VerboseLog;

        _log.Info(
            $"Starting MonitorSwitch {Application.ProductVersion} (pid {Environment.ProcessId}, .NET {Environment.Version}, " +
            $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}) from {Environment.ProcessPath}.");
        _log.Info(
            $"Data folder {AppPaths.DataDirectory}. Enabled={_settings.Enabled}, Mode={_settings.Mode}, " +
            $"Exclusions={_settings.ExcludedProcesses.Count}, Verbose={_log.Verbose}, " +
            $"virtual desktops {(_desktops.IsAvailable ? "available" : "unavailable")}.");

        _windows = new Win32WindowSystem(_desktops);
        _controller = new SwitchController(
            _windows,
            new SnapshotStore(SnapshotStore.DefaultPath, log),
            () => _settings,
            log,
            (uint)Environment.ProcessId);
        LogMonitors();

        _tray = new NotifyIcon { Icon = CreateIcon(), Visible = true, ContextMenuStrip = BuildMenu() };
        _tray.DoubleClick += (_, _) => ShowPlan();
        _log.Debug("Tray icon created.");

        // RF7: a previous run may have died with windows hidden.
        if (_controller.RecoverOnStartup() is { } recovered)
        {
            Notify($"Restored windows left hidden by a previous run ({recovered.Count(RestoreOutcome.Restored)}).");
        }

        _messages = new MessageWindow(() => RestoreAll("emergency hotkey"), () => RestoreAll("session ending"));
        RegisterHotkey();
        SystemEvents.SessionEnding += OnSessionEnding;

        _coordinator = new AltTabCoordinator(_controller, _windows, () => _settings, log, OnHookFailure);
        _coordinator.Start();
        _log.Info("Keyboard hook installed.");
        RefreshMenu();
        _controller.WarmUp();
        _log.Info($"Ready in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms.");
    }

    /// <summary>Restore everything the tool hid. Safe from any thread, and safe to call twice.</summary>
    public void RestoreAll(string why)
    {
        try
        {
            if (_controller.RestoreAll() is { } report)
            {
                _log.Info($"Restore all ({why}): {report}.");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Restore all ({why}) failed.", ex);
        }
    }

    public void ExitThreadSafe() => _ui.Post(_ => ExitThread(), null);

    protected override void ExitThreadCore()
    {
        _log.Info("Exiting.");
        RestoreAll("exit");
        _tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            _coordinator.Dispose();
            _messages.Dispose();
            _tray.Dispose();
            _desktops.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("MonitorSwitch") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        _enabledItem = new ToolStripMenuItem("Enabled", null, (_, _) => Update(_settings with { Enabled = !_settings.Enabled }));
        menu.Items.Add(_enabledItem);

        _viewOnlyItem = new ToolStripMenuItem("View only (log, never hide)", null, (_, _) => Update(_settings with { Mode = OperationMode.ViewOnly }));
        _activeItem = new ToolStripMenuItem("Active (hide other monitors)", null, (_, _) => Update(_settings with { Mode = OperationMode.Active }));
        menu.Items.Add(new ToolStripMenuItem("Mode", null, _viewOnlyItem, _activeItem));

        menu.Items.Add(new ToolStripMenuItem("Show current plan...", null, (_, _) => ShowPlan()));
        menu.Items.Add(new ToolStripMenuItem("Exclusions...", null, (_, _) => EditExclusions()));
        _restoreItem = new ToolStripMenuItem("Restore all windows", null, (_, _) => RestoreAll("menu"));
        menu.Items.Add(_restoreItem);
        menu.Items.Add(new ToolStripMenuItem("Open data folder", null, (_, _) => OpenDataFolder()));
        _verboseItem = new ToolStripMenuItem("Verbose log", null, (_, _) => Update(_settings with { VerboseLog = !_settings.VerboseLog }));
        menu.Items.Add(_verboseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
        return menu;
    }

    private void RefreshMenu()
    {
        _enabledItem.Checked = _settings.Enabled;
        _viewOnlyItem.Checked = _settings.Mode == OperationMode.ViewOnly;
        _activeItem.Checked = _settings.Mode == OperationMode.Active;
        _restoreItem.ShortcutKeyDisplayString = _settings.EmergencyHotkey;
        _verboseItem.Checked = _log.Verbose;
        var state = !_settings.Enabled ? "disabled" : _settings.Mode == OperationMode.ViewOnly ? "view only" : "active";
        _tray.Text = $"MonitorSwitch ({state})";
    }

    private void Update(AppSettings settings)
    {
        var hotkeyChanged = settings.EmergencyHotkey != _settings.EmergencyHotkey;
        _settings = settings;
        _settingsStore.Save(settings);
        _log.Verbose = _verboseFromCommandLine || settings.VerboseLog;
        _log.Info(
            $"Settings changed: Enabled={settings.Enabled}, Mode={settings.Mode}, " +
            $"Exclusions={settings.ExcludedProcesses.Count}, Verbose={_log.Verbose}.");
        if (hotkeyChanged)
        {
            RegisterHotkey();
        }

        RefreshMenu();
    }

    private void RegisterHotkey()
    {
        if (!Hotkey.TryParse(_settings.EmergencyHotkey, out var hotkey) || !_messages.RegisterEmergencyHotkey(hotkey!))
        {
            _log.Warn($"Emergency hotkey '{_settings.EmergencyHotkey}' is invalid or taken.");
            Notify($"Could not register emergency hotkey {_settings.EmergencyHotkey}. Use the tray menu to restore windows.");
            return;
        }

        _log.Info($"Emergency hotkey {_settings.EmergencyHotkey} registered.");
    }

    private void LogMonitors()
    {
        var monitors = _windows.GetMonitors();
        _log.Info($"{monitors.Count} monitor(s).");
        foreach (var monitor in monitors)
        {
            _log.Debug($"  {monitor.Handle} {monitor.DeviceName} {monitor.Bounds}{(monitor.IsPrimary ? " primary" : "")}");
        }
    }

    private void ShowPlan()
    {
        var detailed = new Win32WindowSystem(_desktops, detailed: true);
        var plan = HidePlanner.Plan(
            detailed.EnumerateWindows(),
            detailed.GetCursorMonitor(),
            _settings.ToClassifierOptions((uint)Environment.ProcessId));
        Dialogs.ShowText("MonitorSwitch - current plan", PlanFormatter.Format(detailed.GetMonitors(), plan, includeIgnored: true));
    }

    private void EditExclusions()
    {
        var edited = Dialogs.EditList(
            "MonitorSwitch - exclusions",
            "Process names never hidden, one per line (e.g. vlc.exe).",
            _settings.ExcludedProcesses);
        if (edited is not null)
        {
            Update(_settings with { ExcludedProcesses = edited });
        }
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDirectory}\"") { UseShellExecute = true });
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => RestoreAll($"session ending ({e.Reason})");

    private void OnHookFailure(Exception ex)
    {
        _log.Error("Keyboard hook failed and was removed.", ex);
        RestoreAll("hook failure");
        Notify("The keyboard hook failed and was removed. Alt+Tab works normally; restart MonitorSwitch to re-enable.");
    }

    private void Notify(string text) => _tray.ShowBalloonTip(5000, "MonitorSwitch", text, ToolTipIcon.Info);

    /// <summary>Two side-by-side "monitors", the left one highlighted.</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var active = new SolidBrush(Color.FromArgb(0x2B, 0x7B, 0xE4));
            using var inactive = new SolidBrush(Color.FromArgb(0x9A, 0xA4, 0xB1));
            g.FillRectangle(active, 1, 6, 14, 14);
            g.FillRectangle(inactive, 17, 6, 14, 14);
            g.FillRectangle(active, 6, 22, 4, 4);
            g.FillRectangle(inactive, 22, 22, 4, 4);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
