using System.Text.Json;

namespace MonitorSwitch.Core;

public enum OperationMode
{
    /// <summary>Computes the plan and logs what would be hidden; never touches a window (RF8, criterion 9).</summary>
    ViewOnly,

    /// <summary>Hides and restores windows.</summary>
    Active,
}

/// <summary>User settings, stored at <c>%LOCALAPPDATA%\MonitorSwitch\settings.json</c> (RF8).</summary>
public sealed record AppSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>View-only is the default until the user has checked the plan on their machine.</summary>
    public OperationMode Mode { get; init; } = OperationMode.ViewOnly;

    /// <summary>Starts empty (spec §7 question 3); add apps that misbehave under <c>SW_HIDE</c>.</summary>
    public IReadOnlyList<string> ExcludedProcesses { get; init; } = [];

    /// <summary>Restores every window in the snapshot (RF7).</summary>
    public string EmergencyHotkey { get; init; } = "Ctrl+Alt+Shift+F12";

    /// <summary>See <see cref="ClassifierOptions.OwnedFollowsOwner"/>.</summary>
    public bool OwnedFollowsOwner { get; init; } = true;

    /// <summary>
    /// Swallow the first Tab, hide, then re-inject it, so the system switcher never shows windows
    /// that are about to be hidden (spec §5, "corrida do seletor").
    /// </summary>
    public bool DeferFirstTab { get; init; } = true;

    /// <summary>How long to wait for hidden windows to process <c>SW_HIDE</c>.</summary>
    public int HideTimeoutMs { get; init; } = 100;

    /// <summary>Upper bound to wait, after Alt is released, for the system to activate the chosen window.</summary>
    public int RestoreDelayMs { get; init; } = 250;

    /// <summary>Log every switch and every window decision (handles and process names, never titles).</summary>
    public bool VerboseLog { get; init; }

    public ClassifierOptions ToClassifierOptions(uint selfProcessId) => new()
    {
        ExcludedProcesses = ExcludedProcesses,
        OwnedFollowsOwner = OwnedFollowsOwner,
        SelfProcessId = selfProcessId,
    };
}

public sealed class SettingsStore
{
    private readonly ILog _log;

    public SettingsStore(string path, ILog log)
    {
        FilePath = path;
        _log = log;
    }

    public string FilePath { get; }

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    /// <summary>Loads settings; falls back to defaults when the file is missing or unreadable.</summary>
    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), SnapshotStore.JsonOptions) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            _log.Error($"Settings at {FilePath} are unreadable; using defaults.", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SnapshotStore.JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
