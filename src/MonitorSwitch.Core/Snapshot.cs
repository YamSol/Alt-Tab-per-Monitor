using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonitorSwitch.Core;

/// <summary>
/// What the tool needs to put one hidden window back (spec §2 "Snapshot").
/// No titles and no window content are stored.
/// </summary>
public sealed record SnapshotEntry(
    WindowHandle Handle,
    uint ProcessId,
    string ProcessName,
    WindowPlacement Placement,
    bool WasVisible,
    int ZOrder,
    WindowHandle InsertAfter)
{
    public static SnapshotEntry From(WindowInfo window) => new(
        window.Handle,
        window.ProcessId,
        window.ProcessName,
        window.Placement,
        window.IsVisible,
        window.ZOrder,
        window.InsertAfter);
}

public sealed record Snapshot(int Version, DateTimeOffset CreatedUtc, IReadOnlyList<SnapshotEntry> Entries)
{
    public const int CurrentVersion = 1;

    public static Snapshot From(IEnumerable<WindowInfo> windows, DateTimeOffset now) =>
        new(CurrentVersion, now, windows.Select(SnapshotEntry.From).ToList());
}

/// <summary>
/// Persists the snapshot at <c>%LOCALAPPDATA%\MonitorSwitch\state.json</c> so windows can be
/// recovered after a crash (RF3, RF7). Writes are atomic: temp file, then rename.
/// </summary>
public sealed class SnapshotStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILog _log;

    public SnapshotStore(string path, ILog log)
    {
        FilePath = path;
        _log = log;
    }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "state.json");

    public void Save(Snapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, Serialize(snapshot));
        File.Move(temp, FilePath, overwrite: true);
    }

    internal static string Serialize(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);

    /// <summary>
    /// Reads the snapshot, or returns null when there is none. A corrupt file is moved aside
    /// to <c>state.json.corrupt</c> so it is not retried on every start, and null is returned.
    /// </summary>
    public Snapshot? TryLoad()
    {
        if (!Exists)
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(FilePath), JsonOptions);
            if (snapshot?.Entries is null)
            {
                throw new JsonException("Snapshot has no entries.");
            }

            return snapshot;
        }
        catch (JsonException ex)
        {
            var aside = FilePath + ".corrupt";
            _log.Error($"Snapshot at {FilePath} is unreadable; moved to {aside}.", ex);
            File.Move(FilePath, aside, overwrite: true);
            return null;
        }
    }

    public void Delete()
    {
        if (Exists)
        {
            File.Delete(FilePath);
        }
    }
}
