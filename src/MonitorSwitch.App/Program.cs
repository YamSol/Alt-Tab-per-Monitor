using MonitorSwitch.Core;
using MonitorSwitch.Win32;

namespace MonitorSwitch.App;

internal static class Program
{
    private const string Usage = """
        MonitorSwitch - Alt+Tab limited to the monitor under the cursor.

        Usage: MonitorSwitch.exe [option]

          (no option)   Run in the system tray.
          --list        Print the windows of each monitor and what would be hidden, then exit.
          --list-all    Same as --list, including windows ignored by the rules.
          --restore     Restore windows left hidden by a previous run, then exit.
          --help        Show this help.

        Data: %LOCALAPPDATA%\MonitorSwitch (settings.json, state.json, monitorswitch.log)
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var log = new FileLog(FileLog.DefaultPath);
        var option = args.FirstOrDefault()?.ToLowerInvariant();

        switch (option)
        {
            case null:
                return RunTray(log);
            case "--list" or "--list-all":
                return Cli.List(log, includeIgnored: option == "--list-all");
            case "--restore":
                return Cli.Restore(log);
            default:
                Cli.Print(Usage);
                return option is "--help" or "-h" or "/?" ? 0 : 2;
        }
    }

    private static int RunTray(ILog log)
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MonitorSwitch", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("MonitorSwitch is already running.", "MonitorSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        using var app = new TrayApp(log);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => app.Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => app.Crash(e.ExceptionObject as Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => app.RestoreAll("process exit");

        // Started from a terminal: Ctrl+C there restores and exits (RF7).
        if (ConsoleAttach.ToParent())
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                app.RestoreAll("Ctrl+C");
                app.ExitThreadSafe();
            };
        }

        Application.Run(app);
        return 0;
    }
}
