using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using BrowserFlip.App;
using BrowserFlip.App.Controls;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Settings;
using BrowserFlip.Core.Tests.Desktop;
using static BrowserFlip.Core.Platform.RegistryRoot;
using static BrowserFlip.Core.Platform.RegistryViewKind;

namespace BrowserFlip.Core.Tests.UI;

/// <summary>
/// BF-07 (R4-T01..T05): the main window header (icon, name, tagline | language, pin, compact, refresh) never
/// overlaps, clips or pushes a control out, in every language, language choice and theme, at the measured default
/// width and at every width down to the minimum. The real window gets an HWND (never shown) so the real styles apply.
/// </summary>
public sealed class MainWindowHeaderTests
{
    private const double FrameDip = 16; // window frame (left + right) at 100 %: content width = window width - frame

    /// <summary>UI language and language choice ("" = follow Windows: the longest choice text, e.g. "システムに合わせる").</summary>
    public static TheoryData<string, string> LanguagesAndChoices => new()
    {
        { "ja", "" }, { "zh-CN", "" }, { "en", "" },
        { "ja", "ja" }, { "zh-CN", "zh-CN" }, { "en", "en" },
    };

    internal sealed record HeaderLayout(
        HeaderLayoutMode Mode, double HeaderWidth, Rect Title, Rect Tagline, Rect Tools, Rect Combo, Rect[] Buttons,
        double ComboTextNeeded, string[] Clipped);

    // ------------------------------------------------------------------ R4-T01..T03: first start, every language

    [Theory]
    [MemberData(nameof(LanguagesAndChoices))]
    public void R4_T01_T03_header_is_on_one_line_without_overlap_at_the_default_width(string uiLanguage, string choice)
    {
        var problems = new List<string>();
        WithWindow(uiLanguage, choice, window =>
        {
            var width = window.PreferredWidth();
            var l = LayoutHeader(window, width);
            problems.AddRange(Problems(l, $"{uiLanguage}/'{choice}' default {width:N0}"));
            if (l.Mode != HeaderLayoutMode.SingleLine)
            {
                problems.Add($"{uiLanguage}/'{choice}': default width {width:N0} gives {l.Mode}, expected one line");
            }

            if (width < MainWindow.DefaultWidth)
            {
                problems.Add($"{uiLanguage}/'{choice}': default width {width:N0} below {MainWindow.DefaultWidth}");
            }
        });
        Assert.Empty(problems);
    }

    [Fact]
    public void R4_T01_japanese_needs_more_than_the_old_fixed_width_and_gets_it()
    {
        // The v0.2 window was 520 DIP whatever the language; Japanese with "システムに合わせる" needs more (BF-07).
        double ja = 0, en = 0;
        WithWindow("ja", "", w => ja = w.PreferredWidth());
        WithWindow("en", "en", w => en = w.PreferredWidth());
        Assert.True(ja > MainWindow.DefaultWidth, $"ja default {ja:N0}");
        Assert.True(ja > en, $"ja {ja:N0} > en {en:N0}: the width follows the measured header");
        Assert.True(ja < 640, $"ja default {ja:N0} stays a compact utility window");
    }

    // ------------------------------------------------------------------ R4-T05: minimum width and below

    [Theory]
    [MemberData(nameof(LanguagesAndChoices))]
    public void R4_T05_header_degrades_without_overlap_at_every_width_down_to_a_narrow_work_area(string uiLanguage, string choice)
    {
        var problems = new List<string>();
        var modes = new HashSet<HeaderLayoutMode>();
        WithWindow(uiLanguage, choice, window =>
        {
            for (var width = 760.0; width >= 340; width -= 10)
            {
                var l = LayoutHeader(window, width);
                modes.Add(l.Mode);
                problems.AddRange(Problems(l, $"{uiLanguage}/'{choice}' {width:N0}"));
            }

            var atMin = LayoutHeader(window, window.MinWidth);
            problems.AddRange(Problems(atMin, $"{uiLanguage}/'{choice}' MinWidth {window.MinWidth:N0}"));
        });
        Assert.Empty(problems);
        Assert.Contains(HeaderLayoutMode.SingleLine, modes);
        Assert.Contains(HeaderLayoutMode.TwoRows, modes); // the last step exists and is reached on narrow screens
    }

