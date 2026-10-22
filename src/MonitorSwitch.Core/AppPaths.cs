using System.Security.Cryptography;
using System.Text;

namespace MonitorSwitch.Core;

public static class AppPaths
{
    private static string? _override;

    /// <summary><c>%LOCALAPPDATA%\MonitorSwitch</c>, unless <c>--data-dir</c> chose another folder.</summary>
    public static string DataDirectory => _override ?? DefaultDataDirectory;

    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorSwitch");

    /// <summary>
    /// One running instance per data folder: the default folder keeps the plain name, so it also
    /// matches older versions; any other folder (tests, portable copies) gets its own.
    /// </summary>
    public static string InstanceMutexName
    {
        get
        {
            if (_override is null)
            {
                return @"Local\MonitorSwitch";
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(_override.ToUpperInvariant()));
            return @"Local\MonitorSwitch-" + Convert.ToHexString(hash)[..16];
        }
    }

    /// <summary>Process-wide; call once at startup, before anything reads a path.</summary>
    public static void UseDataDirectory(string? path) => _override = path is null ? null : Path.GetFullPath(path);
}
