namespace MonitorSwitch.Core;

public static class AppPaths
{
    /// <summary><c>%LOCALAPPDATA%\MonitorSwitch</c>.</summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorSwitch");
}
