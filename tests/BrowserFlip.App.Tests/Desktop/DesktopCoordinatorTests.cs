using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Settings;
using static BrowserFlip.Core.Platform.RegistryRoot;
using static BrowserFlip.Core.Platform.RegistryViewKind;

namespace BrowserFlip.Core.Tests.Desktop;

/// <summary>
/// Drives the real <see cref="DesktopCoordinator"/> (real WPF windows parked off-screen, never shown on
/// the desktop at a visible place, never activated) with a fake monitor layout, a fake cursor and a fake
/// clock: docking, orientation, auto-hide and the notification-area behavior end to end.
/// Covers v0.2.0 Release Gate issues T7 (hidden bar does not expand), T10 (floating orientation),
/// T11 (top / bottom docking stays vertical), T12 (responsiveness) and the fixed tray behavior.
/// </summary>
public sealed class DesktopCoordinatorTests
{
    // 3840 x 2400 at 225 % (the Owner's primary monitor), taskbar at the bottom.
    internal static readonly MonitorInfo Primary = new(
        @"\\.\DISPLAY1", new PixelRect(0, 0, 3840, 2400), new PixelRect(0, 0, 3840, 2292), 2.25, true);

    // 1920 x 1080 at 100 %, to the right of the primary monitor (shared vertical edge at x = 3840).
    internal static readonly MonitorInfo RightNeighbor = new(
        @"\\.\DISPLAY2", new PixelRect(3840, 0, 5760, 1080), new PixelRect(3840, 0, 5760, 1032), 1.0, false);

    // 2560 x 1440 at 150 %, to the left of the primary monitor (negative coordinates), taskbar on its right.
    internal static readonly MonitorInfo LeftNegative150 = new(
        @"\\.\DISPLAY3", new PixelRect(-2560, 200, 0, 1640), new PixelRect(-2560, 200, -72, 1640), 1.5, false);

    public static TheoryData<DockEdge> AllEdges => new() { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom };

    // ------------------------------------------------------------------ T7: expand from the edge

    [Fact]
    public void Collapsed_bar_expands_when_the_cursor_rests_on_its_strip()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            h.Collapse();
            var strip = h.BarBounds;

            h.Hover(Primary.WorkArea.Right - 1, strip.Top + (strip.Height / 2), 300);

