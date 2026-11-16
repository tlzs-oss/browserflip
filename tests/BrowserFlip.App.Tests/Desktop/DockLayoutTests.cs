using BrowserFlip.App.Desktop;

namespace BrowserFlip.Core.Tests.Desktop;

public class DockLayoutTests
{
    // 1920x1080 monitor with a 48 px taskbar at the bottom.
    private static readonly PixelRect Work = new(0, 0, 1920, 1032);

    [Theory]
    [InlineData(DockEdge.Left, BarOrientation.Vertical)]
    [InlineData(DockEdge.Right, BarOrientation.Vertical)]
    [InlineData(DockEdge.Top, BarOrientation.Horizontal)]
    [InlineData(DockEdge.Bottom, BarOrientation.Horizontal)]
    public void Orientation_follows_edge(DockEdge edge, BarOrientation expected)
    {
        Assert.Equal(expected, DockLayout.OrientationFor(edge, BarOrientation.Horizontal));
        Assert.Equal(expected, DockLayout.OrientationFor(edge, BarOrientation.Vertical));
    }

    [Theory]
    [InlineData(BarOrientation.Horizontal)]
    [InlineData(BarOrientation.Vertical)]
    public void Floating_uses_preference(BarOrientation floating)
        => Assert.Equal(floating, DockLayout.OrientationFor(null, floating));

    [Fact]
    public void Right_dock_is_flush_with_margin_and_centered()
    {
        var r = DockLayout.Docked(Work, DockEdge.Right, 48, 300, 0.5, 4);
        Assert.Equal(1920 - 4, r.Right);
        Assert.Equal(48, r.Width);
        Assert.Equal(300, r.Height);
        Assert.Equal((1032 / 2) - 150, r.Top);
    }

    [Fact]
    public void Left_dock_respects_position_and_clamps_inside_work_area()
    {
        var top = DockLayout.Docked(Work, DockEdge.Left, 48, 300, 0.0, 4);
        Assert.Equal(4, top.Left);
        Assert.Equal(4, top.Top);

        var bottom = DockLayout.Docked(Work, DockEdge.Left, 48, 300, 1.0, 4);
        Assert.Equal(1032 - 4, bottom.Bottom);
    }

    [Fact]
    public void Bottom_dock_stays_above_taskbar()
    {
        var r = DockLayout.Docked(Work, DockEdge.Bottom, 400, 44, 0.5, 4);
        Assert.Equal(1032 - 4, r.Bottom);
        Assert.True(r.Bottom <= Work.Bottom);
        Assert.Equal(400, r.Width);
    }

    [Fact]
    public void Top_dock_with_offset_work_area_on_second_monitor()
    {
        // Second monitor to the left with negative coordinates and a top taskbar.
        var work = new PixelRect(-2560, 40, 0, 1440);
        var r = DockLayout.Docked(work, DockEdge.Top, 600, 52, 0.5, 6);
        Assert.Equal(46, r.Top);
        Assert.Equal(-1280 - 300, r.Left);
    }

