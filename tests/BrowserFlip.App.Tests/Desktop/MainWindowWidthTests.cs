using BrowserFlip.App.Controls;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.Core.Tests.UI;
using static BrowserFlip.Core.Tests.Desktop.DesktopCoordinatorTests;

namespace BrowserFlip.Core.Tests.Desktop;

/// <summary>
/// BF-07 (R4-T04, T06..T09): main window width at first start, after an upgrade, after the user resized it, on
/// monitors with different scaling and work areas, and after a language switch. Real windows, parked off-screen,
/// never activated (shared desktop).
/// </summary>
public sealed class MainWindowWidthTests
{
    private static UserSettings MainOpen(PixelRect? bounds = null, double scale = 2.25, bool? byUser = null, string language = "")
    {
        var s = Docked(DockEdge.Right);
        s.Mode = WindowMode.Normal;
        s.MainBounds = bounds;
        s.MainScale = scale;
        s.MainWidthByUser = byUser;
        s.Language = language;
        return s;
    }

    private static MonitorInfo Monitor(int width, int height, double scale, int taskbar = 48)
        => new(@"\\.\DISPLAY1", new PixelRect(0, 0, width, height), new PixelRect(0, 0, width, height - taskbar), scale, true);

    private static void InLanguage(string language, Action body)
    {
        Run(() =>
        {
            Loc.Instance.SetLanguage(language);
            try
            {
                body();
            }
            finally
            {
                Loc.Instance.SetLanguage("en");
            }
        });
    }

    /// <summary>
    /// The width set before the first show is the measured preferred width; measured again once shown it can come out
    /// a few DIP smaller (frame / pixel snapping), never larger: the window is never narrower than its header needs.
    /// </summary>
    private static void AssertPreferredWidth(double expected, double actual, string label)
    {
        Assert.True(actual >= expected - 0.5 && actual <= expected + 6, $"{label}: width {actual:N1}, preferred {expected:N1}");
    }

    private static List<string> HeaderProblems(Harness h, string label)
    {
        var w = h.Coordinator.MainWindow;
        return MainWindowHeaderTests.Problems(MainWindowHeaderTests.LayoutHeader(w, w.Width), label);
    }

    // ------------------------------------------------------------------ first start

    [Theory]
    [InlineData("ja")]
    [InlineData("zh-CN")]
    [InlineData("en")]
    public void R4_T01_T03_first_start_uses_the_measured_width_and_the_header_is_on_one_line(string language)
    {
        InLanguage(language, () =>
        {
            using var h = new Harness(MainOpen(), Primary);
            var w = h.Coordinator.MainWindow;
            AssertPreferredWidth(w.PreferredWidth(), w.Width, language);
            Assert.True(w.WidthIsAutomatic);
            var l = MainWindowHeaderTests.LayoutHeader(w, w.Width);
            Assert.Equal(HeaderLayoutMode.SingleLine, l.Mode);
            Assert.Empty(MainWindowHeaderTests.Problems(l, language));
        });
    }

    // ------------------------------------------------------------------ R4-T04: language switch

    [Fact]
    public void R4_T04_an_app_chosen_width_follows_the_header_of_the_new_language()
    {
        InLanguage("en", () =>
        {
            using var h = new Harness(MainOpen(language: "en"), Primary);
            var w = h.Coordinator.MainWindow;
            var en = w.Width;
            Loc.Instance.SetLanguage("ja");
            Harness.Pump();
            AssertPreferredWidth(w.PreferredWidth(), w.Width, "en -> ja");
            Assert.True(w.Width >= en, $"ja {w.Width:N0} >= en {en:N0}");
            Assert.Empty(HeaderProblems(h, "en -> ja"));
            Loc.Instance.SetLanguage("zh-CN");
            Harness.Pump();
            Assert.Empty(HeaderProblems(h, "ja -> zh-CN"));
        });
    }

