using MonitorSwitch.Core;
using MonitorSwitch.Win32;

namespace MonitorSwitch.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var cli = CommandLine.Parse(args);
        AppPaths.UseDataDirectory(cli.DataDirectory);
        var log = new FileLog(FileLog.DefaultPath, cli.Verbose);

        if (cli.Error is not null)
        {
            Cli.Print($"{cli.Error}{Environment.NewLine}{Environment.NewLine}{CommandLine.Usage}");
            return 2;
        }

        ApplicationConfiguration.Initialize();
        switch (cli.Command)
        {
            case Command.Tray:
                return RunTray(log, cli.Verbose);
            case Command.List or Command.ListAll:
                return Cli.List(log, includeIgnored: cli.Command == Command.ListAll);
            case Command.Restore:
                return Cli.Restore(log);
            default:
                Cli.Print(CommandLine.Usage);
                return 0;
        }
    }

    private static int RunTray(FileLog log, bool verboseFromCommandLine)
    {
        // WinForms only accepts this before the first control exists on the thread, i.e. before
        // TrayApp creates its tray icon. Getting the order wrong crashed v0.1.0 at startup.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        TrayApp? app = null;
        Application.ThreadException += (_, e) => OnCrash(log, app, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnCrash(log, app, e.ExceptionObject as Exception);

        using var mutex = new Mutex(initiallyOwned: true, AppPaths.InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            log.Info("Another instance is already running; exiting.");
            MessageBox.Show("MonitorSwitch is already running.", "MonitorSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        try
        {
            app = new TrayApp(log, verboseFromCommandLine);
        }
        catch (Exception ex)
        {
            log.Error("Startup failed.", ex);
            MessageBox.Show(
                $"MonitorSwitch could not start:{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Details: {log.FilePath}",
                "MonitorSwitch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        using (app)
        {
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
        }

        log.Info("Exited.");
        return 0;
    }

    private static void OnCrash(ILog log, TrayApp? app, Exception? exception)
    {
        log.Error("Unhandled exception; restoring windows.", exception);
        app?.RestoreAll("crash");
    }
}
