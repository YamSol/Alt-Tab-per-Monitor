using MonitorSwitch.Core;
using MonitorSwitch.Win32;

namespace MonitorSwitch.App;

/// <summary>One-shot command-line modes; never change any window except <c>--restore</c>.</summary>
internal static class Cli
{
    public static int List(ILog log, bool includeIgnored)
    {
        using var desktops = new VirtualDesktops();
        var windows = new Win32WindowSystem(desktops, detailed: true);
        var settings = new SettingsStore(SettingsStore.DefaultPath, log).Load();
        var plan = HidePlanner.Plan(
            windows.EnumerateWindows(),
            windows.GetCursorMonitor(),
            settings.ToClassifierOptions((uint)Environment.ProcessId));
        Print(PlanFormatter.Format(windows.GetMonitors(), plan, includeIgnored));
        return 0;
    }

    public static int Restore(ILog log)
    {
        using var desktops = new VirtualDesktops();
        var windows = new Win32WindowSystem(desktops);
        var controller = new SwitchController(
            windows,
            new SnapshotStore(SnapshotStore.DefaultPath, log),
            () => new AppSettings(),
            log,
            (uint)Environment.ProcessId);
        var report = controller.RecoverOnStartup();
        Print(report is null ? $"Nothing to restore in {AppPaths.DataDirectory}." : $"Restored: {report}");
        return 0;
    }

    public static void Print(string text)
    {
        ConsoleAttach.ToParent();
        Console.WriteLine();
        Console.WriteLine(text);
    }
}
