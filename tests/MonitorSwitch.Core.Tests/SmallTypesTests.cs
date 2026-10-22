namespace MonitorSwitch.Core.Tests;

public class SmallTypesTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+F12", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7B)]
    [InlineData("win+r", HotkeyModifiers.Win, 'R')]
    [InlineData(" Control + 7 ", HotkeyModifiers.Control, '7')]
    [InlineData("Alt+F1", HotkeyModifiers.Alt, 0x70)]
    [InlineData("Ctrl+Pause", HotkeyModifiers.Control, 0x13)]
    [InlineData("Windows+Space", HotkeyModifiers.Win, 0x20)]
    public void Hotkey_parses(string text, HotkeyModifiers modifiers, int vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(modifiers, vk), hotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F12")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Banana")]
    public void Hotkey_rejects(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out var hotkey));
        Assert.Null(hotkey);
    }

    [Theory]
    [InlineData("VLC.EXE", "vlc")]
    [InlineData("  obs64.exe ", "obs64")]
    [InlineData("notepad", "notepad")]
    public void Process_names_normalize(string input, string expected)
    {
        Assert.Equal(expected, ProcessNames.Normalize(input));
    }

    [Fact]
    public void Empty_process_name_never_matches()
    {
        Assert.False(ProcessNames.Contains([""], ""));
        Assert.False(ProcessNames.Contains(["vlc"], "  "));
    }

    [Fact]
    public void Handles_format_as_hex()
    {
        Assert.Equal("0xFF", new WindowHandle(255).ToString());
        Assert.Equal("0x10", new MonitorHandle(16).ToString());
        Assert.True(WindowHandle.Null.IsNull);
        Assert.True(MonitorHandle.Null.IsNull);
        Assert.False(new MonitorHandle(1).IsNull);
    }

    [Fact]
    public void Rect_reports_size()
    {
        var rect = new Rect(10, 20, 110, 70);

        Assert.Equal(100, rect.Width);
        Assert.Equal(50, rect.Height);
        Assert.Equal("(10,20) 100x50", rect.ToString());
    }

    [Fact]
    public void Restore_report_summarises_outcomes()
    {
        var entry = new SnapshotEntry(new WindowHandle(1), 1, "a.exe", FakeWindowSystem.Placement(ShowCommand.Normal), true, 0, WindowHandle.Null);
        var report = new RestoreReport([new RestoreResult(entry, RestoreOutcome.Restored), new RestoreResult(entry, RestoreOutcome.Closed)]);

        Assert.Contains("Restored=1", report.ToString());
        Assert.Contains("Closed=1", report.ToString());
        Assert.Contains("Failed=0", report.ToString());
    }

    [Fact]
    public void Plan_formatter_groups_by_monitor_and_marks_the_cursor()
    {
        var desktop = new FakeWindowSystem { CursorMonitor = FakeWindowSystem.Left };
        desktop.Add(1, FakeWindowSystem.Left, title: "Editor", state: ShowCommand.Maximized, process: "code.exe");
        desktop.Add(2, FakeWindowSystem.Right, title: "Browser", process: "firefox.exe");
        desktop.Add(3, FakeWindowSystem.Right, title: "Find", owner: 2, state: ShowCommand.Minimized);
        desktop.Add(4, FakeWindowSystem.Right, title: "Tray", toolWindow: true);
        desktop.Add(5, new MonitorHandle(99), title: "Ghost");
        desktop.Add(6, FakeWindowSystem.Right, title: "Invisible", visible: false);
        var plan = HidePlanner.Plan(desktop.EnumerateWindows(), desktop.CursorMonitor, new ClassifierOptions());

        var text = PlanFormatter.Format(desktop.GetMonitors(), plan);

        Assert.Contains(@"Monitor 1 \\.\DISPLAY1 (0,0) 1920x1080 primary  <- cursor", text);
        Assert.Contains("[keep] 0x1 code.exe \"Editor\" (top-level maximized; CurrentMonitor)", text);
        Assert.Contains("[HIDE] 0x2 firefox.exe \"Browser\" (top-level; OtherMonitor)", text);
        Assert.Contains("(owned by 0x2 minimized; OtherMonitor)", text);
        Assert.DoesNotContain("Tray", text);
        Assert.Contains("Unknown monitor", text);
        Assert.EndsWith("3 window(s) would be hidden.", text);

        var detailed = PlanFormatter.Format(desktop.GetMonitors(), plan, includeIgnored: true);
        Assert.Contains("ToolWindow", detailed);
        Assert.DoesNotContain("Invisible", detailed);
    }

    [Fact]
    public void Memory_log_keeps_levels()
    {
        var log = new MemoryLog();
        log.Info("a");
        log.Warn("b");
        log.Error("c", new InvalidOperationException());

        Assert.Equal(["INFO a", "WARN b", "ERROR c"], log.Lines);
    }
}
