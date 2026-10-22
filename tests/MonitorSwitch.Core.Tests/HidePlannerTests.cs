namespace MonitorSwitch.Core.Tests;

/// <summary>Spec §3: one test per row of the classification table, plus owner-following.</summary>
public class HidePlannerTests
{
    private readonly FakeWindowSystem _desktop = new() { CursorMonitor = FakeWindowSystem.Left };

    private HidePlan Plan(ClassifierOptions? options = null) =>
        HidePlanner.Plan(_desktop.EnumerateWindows(), _desktop.CursorMonitor, options ?? new ClassifierOptions());

    private Classification For(HidePlan plan, int id) => plan.Items.Single(c => c.Window.Handle.Value == id);

    [Fact]
    public void Visible_titled_top_level_on_other_monitor_is_hidden()
    {
        _desktop.Add(1, FakeWindowSystem.Right);

        var item = For(Plan(), 1);

        Assert.True(item.Hide);
        Assert.Equal(Reason.OtherMonitor, item.Reason);
    }

    [Fact]
    public void Window_on_current_monitor_is_kept()
    {
        _desktop.Add(1, FakeWindowSystem.Left);

        var item = For(Plan(), 1);

        Assert.False(item.Hide);
        Assert.Equal(Reason.CurrentMonitor, item.Reason);
    }

    [Fact]
    public void Minimized_window_on_other_monitor_is_hidden_and_keeps_its_state()
    {
        _desktop.Add(1, FakeWindowSystem.Right, state: ShowCommand.Minimized);

        var plan = Plan();

        Assert.True(For(plan, 1).Hide);
        Assert.Equal(ShowCommand.Minimized, plan.ToHide.Single().Placement.ShowCommand);
    }

    [Fact]
    public void Titled_non_modal_owned_window_is_classified_independently_of_its_owner()
    {
        // Criterion 6: owner here, owned dialog on the other monitor.
        _desktop.Add(1, FakeWindowSystem.Left, title: "Owner");
        _desktop.Add(2, FakeWindowSystem.Right, title: "Palette", owner: 1);

        var plan = Plan();

        Assert.False(For(plan, 1).Hide);
        Assert.True(For(plan, 2).Hide);
        Assert.Equal(Reason.OtherMonitor, For(plan, 2).Reason);
    }

    [Fact]
    public void Modal_owned_window_is_never_hidden()
    {
        // Criterion 7.
        _desktop.Add(1, FakeWindowSystem.Right, title: "Owner");
        _desktop.Add(2, FakeWindowSystem.Right, title: "Save as", owner: 1, ownerEnabled: false);

        var item = For(Plan(), 2);

        Assert.False(item.Hide);
        Assert.Equal(Reason.OwnedModal, item.Reason);
    }

    [Fact]
    public void Modal_owned_window_stays_even_when_owner_follows_is_on()
    {
        _desktop.Add(1, FakeWindowSystem.Right, title: "Owner");
        _desktop.Add(2, FakeWindowSystem.Left, title: "Save as", owner: 1, ownerEnabled: false);

        var plan = Plan(new ClassifierOptions { OwnedFollowsOwner = true });

        Assert.True(For(plan, 1).Hide);
        Assert.Equal(Reason.OwnedModal, For(plan, 2).Reason);
    }

    [Fact]
    public void Untitled_owned_window_is_kept()
    {
        _desktop.Add(1, FakeWindowSystem.Right, title: "Owner");
        _desktop.Add(2, FakeWindowSystem.Right, title: "", owner: 1);

        Assert.Equal(Reason.OwnedUntitled, For(Plan(), 2).Reason);
    }

    [Fact]
    public void Untitled_top_level_window_is_kept()
    {
        _desktop.Add(1, FakeWindowSystem.Right, title: "  ");

        Assert.Equal(Reason.Untitled, For(Plan(), 1).Reason);
    }

    [Fact]
    public void Tool_window_is_kept()
    {
        _desktop.Add(1, FakeWindowSystem.Right, toolWindow: true);

        Assert.Equal(Reason.ToolWindow, For(Plan(), 1).Reason);
    }

