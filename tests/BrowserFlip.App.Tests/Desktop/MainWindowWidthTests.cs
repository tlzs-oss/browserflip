using System.Runtime.InteropServices;
using System.Windows.Interop;
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

    /// <summary>
    /// Header problems in the window's own layout (the shown, off-screen window at the real frame and DPI, no manual
    /// layout) and, independently, when laid out at the window width.
    /// </summary>
    private static List<string> HeaderProblems(Harness h, string label)
    {
        var w = h.Coordinator.MainWindow;
        Harness.Pump();
        var problems = MainWindowHeaderTests.Problems(MainWindowHeaderTests.ReadHeader(w), $"{label} (window layout)");
        problems.AddRange(MainWindowHeaderTests.Problems(MainWindowHeaderTests.LayoutHeader(w, w.Width), label));
        w.InvalidateMeasure();
        Harness.Pump();
        return problems;
    }

    /// <summary>Layout mode of the header in the window's own layout pass.</summary>
    private static HeaderLayoutMode RealMode(Harness h)
    {
        Harness.Pump();
        return MainWindowHeaderTests.ReadHeader(h.Coordinator.MainWindow).Mode;
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
            Assert.Equal(HeaderLayoutMode.SingleLine, RealMode(h)); // independent of PreferredWidth: the shown window
            AssertPreferredWidth(w.PreferredWidth(), w.Width, language);
            Assert.True(w.WidthIsAutomatic);
            Assert.Empty(HeaderProblems(h, language));
        });
    }

    // ------------------------------------------------------------------ R4-T04: language switch

    [Fact]
    public void R4_T04_an_app_chosen_width_follows_the_header_of_the_new_language()
    {
        InLanguage("en", () =>
        {
            using var h = new Harness(MainOpen(), Primary);
            var w = h.Coordinator.MainWindow;
            var en = w.Width;
            Loc.Instance.SetLanguage("ja");
            Harness.Pump();
            AssertPreferredWidth(w.PreferredWidth(), w.Width, "en -> ja");
            Assert.True(w.Width > en, $"ja {w.Width:N0} > en {en:N0}");
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

    [Fact]
    public void R4_T06_a_wider_width_saved_before_BF07_is_the_users_and_survives_later_starts_and_language_changes()
    {
        // Review H1: v0.2 always opened at 520 DIP, so a wider saved width was chosen by the user.
        InLanguage("en", () =>
        {
            var settings = MainOpen(PixelRect.FromSize(800, 400, 1800, 1500), byUser: null); // 800 DIP
            using (var h = new Harness(settings, Primary))
            {
                var w = h.Coordinator.MainWindow;
                Assert.False(w.WidthIsAutomatic);
                Loc.Instance.SetLanguage("ja");
                Harness.Pump();
                Assert.Equal(800, w.Width, 1);
                h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(800, 400, 1800, 1500));
            }

            Assert.True(settings.MainWidthByUser);
            using var again = new Harness(settings, Primary);
            Assert.Equal(800, again.Coordinator.MainWindow.Width, 1);
        });
    }

    [Fact]
    public void R4_T06_the_old_fixed_default_width_saved_before_BF07_stays_app_chosen()
    {
        InLanguage("en", () =>
        {
            using var h = new Harness(MainOpen(PixelRect.FromSize(800, 400, 1170, 1500), byUser: null), Primary); // 520 DIP
            Assert.True(h.Coordinator.MainWindow.WidthIsAutomatic);
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

    // ------------------------------------------------------------------ user resize (window messages to the own, off-screen HWND)

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmSizing = 0x0214;

    /// <summary>
    /// The message sequence of a border drag (WM_ENTERSIZEMOVE, WM_SIZING, resize, WM_EXITSIZEMOVE), sent to the test's own
    /// off-screen window. These are window messages to that window only: no mouse or keyboard input is generated.
    /// </summary>
    private static void SizeMoveLoop(Harness h, int widthDelta, int heightDelta, bool sizing, Action? during = null)
    {
        var hwnd = h.MainHandle;
        _ = SendMessageW(hwnd, WmEnterSizeMove, IntPtr.Zero, IntPtr.Zero);
        Assert.True(GetWindowRect(hwnd, out var r));
        if (sizing)
        {
            var rect = Marshal.AllocHGlobal(Marshal.SizeOf<Rect32>());
            try
            {
                Marshal.StructureToPtr(r with { Right = r.Right + widthDelta, Bottom = r.Bottom + heightDelta }, rect, false);
                _ = SendMessageW(hwnd, WmSizing, (IntPtr)8 /* WMSZ_BOTTOMRIGHT */, rect);
            }
            finally
            {
                Marshal.FreeHGlobal(rect);
            }

            const uint NoMoveNoZOrderNoActivate = 0x0002 | 0x0004 | 0x0010;
            Assert.True(SetWindowPos(hwnd, IntPtr.Zero, 0, 0, r.Right - r.Left + widthDelta, r.Bottom - r.Top + heightDelta, NoMoveNoZOrderNoActivate));
        }

        during?.Invoke();
        _ = SendMessageW(hwnd, WmExitSizeMove, IntPtr.Zero, IntPtr.Zero);
        Harness.Pump();
    }

    [Theory]
    [InlineData(120, 0, true, false)]   // border drag that changes the width: the user's width from now on
    [InlineData(0, 120, true, true)]    // top / bottom border only (the height is not kept): still app-chosen
    [InlineData(0, 0, false, true)]     // size-move loop without resizing: still app-chosen
    public void Only_a_width_change_by_the_user_makes_the_width_theirs(int widthDelta, int heightDelta, bool sizing, bool stillAutomatic)
    {
        InLanguage("ja", () =>
        {
            var settings = MainOpen();
            using var h = new Harness(settings, Primary);
            var w = h.Coordinator.MainWindow;
            Assert.True(w.WidthIsAutomatic);
            SizeMoveLoop(h, widthDelta, heightDelta, sizing);
            Assert.Equal(stillAutomatic, w.WidthIsAutomatic);
            h.Shell.EnterCompactCommand.Execute(null); // hides the main window: saves its bounds
            Harness.Pump();
            Assert.Equal(!stillAutomatic, settings.MainWidthByUser);
        });
    }

    [Fact]
    public void The_width_cap_waits_for_the_end_of_a_drag_and_a_capped_user_width_is_saved_uncapped()
    {
        InLanguage("ja", () =>
        {
            var narrow = new MonitorInfo(@"\\.\DISPLAY2", new PixelRect(3840, 0, 4640, 1280), new PixelRect(3840, 0, 4640, 1240), 1.0, false); // 800 DIP
            var settings = MainOpen(PixelRect.FromSize(400, 300, 2025, 1500), byUser: true); // 900 DIP
            using var h = new Harness(settings, Primary, narrow);
            var w = h.Coordinator.MainWindow;
            SizeMoveLoop(h, 0, 0, sizing: false, during: () =>
            {
                h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(3850, 100, 900, 800)); // dragged onto the narrow monitor
                w.LimitHeightToWorkArea(); // LocationChanged inside the move loop
                Assert.Equal(900, w.Width, 1); // no resize inside the move loop
            });
            Assert.Equal(800, w.Width, 1); // capped once at the end of the drag
            Assert.Equal(900, w.WantedWidth, 1);

            h.Shell.EnterCompactCommand.Execute(null); // saves the bounds while capped
            Harness.Pump();
            Assert.True(settings.MainWidthByUser);
            Assert.Equal(900, settings.MainBounds!.Value.Width / settings.MainScale, 1);
        });
    }

    // ------------------------------------------------------------------ review M1 / M2 / L2

    [Fact]
    public void A_width_capped_on_a_narrow_monitor_comes_back_on_a_wide_one()
    {
        InLanguage("ja", () =>
        {
            var narrow = new MonitorInfo(@"\\.\DISPLAY2", new PixelRect(3840, 0, 4640, 1280), new PixelRect(3840, 0, 4640, 1240), 1.0, false); // 800 DIP
            using var h = new Harness(MainOpen(PixelRect.FromSize(400, 300, 2025, 1500), byUser: true), Primary, narrow); // 900 DIP
            var w = h.Coordinator.MainWindow;
            Assert.Equal(900, w.Width, 1);

            h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(3850, 100, 900, 800)); // moved onto the narrow monitor
            w.LimitHeightToWorkArea();
            Assert.Equal(800, w.Width, 1);
            Assert.Empty(HeaderProblems(h, "capped on the narrow monitor"));

            h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(400, 300, 1800, 1500)); // back on the wide one
            w.LimitHeightToWorkArea();
            Assert.Equal(900, w.Width, 1);
        });
    }

    [Fact]
    public void Widening_for_a_new_language_keeps_the_tools_inside_the_work_area()
    {
        InLanguage("en", () =>
        {
            using var h = new Harness(MainOpen(), Primary);
            var w = h.Coordinator.MainWindow;
            var px = (int)Math.Round(w.Width * 2.25);
            h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(Primary.WorkArea.Right - px, 400, px, 1500)); // against the right edge
            Loc.Instance.SetLanguage("ja");
            Harness.Pump();
            var b = h.Placement.GetBounds(h.MainHandle);
            var width = (int)Math.Round(w.Width * 2.25);
            Assert.True(width > px, $"ja is wider: {width} > {px}");
            Assert.True(b.Left + width <= Primary.WorkArea.Right, $"right edge {b.Left + width} <= {Primary.WorkArea.Right}");
        });
    }

    [Fact]
    public void A_language_change_while_the_main_window_is_hidden_is_applied_when_it_is_shown()
    {
        InLanguage("en", () =>
        {
            using var h = new Harness(MainOpen(), Primary);
            var w = h.Coordinator.MainWindow;
            var en = w.Width;
            h.Shell.EnterCompactCommand.Execute(null);
            Harness.Pump();
            Assert.False(w.IsVisible);
            Loc.Instance.SetLanguage("ja");
            Harness.Pump();
            h.Shell.ShowNormalCommand.Execute(null);
            Harness.Pump();
            Assert.True(w.IsVisible);
            Assert.True(w.Width > en, $"ja {w.Width:N0} > en {en:N0}");
            Assert.Equal(HeaderLayoutMode.SingleLine, RealMode(h));
        });
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
