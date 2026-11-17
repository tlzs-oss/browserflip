using BrowserFlip.App.Desktop;

namespace BrowserFlip.Core.Tests.Desktop;

public class AutoHideControllerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int ms) => T0.AddMilliseconds(ms);

    private static AutoHideController Enabled()
    {
        var c = new AutoHideController();
        c.SetEnabled(true, T0);
        return c;
    }

    private static AutoHideState Step(AutoHideController c, int ms, bool overBar = false, bool overTab = false, bool suppressed = false)
        => c.Update(new AutoHideInput(At(ms), overBar, overTab, suppressed));

    [Fact]
    public void Disabled_is_always_expanded()
    {
        var c = new AutoHideController();
        Assert.Equal(AutoHideState.Expanded, Step(c, 10_000));
        Assert.False(c.IsCollapsed);
    }

    [Fact]
    public void Stays_open_for_initial_idle_then_collapses_450ms_after_leaving()
    {
        var c = Enabled();
        Assert.Equal(AutoHideState.Expanded, Step(c, 1_000));       // within initial idle (1.5 s)
        Assert.Equal(AutoHideState.PendingCollapse, Step(c, 1_500));
        Assert.Equal(AutoHideState.PendingCollapse, Step(c, 1_900)); // 400 ms
        Assert.Equal(AutoHideState.Collapsed, Step(c, 1_950));       // 450 ms (Owner range 400-600 ms incl. polling)
        Assert.True(c.IsCollapsed);
    }

    [Fact]
    public void Cursor_over_bar_keeps_it_open()
    {
        var c = Enabled();
        for (var t = 0; t <= 10_000; t += 100)
        {
            Assert.Equal(AutoHideState.Expanded, Step(c, t, overBar: true));
        }
    }

    [Fact]
    public void Returning_during_pending_collapse_cancels_it()
    {
        var c = Enabled();
        Step(c, 1_500);
        Assert.Equal(AutoHideState.PendingCollapse, Step(c, 1_700));
        Assert.Equal(AutoHideState.Expanded, Step(c, 1_800, overBar: true));
        Assert.Equal(AutoHideState.PendingCollapse, Step(c, 1_900));
        Assert.Equal(AutoHideState.PendingCollapse, Step(c, 2_300)); // timer restarted at 1.9 s
        Assert.Equal(AutoHideState.Collapsed, Step(c, 2_350));
    }

    [Theory]
    [InlineData("click / mouse button held")]
    [InlineData("dragging")]
    [InlineData("menu open")]
    [InlineData("keyboard focus")]
    public void Suppression_prevents_collapse(string reason)
    {
        _ = reason;
        var c = Enabled();
        for (var t = 0; t <= 5_000; t += 100)
        {
            Assert.False(c.IsCollapsed);
            Step(c, t, suppressed: true);
        }

        Assert.Equal(AutoHideState.Expanded, c.State);
    }

    [Fact]
    public void Suppression_during_pending_collapse_reexpands()
    {
        var c = Enabled();
        Step(c, 1_500);
        Assert.Equal(AutoHideState.Expanded, Step(c, 1_600, suppressed: true));
    }

    [Fact]
    public void Tab_hover_needs_dwell_to_expand()
    {
        var c = Collapsed();
        Assert.Equal(AutoHideState.PendingExpand, Step(c, 3_000, overTab: true));
        Assert.True(c.IsCollapsed);
        Assert.Equal(AutoHideState.PendingExpand, Step(c, 3_050, overTab: true));
        Assert.Equal(AutoHideState.Expanded, Step(c, 3_100, overTab: true)); // 100 ms dwell (Owner range 100-150 ms)
    }

    [Fact]
    public void Brief_pass_over_tab_does_not_expand()
    {
        var c = Collapsed();
        Step(c, 3_000, overTab: true);
        Assert.Equal(AutoHideState.Collapsed, Step(c, 3_100));
        Assert.Equal(AutoHideState.Collapsed, Step(c, 3_300));
    }

    [Fact]
    public void After_hover_expand_collapses_once_cursor_leaves()
    {
        var c = Collapsed();
        Step(c, 3_000, overTab: true);
        Step(c, 3_200, overTab: true);
        Assert.Equal(AutoHideState.Expanded, Step(c, 3_300, overBar: true));
        Assert.Equal(AutoHideState.PendingCollapse, Step(c, 3_400));
        Assert.Equal(AutoHideState.Collapsed, Step(c, 3_850));
    }

    [Fact]
    public void Suppression_does_not_expand_a_collapsed_bar()
    {
        var c = Collapsed();
        Assert.Equal(AutoHideState.Collapsed, Step(c, 3_000, suppressed: true));
    }

    [Fact]
    public void ForceExpand_recovers_from_collapsed_and_holds()
    {
        var c = Collapsed();
        c.ForceExpand(At(5_000));
        Assert.Equal(AutoHideState.Expanded, c.State);
        Assert.Equal(AutoHideState.Expanded, Step(c, 6_000));         // held for 1.5 s
        Assert.Equal(AutoHideState.PendingCollapse, Step(c, 6_500));
    }

    [Fact]
    public void Disabling_expands_immediately()
    {
        var c = Collapsed();
        c.SetEnabled(false, At(4_000));
        Assert.Equal(AutoHideState.Expanded, c.State);
        Assert.Equal(AutoHideState.Expanded, Step(c, 9_000));
    }

    [Fact]
    public void Custom_timings_are_honored()
    {
        var c = new AutoHideController { CollapseDelay = TimeSpan.FromMilliseconds(100), InitialIdle = TimeSpan.Zero };
        c.SetEnabled(true, T0);
        Step(c, 0);
        Assert.Equal(AutoHideState.Collapsed, Step(c, 100));
    }

    [Fact]
    public void Time_until_transition_reports_the_pending_deadline()
    {
        var c = Enabled();
        Assert.Equal(TimeSpan.FromMilliseconds(500), c.TimeUntilTransition(At(1_000))); // hold until 1.5 s
        Step(c, 1_500);
        Assert.Equal(TimeSpan.FromMilliseconds(350), c.TimeUntilTransition(At(1_600))); // collapse at 1.95 s
        Step(c, 1_950);
        Assert.Null(c.TimeUntilTransition(At(2_000))); // collapsed: nothing pending
        Step(c, 2_000, overTab: true);
        Assert.Equal(TimeSpan.FromMilliseconds(70), c.TimeUntilTransition(At(2_030))); // dwell ends at 2.1 s
    }

    private static AutoHideController Collapsed()
    {
        var c = Enabled();
        Step(c, 1_500);
        Step(c, 2_100);
        Assert.Equal(AutoHideState.Collapsed, c.State);
        return c;
    }
}
