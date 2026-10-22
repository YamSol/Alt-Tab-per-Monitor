namespace MonitorSwitch.Core.Tests;

public sealed class SwitchControllerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeWindowSystem _desktop = new() { CursorMonitor = FakeWindowSystem.Left };
    private readonly MemoryLog _log = new();
    private readonly SnapshotStore _store;
    private AppSettings _settings = new() { Mode = OperationMode.Active };

    public SwitchControllerTests()
    {
        _store = new SnapshotStore(_temp.File("state.json"), _log);
    }

    public void Dispose() => _temp.Dispose();

    private SwitchController Controller() => new(_desktop, _store, () => _settings, _log, selfProcessId: 1);

    private void TwoMonitorDesktop()
    {
        _desktop.Add(1, FakeWindowSystem.Left, title: "Here");
        _desktop.Add(2, FakeWindowSystem.Right, title: "Normal");
        _desktop.Add(3, FakeWindowSystem.Right, title: "Minimized", state: ShowCommand.Minimized);
        _desktop.Add(4, FakeWindowSystem.Right, title: "Maximized", state: ShowCommand.Maximized);
        _desktop.Add(5, FakeWindowSystem.Right, title: "Hidden by its app", visible: false);
        _desktop.Foreground = new WindowHandle(1);
    }

    [Fact]
    public void Begin_hides_only_windows_of_other_monitors()
    {
        // Criterion 1.
        TwoMonitorDesktop();

        Controller().Begin();

        Assert.True(_desktop[1].Visible);
        Assert.False(_desktop[2].Visible);
        Assert.False(_desktop[3].Visible);
        Assert.False(_desktop[4].Visible);
    }

    [Fact]
    public void Snapshot_is_on_disk_before_the_first_window_is_hidden()
    {
        // RF3.
        TwoMonitorDesktop();
        var existedAtEachHide = new List<bool>();
        _desktop.BeforeHide = _ => existedAtEachHide.Add(_store.Exists);

        Controller().Begin();

        Assert.NotEmpty(existedAtEachHide);
        Assert.All(existedAtEachHide, Assert.True);
        Assert.Equal(3, _store.TryLoad()!.Entries.Count);
    }

    [Fact]
    public void Cancel_restores_every_hidden_window_to_its_snapshot_state_without_changing_focus()
    {
        // Criterion 2.
        TwoMonitorDesktop();
        var before = _desktop.State();
        var controller = Controller();

        controller.Begin();
        controller.Cancel();

        Assert.Equal(before, _desktop.State());
        Assert.DoesNotContain(_desktop.Calls, c => c.StartsWith("focus"));
        Assert.False(_store.Exists);
        Assert.False(controller.IsSwitching);
    }

    [Fact]
    public void Commit_keeps_every_other_window_as_it_was_and_focuses_the_chosen_one()
    {
        // Criteria 3 and 4, RF6.
        TwoMonitorDesktop();
        _desktop.Add(6, FakeWindowSystem.Left, title: "Chosen");
        var before = _desktop.State();
        var controller = Controller();

        controller.Begin();
        controller.Commit(new WindowHandle(6));

        Assert.Equal(before, _desktop.State());
        Assert.Equal(ShowCommand.Minimized, _desktop[3].Placement.ShowCommand);
        Assert.Equal(new WindowHandle(6), _desktop.Foreground);
        Assert.Equal("focus 0x6", _desktop.Calls[^1]);
    }

    [Fact]
    public void Window_hidden_before_the_trigger_stays_hidden_after_commit()
    {
        // Criterion 5.
        TwoMonitorDesktop();
        var controller = Controller();

        controller.Begin();
        controller.Commit(new WindowHandle(1));

        Assert.False(_desktop[5].Visible);
        Assert.DoesNotContain("show 0x5", _desktop.Calls);
    }

    [Fact]
    public void Restore_goes_from_the_bottom_of_the_z_order_up()
    {
        TwoMonitorDesktop();
        var controller = Controller();

        controller.Begin();
        controller.Cancel();

        var shows = _desktop.Calls.Where(c => c.StartsWith("show")).ToList();
        Assert.Equal(["show 0x4", "show 0x3", "show 0x2"], shows);
    }

    [Fact]
    public void View_only_mode_never_touches_a_window()
    {
        // Criterion 9.
        TwoMonitorDesktop();
        _settings = _settings with { Mode = OperationMode.ViewOnly };
        var before = _desktop.State();
        var controller = Controller();

        var plan = controller.Begin();
        controller.Commit(new WindowHandle(1));
        controller.Cancel();

        Assert.Equal(3, plan!.ToHide.Count);
        Assert.Equal(before, _desktop.State());
        Assert.Empty(_desktop.Calls);
        Assert.False(_store.Exists);
        Assert.Contains(_log.Lines, l => l.Contains("[view-only]") && l.Contains("would hide 3"));
    }

    [Fact]
    public void Disabled_tool_does_nothing()
    {
        TwoMonitorDesktop();
        _settings = _settings with { Enabled = false };

        Assert.Null(Controller().Begin());
        Assert.Empty(_desktop.Calls);
    }

    [Fact]
    public void Nothing_to_hide_writes_no_snapshot()
    {
        _desktop.Add(1, FakeWindowSystem.Left);

        var controller = Controller();
        controller.Begin();

        Assert.False(_store.Exists);
        Assert.False(controller.IsSwitching);
    }

    [Fact]
    public void Second_begin_while_switching_is_ignored()
    {
        TwoMonitorDesktop();
        var controller = Controller();

        controller.Begin();
        var second = controller.Begin();

        Assert.Null(second);
        Assert.Equal(3, _desktop.Calls.Count(c => c.StartsWith("hide")));
    }

    [Fact]
    public void Window_closed_while_hidden_is_dropped()
    {
        // RF5.
        TwoMonitorDesktop();
        var controller = Controller();

        controller.Begin();
        _desktop.Close(2);
        var report = controller.Cancel()!;

        Assert.Equal(1, report.Count(RestoreOutcome.Closed));
        Assert.Equal(2, report.Count(RestoreOutcome.Restored));
    }

    [Fact]
    public void Change_made_by_the_app_while_hidden_is_kept()
    {
        // RF5.
        TwoMonitorDesktop();
        var controller = Controller();

        controller.Begin();
        var maximizedByApp = FakeWindowSystem.Placement(ShowCommand.Maximized, 2);
        _desktop[2].Placement = maximizedByApp;
        var report = controller.Cancel()!;

        Assert.Equal(maximizedByApp, _desktop[2].Placement);
        Assert.Equal(1, report.Count(RestoreOutcome.RestoredWithAppChanges));
    }

    [Fact]
    public void Window_the_app_showed_itself_is_left_alone()
    {
        TwoMonitorDesktop();
        var controller = Controller();

        controller.Begin();
        _desktop[2].Visible = true;
        var report = controller.Cancel()!;

        Assert.Equal(1, report.Count(RestoreOutcome.AlreadyVisible));
        Assert.DoesNotContain("show 0x2", _desktop.Calls);
    }

    [Fact]
    public void Window_that_refuses_to_hide_is_logged_and_survives_restore()
    {
        // UIPI: elevated windows cannot be hidden by a non-elevated process.
        TwoMonitorDesktop();
        _desktop.RefuseHide.Add(new WindowHandle(2));
        var controller = Controller();

        controller.Begin();
        var report = controller.Cancel()!;

        Assert.True(_desktop[2].Visible);
        Assert.Equal(1, report.Count(RestoreOutcome.AlreadyVisible));
        Assert.Contains(_log.Lines, l => l.StartsWith("WARN") && l.Contains("UIPI"));
    }

    [Fact]
    public void One_window_failing_to_restore_does_not_stop_the_others()
    {
        TwoMonitorDesktop();
        _desktop.RefuseShow.Add(new WindowHandle(2));
        _desktop.ThrowOnShow.Add(new WindowHandle(3));
        var controller = Controller();

        controller.Begin();
        var report = controller.Cancel()!;

        Assert.Equal(2, report.Count(RestoreOutcome.Failed));
        Assert.True(_desktop[4].Visible);
        Assert.Contains(_log.Lines, l => l.StartsWith("ERROR Restoring 0x3 threw"));
        Assert.Contains(_log.Lines, l => l.StartsWith("WARN Could not restore 0x2"));
        Assert.False(_store.Exists);
    }

    [Fact]
    public void Failure_while_hiding_restores_immediately_and_rethrows()
    {
        TwoMonitorDesktop();
        _desktop.ThrowOnHide = true;
        var controller = Controller();

        Assert.Throws<InvalidOperationException>(() => controller.Begin());
        Assert.False(controller.IsSwitching);
        Assert.False(_store.Exists);
    }

    [Fact]
    public void Commit_logs_when_focus_fails()
    {
        TwoMonitorDesktop();
        _desktop.RefuseFocus.Add(new WindowHandle(1));
        var controller = Controller();

        controller.Begin();
        controller.Commit(new WindowHandle(1));

        Assert.Contains(_log.Lines, l => l.Contains("Could not focus"));
    }

    [Fact]
    public void Commit_and_cancel_without_a_session_do_nothing()
    {
        var controller = Controller();

        Assert.Null(controller.Commit(new WindowHandle(1)));
        Assert.Null(controller.Cancel());
        Assert.Null(controller.RestoreAll());
    }

    [Fact]
    public void Restart_after_a_crash_restores_the_windows_from_disk()
    {
        // Criterion 8: the first controller "dies" with windows hidden.
        TwoMonitorDesktop();
        var before = _desktop.State();
        Controller().Begin();
        Assert.True(_store.Exists);

        var report = Controller().RecoverOnStartup();

        Assert.NotNull(report);
        Assert.Equal(before, _desktop.State());
        Assert.False(_store.Exists);
    }

    [Fact]
    public void Recovery_skips_handles_reused_by_another_process()
    {
        TwoMonitorDesktop();
        Controller().Begin();
        _desktop[2].ProcessId = 999;

        var report = Controller().RecoverOnStartup()!;

        Assert.Equal(1, report.Count(RestoreOutcome.Closed));
        Assert.False(_desktop[2].Visible);
    }

    [Fact]
    public void Restore_all_uses_the_active_session_first()
    {
        // RF7: emergency hotkey.
        TwoMonitorDesktop();
        var before = _desktop.State();
        var controller = Controller();

        controller.Begin();
        controller.RestoreAll();

        Assert.Equal(before, _desktop.State());
        Assert.False(controller.IsSwitching);
    }

    [Fact]
    public void Restore_all_falls_back_to_the_snapshot_on_disk()
    {
        TwoMonitorDesktop();
        Controller().Begin();

        var report = Controller().RestoreAll();

        Assert.Equal(3, report!.Count(RestoreOutcome.Restored));
    }

    [Fact]
    public void Recovery_with_no_snapshot_returns_null()
    {
        Assert.Null(Controller().RecoverOnStartup());
    }

    [Fact]
    public void Warm_up_touches_no_window_and_no_file()
    {
        TwoMonitorDesktop();

        Controller().WarmUp();

        Assert.Empty(_desktop.Calls);
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void Preview_never_touches_windows()
    {
        TwoMonitorDesktop();

        var plan = Controller().Preview();

        Assert.Equal(3, plan.ToHide.Count);
        Assert.Empty(_desktop.Calls);
    }

    [Fact]
    public void Owned_window_follows_owner_through_a_whole_switch()
    {
        _desktop.Add(1, FakeWindowSystem.Right, title: "Owner");
        _desktop.Add(2, FakeWindowSystem.Left, title: "Palette", owner: 1);
        var before = _desktop.State();
        var controller = Controller();

        controller.Begin();
        Assert.False(_desktop[2].Visible);
        controller.Cancel();

        Assert.Equal(before, _desktop.State());
    }
}
