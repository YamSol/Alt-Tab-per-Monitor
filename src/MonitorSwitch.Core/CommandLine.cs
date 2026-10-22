namespace MonitorSwitch.Core;

public enum Command
{
    Tray,
    List,
    ListAll,
    Restore,
    Help,
}

/// <summary>Parsed command line. <see cref="Error"/> is set when the arguments make no sense.</summary>
public sealed record CommandLine(Command Command, bool Verbose = false, string? DataDirectory = null, string? Error = null)
{
    public const string Usage = """
        MonitorSwitch - Alt+Tab limited to the monitor under the cursor.

        Usage: MonitorSwitch.exe [command] [options]

        Commands:
          (none)            Run in the system tray.
          --list            Print the windows of each monitor and what would be hidden, then exit.
          --list-all        Same as --list, including windows ignored by the rules.
          --restore         Restore windows left hidden by a previous run, then exit.
          --help            Show this help.

        Options:
          --verbose         Log every switch and every window decision (same as "VerboseLog": true).
          --data-dir <dir>  Keep settings, log and snapshot in <dir> instead of %LOCALAPPDATA%\MonitorSwitch.
        """;

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var command = Command.Tray;
        var verbose = false;
        string? dataDirectory = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i].ToLowerInvariant();
            Command? next = arg switch
            {
                "--list" => Command.List,
                "--list-all" => Command.ListAll,
                "--restore" => Command.Restore,
                "--help" or "-h" or "/?" => Command.Help,
                _ => null,
            };

            if (next is { } c)
            {
                if (command != Command.Tray)
                {
                    return new CommandLine(Command.Help, Error: $"Only one command at a time ({args[i]}).");
                }

                command = c;
            }
            else if (arg == "--verbose")
            {
                verbose = true;
            }
            else if (arg == "--data-dir")
            {
                if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return new CommandLine(Command.Help, Error: "--data-dir needs a folder.");
                }

                dataDirectory = args[++i];
            }
            else
            {
                return new CommandLine(Command.Help, Error: $"Unknown argument: {args[i]}");
            }
        }

        return new CommandLine(command, verbose, dataDirectory);
    }
}
