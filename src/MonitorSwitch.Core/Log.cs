namespace MonitorSwitch.Core;

/// <summary>
/// Minimal logger. Callers log handles and process names only, never window titles or keys (spec §5).
/// </summary>
public interface ILog
{
    /// <summary>Step-by-step detail (every switch, every window decision). Written only in verbose mode.</summary>
    void Debug(string message);

    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}

/// <summary>Appends to a text file; rolls over to <c>.1</c> when it grows past <see cref="MaxBytes"/>.</summary>
public sealed class FileLog : ILog
{
    public const long MaxBytes = 1024 * 1024;

    private readonly object _gate = new();

    public FileLog(string path, bool verbose = false)
    {
        FilePath = path;
        Verbose = verbose;
    }

    public string FilePath { get; }

    /// <summary>Writes <see cref="Debug"/> lines too. Can change at run time (tray menu).</summary>
    public bool Verbose { get; set; }

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "monitorswitch.log");

    public void Debug(string message)
    {
        if (Verbose)
        {
            Write("DEBUG", message);
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    // Full exception text, stack trace included: a crash must be diagnosable from the log alone.
    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging must never take the tool down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>In-memory log that keeps every level, used by tests.</summary>
public sealed class MemoryLog : ILog
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return _lines.ToList();
            }
        }
    }

    public void Debug(string message) => Add("DEBUG", message);

    public void Info(string message) => Add("INFO", message);

    public void Warn(string message) => Add("WARN", message);

    public void Error(string message, Exception? exception = null) => Add("ERROR", message);

    private void Add(string level, string message)
    {
        lock (_lines)
        {
            _lines.Add($"{level} {message}");
        }
    }
}
