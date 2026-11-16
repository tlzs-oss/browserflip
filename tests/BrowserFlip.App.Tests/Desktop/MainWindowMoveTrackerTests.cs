using BrowserFlip.App.Desktop;

namespace BrowserFlip.Core.Tests.Desktop;

/// <summary>BF-01 gate: only a user move (WM_MOVING, no WM_SIZING) inside a move loop may dock the main window.</summary>
public sealed class MainWindowMoveTrackerTests
{
    private static readonly PixelRect Start = PixelRect.FromSize(900, 400, 1170, 800);

    [Fact]
    public void A_move_reports_the_rectangle_before_the_drag()
    {
        var t = new MainWindowMoveTracker();
        t.Enter(Start);
        Assert.True(t.Moving());
        Assert.True(t.Moving());
        Assert.Equal(Start, t.Exit());
        Assert.False(t.IsActive);
    }

    [Fact]
    public void A_resize_never_counts_even_if_the_window_also_moved()
    {
        var t = new MainWindowMoveTracker();
        t.Enter(Start);
        t.Sizing();
        Assert.True(t.Moving()); // resizing from the left / top edge also moves the window
        Assert.Null(t.Exit());
    }

    [Fact]
    public void A_loop_without_movement_never_counts()
    {
        var t = new MainWindowMoveTracker();
        t.Enter(Start); // e.g. clicked the title bar without moving
        Assert.Null(t.Exit());
    }

    [Fact]
    public void Moving_outside_a_loop_never_counts_and_a_window_shown_near_an_edge_never_docks()
    {
        var t = new MainWindowMoveTracker();
        Assert.False(t.Moving()); // e.g. SetWindowPos at startup: no WM_ENTERSIZEMOVE
        Assert.Null(t.Exit());
    }

    [Fact]
    public void Each_loop_starts_fresh()
    {
        var t = new MainWindowMoveTracker();
        t.Enter(Start);
        t.Sizing();
        Assert.Null(t.Exit());
        t.Enter(Start);
        _ = t.Moving();
        Assert.Equal(Start, t.Exit());
    }

    [Fact]
    public void An_unknown_start_rectangle_never_counts()
    {
        var t = new MainWindowMoveTracker();
        t.Enter(default);
        _ = t.Moving();
        Assert.Null(t.Exit());
    }
}