    [Fact]
    public void R4_T05_degradation_order_is_one_line_then_wrapped_tagline_then_two_rows()
    {
        var sequence = new List<HeaderLayoutMode>();
        WithWindow("ja", "", window =>
        {
            for (var width = 760.0; width >= 340; width -= 5)
            {
                var mode = LayoutHeader(window, width).Mode;
                if (sequence.Count == 0 || sequence[^1] != mode)
                {
                    sequence.Add(mode);
                }
            }
        });
        Assert.Equal([HeaderLayoutMode.SingleLine, HeaderLayoutMode.Wrapped, HeaderLayoutMode.TwoRows], sequence);
    }

    // ------------------------------------------------------------------ R4-T04: language switch on a live window

    [Theory]
    [InlineData(520)]
    [InlineData(460)]
    [InlineData(380)]
    public void R4_T04_switching_language_lays_the_header_out_again(double width)
    {
        var problems = new List<string>();
        WithWindow("en", "", window =>
        {
            foreach (var language in new[] { "ja", "zh-CN", "en", "ja" })
            {
                Loc.Instance.SetLanguage(language); // same window, same width: no re-creation
                problems.AddRange(Problems(LayoutHeader(window, width), $"switch to {language} at {width}"));
            }
        });
        Assert.Empty(problems);
    }

