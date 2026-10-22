namespace MonitorSwitch.Core;

/// <summary><c>MOD_*</c> flags for <c>RegisterHotKey</c>.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A hotkey such as <c>Ctrl+Alt+Shift+F12</c>, parsed into what <c>RegisterHotKey</c> takes.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["Pause"] = 0x13,
        ["Backspace"] = 0x08,
    };

    public static bool TryParse(string? text, out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        int? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };

            if (modifier != HotkeyModifiers.None)
            {
                modifiers |= modifier;
                continue;
            }

            if (key is not null || ParseKey(part) is not { } vk)
            {
                return false;
            }

            key = vk;
        }

        // A bare key would steal normal typing.
        if (key is null || modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, key.Value);
        return true;
    }

    private static int? ParseKey(string part)
    {
        if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
        {
            return char.ToUpperInvariant(part[0]);
        }

        if (part.Length >= 2 && (part[0] is 'F' or 'f') && int.TryParse(part[1..], out var n) && n is >= 1 and <= 24)
        {
            return 0x70 + n - 1;
        }

        return NamedKeys.TryGetValue(part, out var named) ? named : null;
    }
}