            Assert.False(h.Coordinator.IsCollapsed);
            Assert.True(h.BarBounds.Width > strip.Width, "expanded bar is wider than the strip");
        });
    }

    [Fact]
    public void Collapsed_bar_expands_anywhere_along_its_length_at_the_edge()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            h.Collapse();
            var strip = h.BarBounds;

            // Near both ends of the bar, 10 px inside the edge (the user approaches, not exactly on the strip).
            foreach (var y in new[] { strip.Top + 2, strip.Bottom - 3 })
            {
                h.Hover(Primary.WorkArea.Right - 10, y, 300);
                Assert.False(h.Coordinator.IsCollapsed, $"expands at y={y}");
                h.Collapse();
            }
        });
    }

    [Fact]
    public void Collapsed_bar_does_not_expand_from_far_along_the_edge_or_from_inside_the_screen()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            h.Collapse();
            var strip = h.BarBounds;

            h.Hover(Primary.WorkArea.Right - 1, Math.Max(0, strip.Top - 400), 1000); // same edge, far from the bar
            Assert.True(h.Coordinator.IsCollapsed);
            h.Hover(Primary.WorkArea.Right - 60, strip.Top + (strip.Height / 2), 1000); // 60 px inside: not at the edge
            Assert.True(h.Coordinator.IsCollapsed);
        });
    }

    [Fact]
    public void A_quick_pass_over_the_edge_does_not_expand_but_a_short_rest_does()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            h.Collapse();
            var strip = h.BarBounds;
            var y = strip.Top + (strip.Height / 2);

            h.Hover(Primary.WorkArea.Right - 1, y, 50); // passes by in ~50 ms
            h.Hover(1000, 1000, 50);
            Assert.True(h.Coordinator.IsCollapsed);

            h.Hover(Primary.WorkArea.Right - 1, y, 200); // rests ~200 ms (dwell 120 ms)
            Assert.False(h.Coordinator.IsCollapsed);
        });
    }

    [Fact]
    public void Collapsed_bar_on_an_edge_shared_with_another_monitor_expands_when_the_cursor_slips_over()
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Right);
            settings.DockPosition = 0.3; // the bar sits next to the neighbor monitor (y 0-1080)
            using var h = new Harness(settings, Primary, RightNeighbor);
            h.Collapse();
            var strip = h.BarBounds;
            Assert.True(strip.Bottom <= RightNeighbor.Bounds.Bottom, $"precondition: strip {strip} next to the neighbor");

            // Nothing stops the cursor at a shared edge: it lands just inside the neighbor monitor.
            var y = strip.Top + (strip.Height / 2);
            h.Hover(RightNeighbor.WorkArea.Left + 2, y, 300);

            Assert.False(h.Coordinator.IsCollapsed, $"strip {strip}, zone {h.Coordinator.TriggerZone}, cursor ({RightNeighbor.WorkArea.Left + 2},{y})");
        });
    }

    [Fact]
    public void Trigger_zone_never_reaches_onto_the_taskbar()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Bottom), Primary); // taskbar below the work area
            h.Collapse();
            var zone = h.Coordinator.TriggerZone;
            Assert.Equal(Primary.WorkArea.Bottom, zone.Bottom);

            h.Hover(zone.Left + (zone.Width / 2), Primary.WorkArea.Bottom + 10, 1000); // on the taskbar
            Assert.True(h.Coordinator.IsCollapsed);
        });
    }

    [Theory]
    [MemberData(nameof(AllEdges))]
    public void Every_edge_hides_and_expands_again(DockEdge edge)
    {
        Run(() =>
        {
            using var h = new Harness(Docked(edge), Primary);
            var expanded = h.BarBounds;
            Assert.Equal(edge is DockEdge.Left or DockEdge.Right, expanded.Height > expanded.Width);

            h.Collapse();
            var strip = h.BarBounds;
            Assert.InRange(Math.Min(strip.Width, strip.Height), 3, 5); // visible hint 3-5 px
            Assert.Equal(Math.Max(expanded.Width, expanded.Height), Math.Max(strip.Width, strip.Height)); // as long as the bar

            var (x, y) = PointAtEdge(Primary.WorkArea, edge, strip);
            h.Hover(x, y, 300);
            Assert.False(h.Coordinator.IsCollapsed);
            Assert.Equal(expanded, h.BarBounds);
        });
    }

    [Fact]
    public void Twenty_hide_and_show_cycles_stay_consistent()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            var expanded = h.BarBounds;
            PixelRect? strip = null;
            h.Hover(1000, 1000, 1500); // the bar stays open for 1.5 s after it appears
            for (var i = 0; i < 20; i++)
            {
                h.Hover(1000, 1000, 800); // leave: collapses after 500 ms
                Assert.True(h.Coordinator.IsCollapsed, $"cycle {i}: collapsed, bar {h.BarBounds}");
                strip ??= h.BarBounds;
                Assert.Equal(strip, h.BarBounds);

                h.Hover(Primary.WorkArea.Right - 1, strip.Value.Top + (strip.Value.Height / 2), 250);
                Assert.False(h.Coordinator.IsCollapsed, $"cycle {i}: expanded (strip {strip}, zone {h.Coordinator.TriggerZone})");
                Assert.Equal(expanded, h.BarBounds);

                h.Hover(expanded.Left + 5, expanded.Top + 5, 1500); // using the bar: stays open
                Assert.False(h.Coordinator.IsCollapsed, $"cycle {i}: stays open while used, bar {h.BarBounds}");
            }
        });
    }

    [Fact]
    public void Expand_and_collapse_times_with_the_real_timer_cadence_stay_in_the_owner_ranges()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            h.Collapse();
            var strip = h.BarBounds;
            var expandTimes = new List<int>();
            var collapseTimes = new List<int>();
            for (var phase = 0; phase < 56; phase += 7)
            {
                expandTimes.Add(h.MillisecondsUntil(() => !h.Coordinator.IsCollapsed, Primary.WorkArea.Right - 1, strip.Top + 10, phase));
                h.Hover(Primary.WorkArea.Right - 1, strip.Top + 10, 1600); // the 1.5 s hold passes while at the edge
                collapseTimes.Add(h.MillisecondsUntil(() => h.Coordinator.IsCollapsed, 1000, 1000, phase));
            }

            // Expand: 100 ms dwell + detection latency; collapse: 450 ms delay + latency (then a 160 ms fade).
            Assert.All(expandTimes, t => Assert.InRange(t, 100, 175));
            Assert.All(collapseTimes, t => Assert.InRange(t, 400, 600)); // Owner range 400-600 ms
        });
    }

    [Fact]
    public void A_held_mouse_button_does_not_expand_a_hidden_bar_from_the_edge()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            h.Collapse();
            var strip = h.BarBounds;
            h.Monitors.ButtonDown = true; // e.g. dragging a scroll bar thumb or a window along the edge
            h.Hover(Primary.WorkArea.Right - 1, strip.Top + (strip.Height / 2), 1000);
            Assert.True(h.Coordinator.IsCollapsed);

            h.Monitors.ButtonDown = false;
            h.Hover(Primary.WorkArea.Right - 1, strip.Top + (strip.Height / 2), 200);
            Assert.False(h.Coordinator.IsCollapsed);
        });
    }

    [Fact]
    public void Returning_to_the_bar_during_the_collapse_fade_keeps_it_open()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            var bar = h.BarBounds;
            h.Coordinator.Bar.AnimationsEnabled = true; // the fade starts (it never completes without rendering)
            h.Hover(1000, 1000, 2100); // 1.5 s hold + 450 ms: the fade starts
            if (!h.Coordinator.Bar.IsCollapsing)
            {
                return; // Windows animations are turned off on this machine: there is no fade to interrupt
            }

            h.Hover(bar.Left + 5, bar.Top + 5, 40); // back on the bar while it fades
            Assert.False(h.Coordinator.Bar.IsCollapsing);
            Assert.False(h.Coordinator.IsCollapsed);
            Assert.Equal(bar, h.BarBounds);
        });
    }

    [Fact]
    public void Bar_never_hides_while_a_mouse_button_is_held_on_it()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            var bar = h.BarBounds;
            h.Monitors.ButtonDown = true;
            h.Hover(bar.Left + 5, bar.Top + 5, 2000);
            Assert.False(h.Coordinator.IsCollapsed);
        });
    }

    [Fact]
    public void Docked_bar_on_a_150_percent_monitor_at_negative_coordinates_expands_from_its_edge()
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Left);
            settings.DockMonitor = LeftNegative150.DeviceName;
            using var h = new Harness(settings, Primary, LeftNegative150);
            var expanded = h.BarBounds;
            Assert.Equal(LeftNegative150.WorkArea.Left + 6, expanded.Left); // 4 DIP margin at 150 %

            h.Collapse();
            var strip = h.BarBounds;
            Assert.Equal(LeftNegative150.WorkArea.Left, strip.Left);
            h.Hover(LeftNegative150.WorkArea.Left, strip.Top + (strip.Height / 2), 300);
            Assert.False(h.Coordinator.IsCollapsed);
        });
    }

    [Fact]
    public void Display_change_shows_a_hidden_bar_again_and_a_missing_monitor_falls_back_to_the_primary()
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Left);
            settings.DockMonitor = LeftNegative150.DeviceName;
            using var h = new Harness(settings, Primary, LeftNegative150);
            h.Collapse();

            h.Monitors.All = [Primary]; // the left monitor was disconnected
            h.Monitors.RaiseDisplayChanged();
            Harness.Pump();

            Assert.False(h.Coordinator.IsCollapsed);
            var bar = h.BarBounds;
            Assert.True(Primary.WorkArea.Contains(bar.Left, bar.Top) && Primary.WorkArea.Contains(bar.Right - 1, bar.Bottom - 1),
                $"bar {bar} must be inside the primary work area");
        });
    }

    // ------------------------------------------------------------------ T11: orientation follows the edge

    [Theory]
    [MemberData(nameof(AllEdges))]
    public void Dragging_to_an_edge_docks_with_the_edge_orientation(DockEdge edge)
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Right);
            settings.DockEnabled = false;
            settings.FloatingOrientation = edge is DockEdge.Top or DockEdge.Bottom ? BarOrientation.Vertical : BarOrientation.Horizontal;
            using var h = new Harness(settings, Primary);
            var start = h.BarBounds;
            var cursor = PointAtEdge(Primary.WorkArea, edge, PixelRect.FromSize(1500, 1000, 1, 1));

            h.Drag(PixelRect.FromSize(cursor.X - (start.Width / 2), cursor.Y - (start.Height / 2), start.Width, start.Height), cursor);

            Assert.True(h.Shell.DockEnabled);
            Assert.Equal(edge, h.Shell.DockEdge);
            var end = h.BarBounds;
            Assert.Equal(edge is DockEdge.Left or DockEdge.Right, end.Height > end.Width);
            Assert.True(Primary.WorkArea.Contains(end.Left, end.Top) && Primary.WorkArea.Contains(end.Right - 1, end.Bottom - 1),
                $"docked bar {end} must stay inside the work area");
        });
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    public void Dragging_a_vertical_bar_past_the_top_or_bottom_edge_docks_it_horizontally(DockEdge edge)
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            var start = h.BarBounds;
            Assert.True(start.Height > start.Width, "precondition: docked right = vertical");

            // Grabbed in the middle and pushed to the screen edge: half of the tall bar is past the edge.
            var top = edge == DockEdge.Top ? Primary.WorkArea.Top - (start.Height / 2) : Primary.WorkArea.Bottom - (start.Height / 2);
            var cursorY = edge == DockEdge.Top ? Primary.WorkArea.Top : Primary.WorkArea.Bottom - 1;
            h.Drag(PixelRect.FromSize(1500, top, start.Width, start.Height), (1500, cursorY));

            Assert.True(h.Shell.DockEnabled, "bar should be docked");
            Assert.Equal(edge, h.Shell.DockEdge);
            var end = h.BarBounds;
            Assert.True(end.Width > end.Height, $"bar should be horizontal after docking {edge}, was {end.Width}x{end.Height}");
        });
    }

    [Fact]
    public void Dragging_onto_the_taskbar_docks_to_the_bottom_edge()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            var start = h.BarBounds;
            h.Drag(PixelRect.FromSize(1500, 2100, start.Width, start.Height), (1500, 2350)); // cursor on the taskbar
            Assert.Equal(DockEdge.Bottom, h.Shell.DockEdge);
            Assert.True(h.Shell.DockEnabled);
        });
    }

    [Fact]
    public void Undocking_by_drag_floats_with_the_saved_orientation_and_is_not_re_docked()
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Top);
            settings.FloatingOrientation = BarOrientation.Vertical;
            using var h = new Harness(settings, Primary);
            var start = h.BarBounds;

            h.Drag(PixelRect.FromSize(1500, 900, start.Width, start.Height), (1600, 920));

            Assert.False(h.Shell.DockEnabled);
            var end = h.BarBounds;
            Assert.True(end.Height > end.Width, "floating bar uses the saved (vertical) orientation");
            h.Hover(1000, 1000, 3000);
            Assert.False(h.Shell.DockEnabled); // never forced back to an edge
            Assert.False(h.Coordinator.IsCollapsed); // a floating bar never hides
        });
    }

    [Fact]
    public void Floating_bar_dropped_across_two_monitors_is_not_docked_to_their_shared_edge()
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Right);
            settings.DockEnabled = false;
            settings.FloatingOrientation = BarOrientation.Horizontal;
            using var h = new Harness(settings, Primary, RightNeighbor);
            var start = h.BarBounds;

            // Spans the shared edge at x = 3840; the cursor is 360 px into the neighbor monitor.
            h.Drag(PixelRect.FromSize(3500, 500, start.Width, start.Height), (4200, 520));

            Assert.False(h.Shell.DockEnabled);
        });
    }

    [Fact]
    public void Drag_that_ends_where_it_started_leaves_no_preview()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            var start = h.BarBounds;
            h.Coordinator.OnBarDragStarted();
            h.Monitors.Cursor = (start.Right - 2, start.Top + 10);
            h.Coordinator.UpdateDragPreview();
            Assert.Equal(DockEdge.Right, h.Coordinator.PreviewEdge);

            h.Coordinator.OnBarDragCompleted(); // released without moving
            Assert.Null(h.Coordinator.PreviewEdge);
        });
    }

    [Fact]
    public void Drag_preview_follows_the_edge_under_the_cursor()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            var start = h.BarBounds;
            h.Coordinator.OnBarDragStarted();

            h.Placement.SetBounds(h.BarHandle, PixelRect.FromSize(1500, 1000, start.Width, start.Height));
            h.Monitors.Cursor = (1520, 1100);
            h.Coordinator.UpdateDragPreview();
            Assert.Null(h.Coordinator.PreviewEdge);

            h.Placement.SetBounds(h.BarHandle, PixelRect.FromSize(1500, -50, start.Width, start.Height));
            h.Monitors.Cursor = (1520, 10);
            h.Coordinator.UpdateDragPreview();
            Assert.Equal(DockEdge.Top, h.Coordinator.PreviewEdge);

            h.Coordinator.OnBarDragCompleted();
            Assert.Null(h.Coordinator.PreviewEdge);
            Assert.Equal(DockEdge.Top, h.Shell.DockEdge);
        });
    }

    // ------------------------------------------------------------------ T10: floating orientation option

    [Theory]
    [InlineData(BarOrientation.Horizontal)]
    [InlineData(BarOrientation.Vertical)]
    public void Floating_orientation_option_changes_a_floating_bar(BarOrientation orientation)
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Right);
            settings.DockEnabled = false;
            settings.FloatingOrientation = orientation == BarOrientation.Horizontal ? BarOrientation.Vertical : BarOrientation.Horizontal;
            using var h = new Harness(settings, Primary);

            h.Shell.FloatingOrientation = orientation;
            Harness.Pump();

            var end = h.BarBounds;
            Assert.Equal(orientation == BarOrientation.Horizontal, end.Width > end.Height);
        });
    }

    [Fact]
    public void Floating_orientation_option_does_not_override_a_docked_edge()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            h.Shell.FloatingOrientation = BarOrientation.Horizontal;
            Harness.Pump();
            var end = h.BarBounds;
            Assert.True(end.Height > end.Width, "docked right stays vertical");
        });
    }

    [Fact]
    public void Docked_layout_is_restored_after_a_restart()
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Right);
            PixelRect before;
            using (var h = new Harness(settings, Primary))
            {
                var start = h.BarBounds;
                h.Drag(PixelRect.FromSize(800, 2200, start.Width, start.Height), (900, 2290));
                before = h.BarBounds;
                Assert.Equal(DockEdge.Bottom, settings.DockEdge);
            }

            using var restarted = new Harness(settings, Primary);
            Assert.Equal(DockEdge.Bottom, restarted.Shell.DockEdge);
            Assert.Equal(before, restarted.BarBounds);
        });
    }

    // ------------------------------------------------------------------ tray / lifetime (fixed behavior)

    [Fact]
    public void Closing_the_main_window_keeps_running_with_the_bar_and_the_tray_icon()
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Right);
            settings.Mode = WindowMode.Normal;
            using var h = new Harness(settings, Primary);
            Assert.True(h.Tray!.Visible);

            h.Coordinator.MainWindow.Close();
            Harness.Pump();

            Assert.False(h.Coordinator.MainWindow.IsVisible);
            Assert.True(h.Shell.IsCompact);
            Assert.True(h.Coordinator.Bar.IsVisible);
            Assert.False(h.Tray.Disposed);
            Assert.True(h.Tray.Visible);
        });
    }

    [Fact]
    public void Tray_click_brings_back_a_hidden_bar()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            h.Collapse();
            h.Tray!.Click();
            Harness.Pump();
            Assert.False(h.Coordinator.IsCollapsed);
            Assert.True(h.Coordinator.Bar.IsVisible);
        });
    }

    [Fact]
    public void Tray_menu_offers_show_bar_settings_and_exit()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            var texts = h.Coordinator.BuildMenuModel().Where(i => !i.IsSeparator).Select(i => i.Text).ToList();
            Assert.Equal(Loc("Menu_ShowToolbar"), texts[0]);
            Assert.Contains(Loc("Menu_Settings"), texts);
            Assert.Equal(Loc("Menu_Exit"), texts[^1]);
            Assert.True(h.Coordinator.BuildMenuModel()[0].IsDefault); // left click = show the bar
        });
    }

    [Fact]
    public void Exit_releases_the_tray_icon_and_timers()
    {
        Run(() =>
        {
            using var h = new Harness(Docked(DockEdge.Right), Primary);
            h.Coordinator.Exit();
            Harness.Pump();
            Assert.True(h.Tray!.Disposed);
            Assert.False(h.Coordinator.Bar.IsVisible);
        });
    }

    [Fact]
    public void A_second_start_with_the_same_settings_file_signals_the_first_and_does_not_run()
    {
        var path = Path.Combine(Path.GetTempPath(), "browserflip-single-" + Guid.NewGuid().ToString("N") + ".json");
        using var first = SingleInstance.TryAcquire(path);
        Assert.NotNull(first);
        using var shown = new ManualResetEventSlim();
        first.ShowRequested += (_, _) => shown.Set();

        var second = SingleInstance.TryAcquire(path.ToUpperInvariant()); // same file, other spelling
        Assert.Null(second);
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)), "the running instance is asked to show itself");

        using var other = SingleInstance.TryAcquire(path + ".other"); // another settings file = independent instance
        Assert.NotNull(other);
    }

    // ------------------------------------------------------------------ harness

    private static string Loc(string key) => BrowserFlip.App.Localization.Loc.Instance[key];

    internal static UserSettings Docked(DockEdge edge)
    {
        var s = UserSettings.InMemory();
        s.Mode = WindowMode.Compact;
        s.DockEnabled = true;
        s.DockEdge = edge;
        s.DockMonitor = Primary.DeviceName;
        s.DockPosition = 0.5;
        return s;
    }

    /// <summary>A point at <paramref name="edge"/> of <paramref name="work"/>, centered on <paramref name="along"/>.</summary>
    internal static (int X, int Y) PointAtEdge(PixelRect work, DockEdge edge, PixelRect along) => edge switch
    {
        DockEdge.Left => (work.Left, along.Top + (along.Height / 2)),
        DockEdge.Right => (work.Right - 1, along.Top + (along.Height / 2)),
        DockEdge.Top => (along.Left + (along.Width / 2), work.Top),
        _ => (along.Left + (along.Width / 2), work.Bottom - 1),
    };

    internal static void Run(Action body)
    {
        EnsureApplication();
        Sta.Run(body);
    }

    /// <summary>
    /// Creates the WPF application (resources, themes) on a throw-away STA thread whose dispatcher never
    /// runs. The <see cref="Application"/> constructor queues its startup on the creating thread: pumping
    /// that thread (as these tests do) would run the real <c>App.OnStartup</c> — real settings file, real
    /// windows and tray icon on the desktop. Never create it on a test thread that pumps.
    /// </summary>
    internal static void EnsureApplication()
    {
        if (Application.Current is not null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            var app = new BrowserFlip.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            ThemeManager.Initialize(app, ThemePreference.Light);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    internal sealed class Harness : IDisposable
    {
        private DateTimeOffset _now = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

        public Harness(UserSettings settings, params MonitorInfo[] monitors)
        {
            Settings = settings;
            Monitors = new FakeMonitors(monitors);
            Main = CreateMain(settings);
            Shell = new ShellViewModel(settings);
            Coordinator = new DesktopCoordinator(null, Main, Shell, Monitors, Placement, () => Tray = new FakeTray())
            {
                Now = () => _now,
                ShowDragPreview = false,
                AllowActivation = false, // never take the focus from other apps on a shared desktop
            };

            // Real windows, parked far off-screen and never activated: nothing appears on the desktop.
            Coordinator.Bar.AnimationsEnabled = false;
            Coordinator.Bar.ShowActivated = false;
            Coordinator.Bar.Left = -32000;
            Coordinator.MainWindow.ShowActivated = false;
            Coordinator.MainWindow.Left = -32000;
            Coordinator.Start();
            Pump();
        }

        public UserSettings Settings { get; }

        public FakeMonitors Monitors { get; }

        public FakePlacement Placement { get; } = new();

        public FakeTray? Tray { get; private set; }

        public MainViewModel Main { get; }

        public ShellViewModel Shell { get; }

        public DesktopCoordinator Coordinator { get; }

        public IntPtr BarHandle => new WindowInteropHelper(Coordinator.Bar).Handle;

        public PixelRect BarBounds => Placement.GetBounds(BarHandle);

        /// <summary>Keeps the cursor at (x, y) for the given time, ticking auto-hide every 40 ms.</summary>
        public void Hover(int x, int y, int milliseconds)
        {
            Monitors.Cursor = (x, y);
            for (var t = 0; t <= milliseconds; t += 40)
            {
                Coordinator.AutoHideTick();
                _now += TimeSpan.FromMilliseconds(40);
            }

            Pump();
        }

        /// <summary>
        /// Real timer cadence: the cursor arrives <paramref name="phaseMs"/> after the last tick, then the
        /// coordinator is ticked at the interval it asks for plus 16 ms (DispatcherTimer resolution).
        /// Returns the milliseconds from arrival until <paramref name="done"/> is true.
        /// </summary>
        public int MillisecondsUntil(Func<bool> done, int x, int y, int phaseMs)
        {
            var lastTick = _now;
            var arrive = lastTick + TimeSpan.FromMilliseconds(phaseMs);
            var next = lastTick + Coordinator.PollInterval + TimeSpan.FromMilliseconds(16);
            if (next < arrive)
            {
                next = arrive;
            }

            Monitors.Cursor = (x, y);
            for (var i = 0; i < 200; i++)
            {
                _now = next;
                Coordinator.AutoHideTick();
                if (done())
                {
                    return (int)(_now - arrive).TotalMilliseconds;
                }

                next = _now + Coordinator.PollInterval + TimeSpan.FromMilliseconds(16);
            }

            return int.MaxValue;
        }

        /// <summary>Moves the cursor away until the docked bar has collapsed.</summary>
        public void Collapse()
        {
            Hover(1000, 1000, 2500);
            Assert.True(Coordinator.IsCollapsed, "precondition: the bar collapses when the cursor is away");
        }

        /// <summary>Simulates a DragMove that ends with the bar at <paramref name="end"/> and the cursor at <paramref name="cursor"/>.</summary>
        public void Drag(PixelRect end, (int X, int Y) cursor)
        {
            Coordinator.OnBarDragStarted();
            Placement.SetBounds(BarHandle, end);
            Monitors.Cursor = cursor;
            Coordinator.UpdateDragPreview();
            Coordinator.OnBarDragCompleted();
            Pump();
        }

        public static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

        public void Dispose()
        {
            Coordinator.Exit(); // closes both windows (they unsubscribe from the shared Loc instance)
            Coordinator.Dispose();
            Shell.Dispose();
            Main.Dispose();
        }

        private static MainViewModel CreateMain(UserSettings settings)
        {
            var reg = new FakeRegistry();
            var fs = new FakeFileSystem();
            foreach (var (id, exe, progId) in new[]
            {
                ("Google Chrome", @"C:\C\chrome.exe", "ChromeHTML"),
                ("Microsoft Edge", @"C:\E\msedge.exe", "MSEdgeHTM"),
                ("Mozilla Firefox", @"C:\F\firefox.exe", "FirefoxURL"),
            })
            {
                reg.AddBrowser(LocalMachine, Registry64, id, id, $"\"{exe}\"", progId, progId, id);
                fs.Files.Add(exe);
            }

            var assoc = new FakeAssociations().With("http", "MSEdgeHTM").With("https", "MSEdgeHTM");
            var policy = new PolicyInspector(reg);
            var main = new MainViewModel(new BrowserDetector(reg, fs, new FakeStrings()), new Defaults.DefaultBrowserService(assoc),
                new SettingsLauncher(new FakeLauncher(), new FakeOs(26100), policy), policy, _ => null, settings, startTimer: false);
            main.Refresh();
            return main;
        }
    }

    internal sealed class FakeMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitorService
    {
        public event EventHandler? DisplayChanged;

        public IReadOnlyList<MonitorInfo> All { get; set; } = monitors;

        public (int X, int Y)? Cursor { get; set; } = (1000, 1000);

        public bool ButtonDown { get; set; }

        public IReadOnlyList<MonitorInfo> GetMonitors() => All;

        public MonitorInfo FromPoint(int x, int y)
            => All.FirstOrDefault(m => m.Bounds.Contains(x, y))
               ?? All.MinBy(m => Distance(m.Bounds, x, y))!;

        public MonitorInfo FromWindow(IntPtr hwnd) => All.First(m => m.IsPrimary);

        public (int X, int Y)? GetCursorPosition() => Cursor;

        public bool IsFullScreenOrPresentationActive() => false;

        public bool IsMouseButtonDown() => ButtonDown;

        public void RaiseDisplayChanged() => DisplayChanged?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
        }

        private static long Distance(PixelRect r, int x, int y)
        {
            var dx = Math.Max(0, Math.Max(r.Left - x, x - (r.Right - 1)));
            var dy = Math.Max(0, Math.Max(r.Top - y, y - (r.Bottom - 1)));
            return ((long)dx * dx) + ((long)dy * dy);
        }
    }

    internal sealed class FakePlacement : IWindowPlacement
    {
        private readonly Dictionary<IntPtr, PixelRect> _bounds = [];

        public PixelRect GetBounds(IntPtr hwnd) => _bounds.GetValueOrDefault(hwnd);

        public void SetBounds(IntPtr hwnd, PixelRect bounds) => _bounds[hwnd] = bounds;

        public void Move(IntPtr hwnd, int left, int top)
        {
            var b = GetBounds(hwnd);
            _bounds[hwnd] = PixelRect.FromSize(left, top, b.Width, b.Height);
        }
    }

    internal sealed class FakeTray : ITrayIcon
    {
        public event EventHandler? Activated;

        public event EventHandler? MenuRequested;

        public bool Visible { get; set; }

        public string ToolTip { get; set; } = string.Empty;

        public bool Disposed { get; private set; }

        public void Click() => Activated?.Invoke(this, EventArgs.Empty);

        public void RightClick() => MenuRequested?.Invoke(this, EventArgs.Empty);

        public void UpdateIcon()
        {
        }

        public void ShowMenu(IReadOnlyList<TrayMenuItem> items)
        {
        }

        public void Dispose() => Disposed = true;
    }
}