    [Fact]
    public void Child_window_is_never_hidden()
    {
        _desktop.Add(1, FakeWindowSystem.Right, child: true);

        Assert.Equal(Reason.Child, For(Plan(), 1).Reason);
    }

    [Fact]
    public void Window_hidden_before_the_trigger_is_not_touched()
    {
        // Criterion 5.
        _desktop.Add(1, FakeWindowSystem.Right, visible: false);

        var plan = Plan();

        Assert.Equal(Reason.AlreadyHidden, For(plan, 1).Reason);
        Assert.Empty(plan.ToHide);
    }

    [Fact]
    public void Cloaked_window_is_kept()
    {
        _desktop.Add(1, FakeWindowSystem.Right, cloaked: true);

        Assert.Equal(Reason.Cloaked, For(Plan(), 1).Reason);
    }

    [Fact]
    public void Window_on_another_virtual_desktop_is_kept()
    {
        // RF9.
        _desktop.Add(1, FakeWindowSystem.Right, otherDesktop: true);

        Assert.Equal(Reason.OtherVirtualDesktop, For(Plan(), 1).Reason);
    }

    [Fact]
    public void Excluded_process_is_kept()
    {
        _desktop.Add(1, FakeWindowSystem.Right, process: "VLC.exe");

        var item = For(Plan(new ClassifierOptions { ExcludedProcesses = ["vlc"] }), 1);

        Assert.Equal(Reason.Excluded, item.Reason);
    }

    [Fact]
    public void Own_windows_are_kept()
    {
        _desktop.Add(1, FakeWindowSystem.Right, pid: 42);

        Assert.Equal(Reason.Self, For(Plan(new ClassifierOptions { SelfProcessId = 42 }), 1).Reason);
    }

    [Fact]
    public void Owned_window_follows_hidden_owner_when_enabled()
    {
        // Spec §7 question 1: no orphaned dialogs on the current monitor.
        _desktop.Add(1, FakeWindowSystem.Right, title: "Owner");
        _desktop.Add(2, FakeWindowSystem.Left, title: "Find", owner: 1);
        _desktop.Add(3, FakeWindowSystem.Left, title: "Nested", owner: 2);

        var plan = Plan(new ClassifierOptions { OwnedFollowsOwner = true });

        Assert.Equal(Reason.OwnerHidden, For(plan, 2).Reason);
        Assert.Equal(Reason.OwnerHidden, For(plan, 3).Reason);
        Assert.Equal(3, plan.ToHide.Count);
    }

    [Fact]
    public void Owned_window_stays_when_owner_follows_is_off()
    {
        _desktop.Add(1, FakeWindowSystem.Right, title: "Owner");
        _desktop.Add(2, FakeWindowSystem.Left, title: "Find", owner: 1);

        var plan = Plan(new ClassifierOptions { OwnedFollowsOwner = false });

        Assert.Equal(Reason.CurrentMonitor, For(plan, 2).Reason);
        Assert.Single(plan.ToHide);
    }

    [Fact]
    public void Owned_window_does_not_follow_a_kept_owner()
    {
        _desktop.Add(1, FakeWindowSystem.Right, title: "Owner", process: "vlc.exe");
        _desktop.Add(2, FakeWindowSystem.Left, title: "Find", owner: 1);

        var plan = Plan(new ClassifierOptions { ExcludedProcesses = ["vlc.exe"] });

        Assert.Empty(plan.ToHide);
    }

    [Fact]
    public void Kept_and_to_hide_partition_the_items()
    {
        _desktop.Add(1, FakeWindowSystem.Left);
        _desktop.Add(2, FakeWindowSystem.Right);
        _desktop.Add(3, FakeWindowSystem.Right, visible: false);

        var plan = Plan();

        Assert.Equal([2], plan.ToHide.Select(w => (int)w.Handle.Value));
        Assert.Equal([1, 3], plan.Kept.Select(w => (int)w.Handle.Value));
    }
}
