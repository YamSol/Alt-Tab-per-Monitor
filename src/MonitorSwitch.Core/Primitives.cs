using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonitorSwitch.Core;

/// <summary>An <c>HWND</c>. Kept as a number so Core never touches Win32 directly.</summary>
[JsonConverter(typeof(WindowHandleJsonConverter))]
public readonly record struct WindowHandle(nint Value)
{
    public static readonly WindowHandle Null = new(0);

    public bool IsNull => Value == 0;

    public override string ToString() => $"0x{(long)Value:X}";
}

/// <summary>An <c>HMONITOR</c>.</summary>
public readonly record struct MonitorHandle(nint Value)
{
    public static readonly MonitorHandle Null = new(0);

    public bool IsNull => Value == 0;

    public override string ToString() => $"0x{(long)Value:X}";
}

public readonly record struct Point(int X, int Y);

public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public override string ToString() => $"({Left},{Top}) {Width}x{Height}";
}

/// <summary>Subset of <c>SW_*</c> values that <c>GetWindowPlacement</c> reports in <c>showCmd</c>.</summary>
public enum ShowCommand
{
    Hide = 0,
    Normal = 1,
    Minimized = 2,
    Maximized = 3,
}

/// <summary>Mirror of <c>WINDOWPLACEMENT</c>.</summary>
public sealed record WindowPlacement(
    ShowCommand ShowCommand,
    Point MinPosition,
    Point MaxPosition,
    Rect NormalPosition,
    int Flags = 0)
{
    /// <summary>Placeholder for windows whose placement was not queried (already hidden ones).</summary>
    public static readonly WindowPlacement Unknown = new(ShowCommand.Hide, default, default, default);
}

public sealed record MonitorInfo(MonitorHandle Handle, string DeviceName, Rect Bounds, bool IsPrimary);

internal sealed class WindowHandleJsonConverter : JsonConverter<WindowHandle>
{
    public override WindowHandle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new((nint)reader.GetInt64());

    public override void Write(Utf8JsonWriter writer, WindowHandle value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((long)value.Value);
}
