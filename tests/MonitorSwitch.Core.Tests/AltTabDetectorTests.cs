using static MonitorSwitch.Core.AltTabDetector;

namespace MonitorSwitch.Core.Tests;

/// <summary>RF2: Alt+Tab and Alt+Shift+Tab detection from the raw key stream.</summary>
public class AltTabDetectorTests
{
    private readonly AltTabDetector _detector = new(deferFirstTab: true);

    private KeyDecision Down(int vk, bool altFlag = false) => _detector.Process(new KeyEvent(vk, true, altFlag));

    private KeyDecision Up(int vk) => _detector.Process(new KeyEvent(vk, false));

    [Theory]
    [InlineData(VkLMenu)]
    [InlineData(VkRMenu)]
    [InlineData(VkMenu)]
    public void Alt_then_tab_begins_and_swallows_the_first_tab(int alt)
    {
        Down(alt);

        var decision = Down(VkTab);

        Assert.Equal(KeyAction.Begin, decision.Action);
        Assert.True(decision.Swallow);
        Assert.False(decision.Reverse);
        Assert.True(_detector.IsSwitching);
    }

    [Fact]
    public void Alt_shift_tab_begins_in_reverse()
    {
        Down(VkLMenu);
        Down(VkLShift);

        var decision = Down(VkTab);

        Assert.Equal(KeyAction.Begin, decision.Action);
        Assert.True(decision.Reverse);
    }

    [Fact]
    public void Tab_up_after_a_swallowed_tab_down_is_swallowed_once()
    {
        Down(VkLMenu);
        Down(VkTab);

        Assert.True(Up(VkTab).Swallow);
        Assert.False(Up(VkTab).Swallow);
    }

    [Fact]
    public void Further_tabs_while_switching_pass_through()
    {
        Down(VkLMenu);
        Down(VkTab);
        Up(VkTab);

        var decision = Down(VkTab);

        Assert.Equal(KeyAction.None, decision.Action);
        Assert.False(decision.Swallow);
    }

    [Fact]
    public void Releasing_alt_after_a_switch_commits()
    {
        Down(VkLMenu);
        Down(VkTab);
        Up(VkTab);

        Assert.Equal(KeyAction.Commit, Up(VkLMenu).Action);
        Assert.False(_detector.IsSwitching);
    }

    [Fact]
    public void Escape_cancels_and_is_not_swallowed()
    {
        Down(VkLMenu);
        Down(VkTab);

        var decision = Down(VkEscape);

        Assert.Equal(KeyAction.Cancel, decision.Action);
        Assert.False(decision.Swallow);
        Assert.Equal(KeyAction.None, Up(VkLMenu).Action);
    }

    [Fact]
    public void Tab_after_cancel_with_alt_still_held_begins_again()
    {
        Down(VkLMenu);
        Down(VkTab);
        Down(VkEscape);

        Assert.Equal(KeyAction.Begin, Down(VkTab).Action);
    }

    [Fact]
    public void Alt_alone_does_nothing()
    {
        Assert.Equal(KeyAction.None, Down(VkLMenu).Action);
        Assert.Equal(KeyAction.None, Up(VkLMenu).Action);
    }

    [Fact]
    public void Tab_without_alt_does_nothing()
    {
        var decision = Down(VkTab);

        Assert.Equal(KeyAction.None, decision.Action);
        Assert.False(decision.Swallow);
    }

    [Fact]
    public void Escape_outside_a_switch_does_nothing()
    {
        Assert.Equal(KeyAction.None, Down(VkEscape).Action);
    }

    [Fact]
    public void Ctrl_alt_tab_is_ignored()
    {
        Down(VkLControl);
        Down(VkLMenu);

        Assert.Equal(KeyAction.None, Down(VkTab).Action);
    }

    [Fact]
    public void Alt_flag_on_tab_recovers_a_missed_alt_down()
    {
        Assert.Equal(KeyAction.Begin, Down(VkTab, altFlag: true).Action);
    }

    [Fact]
    public void Without_deferral_the_tab_is_not_swallowed()
    {
        _detector.DeferFirstTab = false;
        Down(VkLMenu);

        var decision = Down(VkTab);

        Assert.Equal(KeyAction.Begin, decision.Action);
        Assert.False(decision.Swallow);
        Assert.False(Up(VkTab).Swallow);
    }

    [Fact]
    public void Repeated_alt_down_does_not_restart()
    {
        Down(VkLMenu);
        Down(VkTab);
        Down(VkLMenu);

        Assert.True(_detector.IsSwitching);
    }

    [Fact]
    public void Other_keys_are_ignored()
    {
        Down(VkLMenu);

        Assert.Equal(default, Down(0x41));
        Assert.Equal(default, Up(0x41));
    }

    [Fact]
    public void Reset_forgets_alt_and_modifiers()
    {
        Down(VkLControl);
        Down(VkLMenu);
        _detector.Reset();

        Assert.Equal(KeyAction.None, Down(VkTab).Action);
        Down(VkLMenu);
        Assert.Equal(KeyAction.Begin, Down(VkTab).Action);
    }
}