    [Fact]
    public void R4_T04_a_user_chosen_width_is_kept_when_the_language_changes_and_the_header_adapts()
    {
        InLanguage("en", () =>
        {
            using var h = new Harness(MainOpen(PixelRect.FromSize(300, 300, 1080, 900), byUser: true, language: "en"), Primary); // 480 DIP
            var w = h.Coordinator.MainWindow;
            Assert.Equal(480, w.Width, 1);
            Loc.Instance.SetLanguage("ja");
            Harness.Pump();
            Assert.Equal(480, w.Width, 1);
            Assert.Empty(HeaderProblems(h, "ja at the user's 480"));
        });
    }

    // ------------------------------------------------------------------ R4-T06: old too-small saved size

    [Theory]
    [InlineData("ja")]
    [InlineData("zh-CN")]
    [InlineData("en")]
    public void R4_T06_a_too_narrow_width_saved_before_BF07_is_widened_once_and_then_kept(string language)
    {
        InLanguage(language, () =>
        {
            // The Owner's v0.2 setting: 1148 px at 225 % = 510 DIP, saved without MainWidthByUser.
            var settings = MainOpen(PixelRect.FromSize(800, 400, 1148, 1500), byUser: null);
            double preferred;
            using (var h = new Harness(settings, Primary))
            {
                var w = h.Coordinator.MainWindow;
                preferred = w.PreferredWidth();
                AssertPreferredWidth(Math.Max(510, preferred), w.Width, $"{language} upgraded");
                Assert.Empty(HeaderProblems(h, $"{language} upgraded"));
                h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(800, 400, (int)Math.Round(w.Width * 2.25), 1500));
            }

            // Saved again (exit) as an app-chosen width: no second adjustment, no reset of a user width later.
            Assert.False(settings.MainWidthByUser);
            using var again = new Harness(settings, Primary);
            AssertPreferredWidth(preferred, again.Coordinator.MainWindow.Width, $"{language} second start");
        });
    }

    [Fact]
    public void R4_T06_a_wider_width_saved_before_BF07_is_never_narrowed()
    {
        InLanguage("ja", () =>
        {
            using var h = new Harness(MainOpen(PixelRect.FromSize(800, 400, 1800, 1500), byUser: null), Primary); // 800 DIP
            Assert.Equal(800, h.Coordinator.MainWindow.Width, 1);
        });
    }

    // ------------------------------------------------------------------ R4-T07: custom size kept

    [Theory]
    [InlineData(1080)] // 480 DIP: narrower than the Japanese one-line width -> kept, header wraps
    [InlineData(1600)] // 711 DIP
    public void R4_T07_a_width_the_user_chose_is_kept_on_every_start(int pixels)
    {
        InLanguage("ja", () =>
        {
            var settings = MainOpen(PixelRect.FromSize(800, 400, pixels, 1500), byUser: true);
            for (var start = 0; start < 3; start++)
            {
                using var h = new Harness(settings, Primary);
                var w = h.Coordinator.MainWindow;
                Assert.Equal(pixels / 2.25, w.Width, 1);
                Assert.False(w.WidthIsAutomatic);
                Assert.Empty(HeaderProblems(h, $"start {start}"));
                h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(800, 400, pixels, 1500));
            }

            Assert.True(settings.MainWidthByUser);
        });
    }

    [Fact]
    public void R4_T07_settings_round_trip_keeps_who_chose_the_width_and_old_files_read_as_unknown()
    {
        foreach (var value in new bool?[] { true, false, null })
        {
            var s = UserSettings.InMemory();
            s.MainWidthByUser = value;
            var copy = UserSettings.InMemory();
            copy.Apply(s.ToJson());
            Assert.Equal(value, copy.MainWidthByUser);
        }

        var old = UserSettings.InMemory();
        old.Apply(new System.Text.Json.Nodes.JsonObject { ["MainScale"] = 2.25 }); // v0.2 file: no MainWidthByUser
        Assert.Null(old.MainWidthByUser);
        var bad = UserSettings.InMemory();
        bad.Apply(new System.Text.Json.Nodes.JsonObject { ["MainWidthByUser"] = "yes" });
        Assert.Null(bad.MainWidthByUser);
    }

    // ------------------------------------------------------------------ R4-T08: DPI 100 / 125 / 150 / 200 %

    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(1920, 1080, 1.25)]
    [InlineData(1920, 1080, 1.5)]
    [InlineData(3840, 2160, 2.0)]
    [InlineData(1366, 768, 1.0)]
    [InlineData(1280, 720, 1.5)]   // 853 DIP
    [InlineData(1024, 768, 2.0)]   // 512 DIP: narrower than the Japanese one-line width
    [InlineData(800, 600, 2.0)]    // 400 DIP: narrower than MinWidth
    public void R4_T08_first_start_fits_the_work_area_at_every_scale(int width, int height, double scale)
    {
        foreach (var language in new[] { "ja", "zh-CN", "en" })
        {
            InLanguage(language, () =>
            {
                var monitor = Monitor(width, height, scale);
                using var h = new Harness(MainOpen(), monitor);
                var w = h.Coordinator.MainWindow;
                var work = monitor.WorkArea.Width / scale;
                Assert.True(w.Width <= work + 0.5, $"{language} {width}x{height}@{scale}: {w.Width:N0} <= work area {work:N0}");
                AssertPreferredWidth(Math.Min(w.PreferredWidth(), work), Math.Min(w.Width, work), $"{language} {width}x{height}@{scale}");
                Assert.True(w.MinWidth <= work + 0.5, $"MinWidth {w.MinWidth:N0} <= work area {work:N0}");
                Assert.Empty(HeaderProblems(h, $"{language} {width}x{height}@{scale}"));
            });
        }
    }

    // ------------------------------------------------------------------ R4-T09: multi-monitor / work area

    [Fact]
    public void R4_T09_a_width_saved_on_a_wider_monitor_is_capped_to_the_monitor_it_comes_back_on()
    {
        InLanguage("ja", () =>
        {
            // Saved 1700 DIP wide on a 3840 px monitor at 100 %; only a 900 x 700 monitor is left (below a CI desktop width too).
            var small = Monitor(900, 700, 1.0);
            using var h = new Harness(MainOpen(PixelRect.FromSize(4000, 100, 1700, 900), scale: 1.0, byUser: true), small);
            var w = h.Coordinator.MainWindow;
            Assert.Equal(900, w.Width, 1);
            var pos = h.Placement.GetBounds(h.MainHandle);
            Assert.True(small.WorkArea.Intersect(PixelRect.FromSize(pos.Left, pos.Top, 900, 500)).Width >= 48, $"visible: {pos}");
            Assert.Empty(HeaderProblems(h, "capped"));
        });
    }

    [Fact]
    public void R4_T09_main_window_saved_on_a_disconnected_monitor_comes_back_visible_with_its_width()
    {
        InLanguage("ja", () =>
        {
            // Saved on the 100 % right-hand monitor (600 DIP, user width); it is gone at start.
            using var h = new Harness(MainOpen(PixelRect.FromSize(4200, 200, 600, 700), scale: 1.0, byUser: true), Primary);
            var w = h.Coordinator.MainWindow;
            Assert.Equal(600, w.Width, 1);
            var pos = h.Placement.GetBounds(h.MainHandle);
            Assert.True(pos.Left < Primary.WorkArea.Right - 48 && pos.Top < Primary.WorkArea.Bottom - 48, $"moved onto the primary: {pos}");
        });
    }

    [Fact]
    public void R4_T09_on_the_negative_150_percent_monitor_the_width_and_header_are_correct()
    {
        InLanguage("ja", () =>
        {
            using var h = new Harness(MainOpen(PixelRect.FromSize(-2400, 300, 900, 900), scale: 1.5, byUser: true), Primary, LeftNegative150);
            Assert.Equal(600, h.Coordinator.MainWindow.Width, 1);
            Assert.Empty(HeaderProblems(h, "left monitor"));
        });
    }
}
