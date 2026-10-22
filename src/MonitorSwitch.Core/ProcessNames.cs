namespace MonitorSwitch.Core;

/// <summary>Exclusion-list matching: case-insensitive, with or without the <c>.exe</c> suffix.</summary>
public static class ProcessNames
{
    public static string Normalize(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.ToLowerInvariant();
    }

    public static bool Contains(IEnumerable<string> list, string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var target = Normalize(processName);
        return list.Any(entry => Normalize(entry) == target);
    }
}
