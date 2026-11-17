using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrowserFlip.App;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;
using static BrowserFlip.Core.Tests.Desktop.DesktopCoordinatorTests;

namespace BrowserFlip.Core.Tests.Desktop;

/// <summary>
/// BF-05: every detected browser is shown in the compact bar, in every orientation, docked or floating,
/// after hiding / expanding and orientation changes. Uses the real bar window laid out by the real coordinator.
/// </summary>
public sealed class CompactBarContentTests
{
    /// <summary>Browser buttons of the bar that are fully inside the bar's visible area.</summary>
    internal static (int Generated, int FullyVisible, string Detail) VisibleBrowsers(CompactBarWindow bar)
    {
        bar.UpdateLayout();
        var items = FindChild<ItemsControl>(bar)!;
        var root = (FrameworkElement)bar.Content;
        var generated = 0;
        var visible = 0;
        var detail = new List<string>();
        for (var i = 0; i < items.Items.Count; i++)
        {
            if (items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
            {
                detail.Add($"#{i}: no container");
                continue;
            }

            generated++;
            var button = FindChild<Button>(container);
            var target = (FrameworkElement?)button ?? container;
            if (target.ActualWidth < 1 || target.ActualHeight < 1)
            {
                detail.Add($"#{i}: {target.ActualWidth:N0}x{target.ActualHeight:N0}");
                continue;
            }

            var topLeft = target.TranslatePoint(new Point(0, 0), root);
            var bottomRight = target.TranslatePoint(new Point(target.ActualWidth, target.ActualHeight), root);
            if (topLeft.X >= -0.5 && topLeft.Y >= -0.5 && bottomRight.X <= root.ActualWidth + 0.5 && bottomRight.Y <= root.ActualHeight + 0.5)
            {
                visible++;
            }
            else
            {
                detail.Add($"#{i}: ({topLeft.X:N0},{topLeft.Y:N0})-({bottomRight.X:N0},{bottomRight.Y:N0}) in {root.ActualWidth:N0}x{root.ActualHeight:N0}");
            }
        }

        return (generated, visible, string.Join("; ", detail));
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
            {
                return found;
            }

            if (FindChild<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    private static void AssertAllBrowsersVisible(Harness h, string label)
    {
        Harness.Pump();
        var (generated, visible, detail) = VisibleBrowsers(h.Coordinator.Bar);
        var count = h.Main.Browsers.Count;
        Assert.True(count > 0, "precondition: browsers detected");
        Assert.True(generated == count && visible == count,
            $"{label}: {visible}/{count} browsers fully visible ({generated} generated), bar {h.BarBounds}; {detail}");
    }

    public static TheoryData<string> Layouts => new() { "floating-horizontal", "floating-vertical", "Top", "Bottom", "Left", "Right" };

    private static UserSettings SettingsFor(string layout)
    {
        var s = Docked(DockEdge.Right);
        if (layout.StartsWith("floating", StringComparison.Ordinal))
        {
            s.DockEnabled = false;
            s.FloatingOrientation = layout.EndsWith("horizontal", StringComparison.Ordinal) ? BarOrientation.Horizontal : BarOrientation.Vertical;
            s.CompactBounds = PixelRect.FromSize(800, 600, 100, 100);
        }
        else
        {
            s.DockEdge = Enum.Parse<DockEdge>(layout);
        }

        return s;
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void A_bar_measured_before_its_browser_buttons_exist_reports_it(string layout)
    {
        // BF-05 root cause: before the bar window has been laid out while shown, its browser buttons do not exist and
        // a measurement covers only the app icon, pin and options buttons.
        Run(() =>
        {
            using var h = new Harness(SettingsFor(layout), Primary);
            var bar = new CompactBarWindow { DataContext = new DesktopContext(h.Main, h.Shell), ShowActivated = false };
            try
            {
                _ = new System.Windows.Interop.WindowInteropHelper(bar).EnsureHandle(); // like DesktopCoordinator.Start
                var size = bar.MeasureExpanded(2000);
                var length = h.Shell.IsVertical ? size.Height : size.Width;
                Assert.False(bar.LastMeasureComplete, "the incomplete measurement is detected");
                Assert.True(length < (3 + h.Main.Browsers.Count) * h.Shell.BarButtonSize, $"{layout}: {length:N1} DIP (the BF-05 length)");
            }
            finally
            {
                bar.Close();
            }
        });
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void A_bar_sized_before_its_browser_buttons_existed_is_sized_again_once_they_do(string layout)
    {
        // Start-up in compact mode: the first layout runs inside start-up, before the bar was ever laid out, so it
        // measures without the browsers (the BF-05 length). The bar must be sized again once the buttons exist,
        // whatever path showed it (no reliance on a single re-layout queued after Show).
        // A saved, user-chosen main window width: start-up does not measure the main window header (BF-07 first-run
        // width), which keeps the bar's first measurement ahead of any layout work, as in v0.2 (BF-05 path).
        Run(() =>
        {
            var s = SettingsFor(layout);
            s.BarSize = BarSize.Medium;
            s.MainBounds = PixelRect.FromSize(400, 300, 1300, 900);
            s.MainScale = 2.25;
            s.MainWidthByUser = true;
            using var h = new Harness(s, Primary); // Start() + pump
            Assert.True(h.Coordinator.IncompleteMeasurements > 0, "precondition: the start-up measurement missed the browsers");
            Assert.True(h.Coordinator.IncompleteMeasureRelayouts > 0,
                $"the size from the incomplete measurement was corrected (incomplete {h.Coordinator.IncompleteMeasurements}, corrections {h.Coordinator.IncompleteMeasureRelayouts})");
            AssertAllBrowsersVisible(h, $"start-up {layout}");
        });
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_browser_is_shown_at_a_first_start_in_compact_mode(string layout)
    {
        // BF-07 first run: the main window width is measured from its header during start-up (no saved bounds).
        Run(() =>
        {
            var s = SettingsFor(layout);
            s.BarSize = BarSize.Medium;
            s.MainBounds = null;
            using var h = new Harness(s, Primary);
            AssertAllBrowsersVisible(h, $"first start {layout}");
        });
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_browser_is_shown_when_the_bar_starts_in_compact_mode(string layout)
    {
        Run(() =>
        {
            using var h = new Harness(SettingsFor(layout), Primary);
            AssertAllBrowsersVisible(h, layout);
        });
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_browser_is_shown_after_hiding_and_expanding_again(string layout)
    {
        Run(() =>
        {
            using var h = new Harness(SettingsFor(layout), Primary);
            if (!h.Shell.DockEnabled)
            {
                return; // a floating bar never hides
            }

            for (var i = 0; i < 3; i++)
            {
                h.Collapse();
                var strip = h.BarBounds;
                var (x, y) = PointAtEdge(Primary.WorkArea, h.Shell.DockEdge, strip);
                h.Hover(x, y, 300);
                Assert.False(h.Coordinator.IsCollapsed);
                AssertAllBrowsersVisible(h, $"{layout} cycle {i}");
            }
        });
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void Every_browser_is_shown_after_docking_the_main_window_by_drag(DockEdge edge)
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Right);
            settings.Mode = WindowMode.Normal;
            using var h = new Harness(settings, Primary);
            h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(2600, 400, 1170, 800));
            h.Monitors.Cursor = PointAtEdge(Primary.WorkArea, edge, PixelRect.FromSize(1500, 1000, 1, 1));
            h.Coordinator.OnMainDragCompleted(PixelRect.FromSize(900, 400, 1170, 800));
            Harness.Pump();
            Assert.True(h.Coordinator.IsCollapsed);

            var strip = h.BarBounds;
            var (x, y) = PointAtEdge(Primary.WorkArea, edge, strip);
            h.Hover(x, y, 300);
            AssertAllBrowsersVisible(h, $"main drag -> {edge}");
        });
    }

    [Fact]
    public void Every_browser_is_shown_after_switching_orientation_back_and_forth()
    {
        Run(() =>
        {
            using var h = new Harness(SettingsFor("floating-horizontal"), Primary);
            foreach (var edge in new[] { DockEdge.Top, DockEdge.Right, DockEdge.Bottom, DockEdge.Left, DockEdge.Top })
            {
                var start = h.BarBounds;
                var cursor = PointAtEdge(Primary.WorkArea, edge, PixelRect.FromSize(1500, 1000, 1, 1));
                h.Drag(PixelRect.FromSize(cursor.X - (start.Width / 2), cursor.Y - (start.Height / 2), start.Width, start.Height), cursor);
                AssertAllBrowsersVisible(h, $"dragged to {edge}");
            }

            h.Drag(PixelRect.FromSize(1500, 900, h.BarBounds.Width, h.BarBounds.Height), (1550, 920)); // float again
            AssertAllBrowsersVisible(h, "floating again");
        });
    }

    [Theory]
    [InlineData(BarSize.Small, DockEdge.Top)]
    [InlineData(BarSize.Medium, DockEdge.Top)]
    [InlineData(BarSize.Medium, DockEdge.Bottom)]
    [InlineData(BarSize.Medium, DockEdge.Left)]
    public void Every_browser_is_shown_when_a_vertical_floating_bar_is_docked_by_dragging_the_bar(BarSize size, DockEdge edge)
    {
        Run(() =>
        {
            var s = SettingsFor("floating-vertical");
            s.BarSize = size;
            using var h = new Harness(s, Primary);
            AssertAllBrowsersVisible(h, "floating vertical");
            var start = h.BarBounds;
            var cursor = PointAtEdge(Primary.WorkArea, edge, PixelRect.FromSize(1500, 1000, 1, 1));
            h.Drag(PixelRect.FromSize(cursor.X - (start.Width / 2), cursor.Y - (start.Height / 2), start.Width, start.Height), cursor);
            AssertAllBrowsersVisible(h, $"{size} vertical -> {edge}");
        });
    }

    [Theory]
    [InlineData(BarSize.Small)]
    [InlineData(BarSize.Medium)]
    public void Every_browser_is_shown_when_the_main_window_is_dragged_to_the_top_with_a_vertical_floating_setting(BarSize size)
    {
        Run(() =>
        {
            var s = SettingsFor("floating-vertical");
            s.Mode = WindowMode.Normal;
            s.BarSize = size;
            using var h = new Harness(s, Primary);
            h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(2600, 400, 1170, 800));
            h.Monitors.Cursor = (1500, Primary.WorkArea.Top);
            h.Coordinator.OnMainDragCompleted(PixelRect.FromSize(900, 400, 1170, 800));
            Harness.Pump();
            var strip = h.BarBounds;
            h.Hover(strip.Left + (strip.Width / 2), Primary.WorkArea.Top, 300);
            AssertAllBrowsersVisible(h, $"{size} main drag -> top");
        });
    }

    [Theory]
    [InlineData("Top")]
    [InlineData("Right")]
    [InlineData("floating-horizontal")]
    public void The_bar_follows_a_change_in_the_number_of_browsers(string layout)
    {
        Run(() =>
        {
            using var h = new Harness(SettingsFor(layout), Primary);
            AssertAllBrowsersVisible(h, "3 browsers");
            var three = h.BarBounds;

            foreach (var (id, exe, progId) in new[] { ("Brave", @"C:\B\brave.exe", "BraveHTML"), ("Vivaldi", @"C:\V\vivaldi.exe", "VivaldiHTM") })
            {
                h.Registry.AddBrowser(BrowserFlip.Core.Platform.RegistryRoot.LocalMachine, BrowserFlip.Core.Platform.RegistryViewKind.Registry64,
                    id, id, $"\"{exe}\"", progId, progId, id);
                h.Files.Files.Add(exe);
            }

            h.Main.Refresh(); // e.g. the refresh button / F5 after installing browsers
            AssertAllBrowsersVisible(h, "5 browsers");
            var five = h.BarBounds;
            Assert.True(Math.Max(five.Width, five.Height) > Math.Max(three.Width, three.Height), $"bar grows: {three} -> {five}");
        });
    }

    [Theory]
    [InlineData("Top")]
    [InlineData("Bottom")]
    [InlineData("Left")]
    [InlineData("floating-horizontal")]
    public void A_browser_list_that_no_longer_fits_makes_the_bar_size_again(string layout)
    {
        Run(() =>
        {
            using var h = new Harness(SettingsFor(layout), Primary);
            AssertAllBrowsersVisible(h, "before");
            var vertical = h.Shell.IsVertical;

            // The browser list grows without any of the usual layout triggers (no default-browser text change):
            // only the real layout of the list shows that it no longer fits.
            var before = h.Coordinator.OverflowRelayouts;
            h.Main.Browsers.Add(h.Main.Browsers[0]);
            h.Main.Browsers.Add(h.Main.Browsers[1]);
            Harness.Pump();
            var sc = h.Coordinator.Bar.BrowserScroller;
            Assert.True(h.Coordinator.OverflowRelayouts > before,
                $"the overflow was detected (extent {sc.ExtentWidth:N0}x{sc.ExtentHeight:N0}, viewport {sc.ViewportWidth:N0}x{sc.ViewportHeight:N0})");
            AssertAllBrowsersVisible(h, $"{layout} after the list grew");
        });
    }

    [Fact]
    public void Browsers_that_cannot_fit_on_a_small_screen_get_a_scroll_bar_instead_of_disappearing()
    {
        Run(() =>
        {
            var tiny = new MonitorInfo(@"\\.\DISPLAY9", new PixelRect(0, 0, 230, 400), new PixelRect(0, 0, 230, 400), 1.0, true);
            var s = SettingsFor("Top");
            s.DockMonitor = tiny.DeviceName;
            using var h = new Harness(s, tiny);
            Harness.Pump();
            var scroller = h.Coordinator.Bar.BrowserScroller;
            Assert.Equal(Visibility.Visible, scroller.ComputedHorizontalScrollBarVisibility);
            Assert.True(scroller.ViewportHeight >= h.Shell.BarButtonSize, $"buttons not covered by the scroll bar: viewport height {scroller.ViewportHeight:N1}");
            Assert.True(scroller.ExtentWidth >= h.Main.Browsers.Count * h.Shell.BarButtonSize, "every browser is in the scrollable list");
            Assert.True(h.BarBounds.Right <= tiny.WorkArea.Right);
        });
    }

    [Fact]
    public void A_bar_that_fits_shows_no_scroll_bar()
    {
        Run(() =>
        {
            using var h = new Harness(SettingsFor("Top"), Primary);
            Harness.Pump();
            var scroller = h.Coordinator.Bar.BrowserScroller;
            Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Hidden, scroller.HorizontalScrollBarVisibility);
            Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Hidden, scroller.VerticalScrollBarVisibility);
        });
    }

    // ------------------------------------------------------------------ BF-06: no close button on the bar

    [Theory]
    [MemberData(nameof(Layouts))]
    public void The_compact_bar_has_no_close_button_in_any_layout(string layout)
    {
        Run(() =>
        {
            using var h = new Harness(SettingsFor(layout), Primary);
            Harness.Pump();
            var bar = h.Coordinator.Bar;
            Assert.Null(bar.FindName("CloseButton"));
            var panelButtons = bar.BarPanel.Children.OfType<System.Windows.Controls.Primitives.ButtonBase>().Count();
            Assert.Equal(3, panelButtons); // app icon (main window), pin, display options; the browsers are in the list
            foreach (var glyph in FindAll<System.Windows.Controls.TextBlock>(bar))
            {
                Assert.NotEqual("\uE711", glyph.Text); // no "close" glyph anywhere in the bar
            }
        });
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
            {
                yield return found;
            }

            foreach (var deeper in FindAll<T>(child))
            {
                yield return deeper;
            }
        }
    }

    [Fact]
    public void Compact_button_from_the_main_window_shows_every_browser()
    {
        Run(() =>
        {
            var settings = Docked(DockEdge.Right);
            settings.Mode = WindowMode.Normal;
            settings.FloatingOrientation = BarOrientation.Horizontal;
            using var h = new Harness(settings, Primary);
            h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(900, 400, 1170, 800));
            h.Shell.EnterCompactCommand.Execute(null);
            AssertAllBrowsersVisible(h, "compact button");
        });
    }
}
