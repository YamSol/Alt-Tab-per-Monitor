namespace MonitorSwitch.Core.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly MemoryLog _log = new();

    public void Dispose() => _temp.Dispose();

    private static Snapshot SampleSnapshot()
    {
        var desktop = new FakeWindowSystem();
        desktop.Add(10, FakeWindowSystem.Right, title: "Secret document.docx", state: ShowCommand.Maximized, process: "winword.exe");
        desktop.Add(11, FakeWindowSystem.Right, state: ShowCommand.Minimized);
        return Snapshot.From(desktop.EnumerateWindows(), new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Snapshot_round_trips_through_disk()
    {
        var store = new SnapshotStore(_temp.File(Path.Combine("nested", "state.json")), _log);
        var snapshot = SampleSnapshot();

        store.Save(snapshot);
        var loaded = store.TryLoad();

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Entries, loaded.Entries);
        Assert.Equal(snapshot.CreatedUtc, loaded.CreatedUtc);
        Assert.Equal(Snapshot.CurrentVersion, loaded.Version);
    }

    [Fact]
    public void Snapshot_never_contains_window_titles()
    {
        // Spec §5, privacy.
        var store = new SnapshotStore(_temp.File("state.json"), _log);

        store.Save(SampleSnapshot());

        Assert.DoesNotContain("Secret", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        var store = new SnapshotStore(_temp.File("state.json"), _log);

        store.Save(SampleSnapshot());

        Assert.Equal(["state.json"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Missing_snapshot_loads_as_null()
    {
        var store = new SnapshotStore(_temp.File("state.json"), _log);

        Assert.False(store.Exists);
        Assert.Null(store.TryLoad());
        store.Delete();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("""{ "Version": 1 }""")]
    public void Corrupt_snapshot_is_moved_aside(string content)
    {
        var path = _temp.File("state.json");
        File.WriteAllText(path, content);
        var store = new SnapshotStore(path, _log);

        Assert.Null(store.TryLoad());
        Assert.False(store.Exists);
        Assert.True(File.Exists(path + ".corrupt"));
        Assert.Contains(_log.Lines, l => l.StartsWith("ERROR"));
    }

    [Fact]
    public void Delete_removes_the_snapshot()
    {
        var store = new SnapshotStore(_temp.File("state.json"), _log);
        store.Save(SampleSnapshot());

        store.Delete();

        Assert.False(store.Exists);
    }

    [Fact]
    public void Default_paths_live_under_local_app_data()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(root, "MonitorSwitch", "state.json"), SnapshotStore.DefaultPath);
        Assert.Equal(Path.Combine(root, "MonitorSwitch", "settings.json"), SettingsStore.DefaultPath);
        Assert.StartsWith(Path.Combine(root, "MonitorSwitch"), FileLog.DefaultPath);
    }

    [Fact]
    public void Settings_default_to_view_only_with_an_empty_exclusion_list()
    {
        var settings = new SettingsStore(_temp.File("settings.json"), _log).Load();

        Assert.True(settings.Enabled);
        Assert.Equal(OperationMode.ViewOnly, settings.Mode);
        Assert.Empty(settings.ExcludedProcesses);
        Assert.True(settings.OwnedFollowsOwner);
        Assert.True(Hotkey.TryParse(settings.EmergencyHotkey, out _));
    }

    [Fact]
    public void Settings_round_trip()
    {
        var store = new SettingsStore(_temp.File("settings.json"), _log);
        var settings = new AppSettings { Mode = OperationMode.Active, ExcludedProcesses = ["vlc.exe", "obs64"], RestoreDelayMs = 50 };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(OperationMode.Active, loaded.Mode);
        Assert.Equal(["vlc.exe", "obs64"], loaded.ExcludedProcesses);
        Assert.Equal(50, loaded.RestoreDelayMs);
        Assert.Contains("\"Active\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Unreadable_settings_fall_back_to_defaults()
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, "{{{");

        var settings = new SettingsStore(path, _log).Load();

        Assert.Equal(new AppSettings().Mode, settings.Mode);
        Assert.Contains(_log.Lines, l => l.StartsWith("ERROR"));
    }

    [Fact]
    public void Settings_map_to_classifier_options()
    {
        var options = new AppSettings { ExcludedProcesses = ["a"], OwnedFollowsOwner = false }.ToClassifierOptions(7);

        Assert.Equal(["a"], options.ExcludedProcesses);
        Assert.False(options.OwnedFollowsOwner);
        Assert.Equal(7u, options.SelfProcessId);
    }

    [Fact]
    public void File_log_appends_and_rolls_over()
    {
        var log = new FileLog(_temp.File(Path.Combine("logs", "test.log")));
        log.Info("first");
        log.Warn("second");
        log.Error("third", new InvalidOperationException("why"));

        var lines = File.ReadAllLines(log.FilePath);
        Assert.Equal(3, lines.Length);
        Assert.Contains("INFO first", lines[0]);
        Assert.Contains("WARN second", lines[1]);
        Assert.Contains("ERROR third InvalidOperationException: why", lines[2]);

        File.WriteAllText(log.FilePath, new string('x', (int)FileLog.MaxBytes + 1));
        log.Info("after roll");

        Assert.True(File.Exists(log.FilePath + ".1"));
        Assert.Single(File.ReadAllLines(log.FilePath));
    }

    [Fact]
    public void File_log_swallows_io_errors()
    {
        var path = _temp.File("locked.log");
        using var locked = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var log = new FileLog(path);

        log.Info("cannot write");
    }
}