    // ------------------------------------------------------------------ Light / Dark

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public void Header_has_no_overlap_in_every_theme(string theme)
    {
        // The theme dictionary is merged into the window (the shared Application lives on another thread).
        var problems = new List<string>();
        foreach (var language in Loc.SupportedCultures)
        {
            WithWindow(language, string.Empty, window =>
            {
                window.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/BrowserFlip;component/Themes/{theme}.xaml", UriKind.Absolute),
                });
                foreach (var width in new[] { window.PreferredWidth(), 520, 460, 380 })
                {
                    problems.AddRange(Problems(LayoutHeader(window, width), $"{theme}/{language} {width:N0}"));
                }
            });
        }

        Assert.Empty(problems);
    }

    // ------------------------------------------------------------------ helpers

    internal static void WithWindow(string uiLanguage, string choice, Action<MainWindow> body)
    {
        Sta.Run(() =>
        {
            DesktopCoordinatorTests.EnsureApplication();
            var (window, main, shell) = CreateWindow(uiLanguage, choice);
            try
            {
                body(window);
            }
            finally
            {
                window.Close();
                shell.Dispose();
                main.Dispose();
                Loc.Instance.SetLanguage("en");
            }
        });
    }

    internal static HeaderLayout LayoutHeader(MainWindow window, double windowWidth)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(windowWidth - FrameDip, double.PositiveInfinity));
        root.Arrange(new Rect(new Size(windowWidth - FrameDip, root.DesiredSize.Height)));
        root.UpdateLayout();
        return ReadHeader(window);
    }

    /// <summary>Reads the header geometry of a window that has been laid out.</summary>
    internal static HeaderLayout ReadHeader(MainWindow window)
    {
        var header = (HeaderPanel)window.FindName("Header");
        var brand = (FrameworkElement)window.FindName("HeaderBrand");
        var tools = (FrameworkElement)window.FindName("HeaderTools");
        var title = (TextBlock)window.FindName("HeaderTitle");
        var tagline = (TextBlock)window.FindName("HeaderTagline");
        var combo = Descendants<ComboBox>(tools).First();
        var buttons = tools.Children().OfType<ButtonBase>().ToList();

        var clipped = new List<string>();
        foreach (var e in new FrameworkElement[] { brand, tools, title, tagline, combo })
        {
            // A child arranged smaller than it asked for is clipped by WPF (the v0.2 tagline under the language box).
            if (e.ActualWidth + 0.5 < e.DesiredSize.Width - e.Margin.Left - e.Margin.Right
                || e.ActualHeight + 0.5 < e.DesiredSize.Height - e.Margin.Top - e.Margin.Bottom)
            {
                clipped.Add($"{e.Name}{(e is TextBlock t ? $" '{t.Text}'" : string.Empty)} {e.ActualWidth:N1}x{e.ActualHeight:N1} < {e.DesiredSize}");
            }
        }

        if (TextWidth(tagline, tagline.Text) > Bounds(tagline, header).Width + 0.5 && LineCount(tagline) < 2)
        {
            clipped.Add($"tagline '{tagline.Text}' wider than its space and not wrapped");
        }

        var selected = (combo.SelectedItem as LanguageOption)?.DisplayName ?? string.Empty;
        return new HeaderLayout(
            header.Mode, header.ActualWidth, Bounds(title, header), Bounds(tagline, header), Bounds(tools, header), Bounds(combo, header),
            buttons.Select(b => Bounds(b, header)).ToArray(), TextWidth(combo, selected), clipped.ToArray());
    }

    internal static (MainWindow Window, MainViewModel Main, ShellViewModel Shell) CreateWindow(string uiLanguage, string settingsLanguage)
    {
        var reg = new FakeRegistry();
        var fs = new FakeFileSystem();
        reg.AddBrowser(LocalMachine, Registry64, "Microsoft Edge", "Microsoft Edge", "\"C:\\E\\msedge.exe\"", "MSEdgeHTM", "MSEdgeHTM", "Microsoft Edge");
        fs.Files.Add(@"C:\E\msedge.exe");
        var assoc = new FakeAssociations().With("http", "MSEdgeHTM").With("https", "MSEdgeHTM");
        var policy = new PolicyInspector(reg);
        var settings = UserSettings.InMemory();
        settings.Language = settingsLanguage;
        Loc.Instance.SetLanguage(uiLanguage);
        var main = new MainViewModel(new BrowserDetector(reg, fs, new FakeStrings()), new Defaults.DefaultBrowserService(assoc),
            new SettingsLauncher(new FakeLauncher(), new FakeOs(26100), policy), policy, _ => null, settings, startTimer: false);
        main.Refresh();
        var shell = new ShellViewModel(settings);
        var window = new MainWindow { DataContext = main, Shell = shell };
        _ = new WindowInteropHelper(window).EnsureHandle();
        return (window, main, shell);
    }

    internal static List<string> Problems(HeaderLayout l, string label)
    {
        var problems = new List<string>();
        foreach (var (name, r) in new[] { ("name", l.Title), ("tagline", l.Tagline) })
        {
            if (r.IntersectsWith(l.Tools) && Rect.Intersect(r, l.Tools) is { Width: > 0.5, Height: > 0.5 })
            {
                problems.Add($"{label}: {name} {r} overlaps the controls {l.Tools} ({l.Mode})");
            }

            if (r.Left < -0.5 || r.Right > l.HeaderWidth + 0.5)
            {
                problems.Add($"{label}: {name} {r} outside the header (0-{l.HeaderWidth:N0})");
            }
        }

        if (l.Tools.Left < -0.5 || l.Tools.Right > l.HeaderWidth + 0.5)
        {
            problems.Add($"{label}: controls {l.Tools} outside the header (0-{l.HeaderWidth:N0}, {l.Mode})");
        }

        if (l.Buttons.Length != 3 || l.Buttons.Any(b => b.Width < 30 || b.Height < 28 || b.Left < l.Tools.Left - 0.5 || b.Right > l.Tools.Right + 0.5))
        {
            problems.Add($"{label}: pin / compact / refresh not all shown at full size: {string.Join(", ", l.Buttons)}");
        }

        if (l.Combo.Width < l.ComboTextNeeded + 30)
        {
            problems.Add($"{label}: language box {l.Combo.Width:N0} wide is too narrow for its text ({l.ComboTextNeeded:N0} + arrow)");
        }

        problems.AddRange(l.Clipped.Select(c => $"{label}: clipped {c} ({l.Mode})"));
        return problems;
    }

    internal static Rect Bounds(FrameworkElement e, Visual ancestor)
        => e.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    internal static IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
            {
                yield return t;
            }

            foreach (var d in Descendants<T>(child))
            {
                yield return d;
            }
        }
    }

    private static double TextWidth(Control c, string text) => Formatted(text, c.FontFamily, c.FontStyle, c.FontWeight, c.FontStretch, c.FontSize).WidthIncludingTrailingWhitespace;

    private static double TextWidth(TextBlock t, string text) => Formatted(text, t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch, t.FontSize).WidthIncludingTrailingWhitespace;

    private static int LineCount(TextBlock t)
    {
        var one = Formatted("Ag", t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch, t.FontSize).Height;
        return (int)Math.Round(t.ActualHeight / one);
    }

    private static FormattedText Formatted(string text, FontFamily family, FontStyle style, FontWeight weight, FontStretch stretch, double size)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(family, style, weight, stretch), size, Brushes.Black, 1.0);
}

internal static class PanelExtensions
{
    public static IEnumerable<UIElement> Children(this FrameworkElement e) => e is Panel p ? p.Children.Cast<UIElement>() : [];
}