    [Fact]
    public void Bar_longer_than_edge_is_shrunk_to_fit()
    {
        var r = DockLayout.Docked(Work, DockEdge.Right, 48, 5000, 0.5, 4);
        Assert.Equal(1032 - 8, r.Height);
        Assert.Equal(4, r.Top);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-3)]
    [InlineData(7)]
    public void Invalid_position_is_sanitized(double position)
    {
        var r = DockLayout.Docked(Work, DockEdge.Right, 48, 300, position, 4);
        Assert.True(r.Top >= Work.Top && r.Bottom <= Work.Bottom);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    public void Tab_is_flush_with_edge_thin_and_inside_work_area(DockEdge edge)
    {
        var bar = DockLayout.Docked(Work, edge, edge is DockEdge.Left or DockEdge.Right ? 48 : 400, edge is DockEdge.Left or DockEdge.Right ? 400 : 44, 0.5, 4);
        var tab = DockLayout.Tab(Work, edge, bar, 8, 56);

        Assert.Equal(tab, tab.Intersect(Work)); // fully inside the work area: never spills to a neighbor monitor
        switch (edge)
        {
            case DockEdge.Left:
                Assert.Equal((Work.Left, 8, 56), (tab.Left, tab.Width, tab.Height));
                break;
            case DockEdge.Right:
                Assert.Equal((Work.Right, 8, 56), (tab.Right, tab.Width, tab.Height));
                break;
            case DockEdge.Top:
                Assert.Equal((Work.Top, 56, 8), (tab.Top, tab.Width, tab.Height));
                break;
            case DockEdge.Bottom:
                Assert.Equal((Work.Bottom, 56, 8), (tab.Bottom, tab.Width, tab.Height));
                break;
        }

        // Centered on the expanded bar along the edge.
        if (edge is DockEdge.Left or DockEdge.Right)
        {
            Assert.InRange(tab.Top + (tab.Height / 2) - (bar.Top + (bar.Height / 2)), -1, 1);
        }
        else
        {
            Assert.InRange(tab.Left + (tab.Width / 2) - (bar.Left + (bar.Width / 2)), -1, 1);
        }
    }

    [Theory]
    [InlineData(10, 300, DockEdge.Left)]
    [InlineData(1920 - 48 - 5, 300, DockEdge.Right)]
    [InlineData(800, 12, DockEdge.Top)]
    [InlineData(800, 1032 - 300 - 20, DockEdge.Bottom)]
    public void Snaps_to_nearest_edge_within_threshold(int left, int top, DockEdge expected)
        => Assert.Equal(expected, DockLayout.SnapEdge(PixelRect.FromSize(left, top, 48, 300), Work, 24));

    [Fact]
    public void No_snap_in_the_middle()
        => Assert.Null(DockLayout.SnapEdge(PixelRect.FromSize(800, 400, 48, 300), Work, 24));

    [Fact]
    public void PositionAlong_round_trips_with_Docked()
    {
        var r = DockLayout.Docked(Work, DockEdge.Right, 48, 300, 0.25, 4);
        var p = DockLayout.PositionAlong(r, Work, DockEdge.Right);
        Assert.InRange(p, 0.24, 0.26);
    }

    [Fact]
    public void ClampInto_moves_and_shrinks()
    {
        var r = DockLayout.ClampInto(PixelRect.FromSize(1900, 1000, 300, 200), Work);
        Assert.Equal(Work.Right, r.Right);
        Assert.Equal(Work.Bottom, r.Bottom);

        var big = DockLayout.ClampInto(PixelRect.FromSize(-50, -50, 5000, 5000), Work);
        Assert.Equal(Work, big);
    }

    private static readonly MonitorInfo Primary = new(@"\\.\DISPLAY1", new(0, 0, 1920, 1080), Work, 1.0, true);
    private static readonly MonitorInfo Secondary = new(@"\\.\DISPLAY2", new(1920, 0, 4480, 1440), new(1920, 0, 4480, 1400), 1.5, false);

    [Fact]
    public void EnsureVisible_keeps_visible_window()
    {
        var w = PixelRect.FromSize(100, 100, 600, 700);
        Assert.Equal(w, DockLayout.EnsureVisible(w, [Primary, Secondary], 48));
    }

    [Fact]
    public void EnsureVisible_recovers_window_from_disconnected_monitor()
    {
        // Saved on a third monitor that no longer exists.
        var w = PixelRect.FromSize(6000, 200, 600, 700);
        var r = DockLayout.EnsureVisible(w, [Primary, Secondary], 48);
        Assert.Equal(r, r.Intersect(Primary.WorkArea));
        Assert.Equal((600, 700), (r.Width, r.Height));
    }

    [Fact]
    public void EnsureVisible_moves_mostly_offscreen_window_onto_its_monitor()
    {
        var w = PixelRect.FromSize(4450, 300, 600, 700); // only 30 px visible on the secondary
        var r = DockLayout.EnsureVisible(w, [Primary, Secondary], 48);
        Assert.Equal(r, r.Intersect(Secondary.WorkArea));
    }

    [Fact]
    public void EnsureVisible_fixes_title_bar_above_work_area()
    {
        var w = PixelRect.FromSize(100, -200, 600, 700);
        var r = DockLayout.EnsureVisible(w, [Primary], 48);
        Assert.Equal(Work.Top, r.Top);
        Assert.Equal(700, r.Height);
    }

    [Fact]
    public void EnsureVisible_handles_empty_input()
    {
        Assert.Equal(default, DockLayout.EnsureVisible(default, [Primary], 48));
        var w = PixelRect.FromSize(1, 1, 10, 10);
        Assert.Equal(w, DockLayout.EnsureVisible(w, [], 48));
    }
}
