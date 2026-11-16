using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BrowserFlip.App;
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
/// Lays out the real main window off-screen and checks the compact design: about 520 DIP wide, height in
/// range, and no text cut off with an ellipsis, in every language x theme x options state. The window gets
/// an HWND (never shown) so the Fluent styles used by the real app are applied. Runs at the test machine's
/// DPI; 150 % / 225 % are covered by the real-device screenshots in the test records.
/// </summary>
public sealed class MainWindowLayoutTests
{
    private const double ContentWidth = 520 - 16; // window frame ~16 DIP

    [Fact]
    public void Main_window_is_compact_and_nothing_is_trimmed_in_any_language_or_theme()
    {
        var failures = new List<string>();
        Sta.Run(() =>
        {
            EnsureApplication();
            try
            {
                foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
                {
                    ThemeManager.SetPreference(theme);
                    foreach (var language in Loc.SupportedCultures)
                    {
                        Loc.Instance.SetLanguage(language);
                        foreach (var expanded in new[] { false, true })
                        {
                            var label = $"{language}/{theme}/{(expanded ? "options" : "normal")}";

                            // Typical machine: 4 healthy browsers — size must stay compact.
                            var (height, trimmed) = Layout(expanded, edgeCases: false);
                            if (!expanded && height is < 260 or > 380)
                            {
                                failures.Add($"{label}: content height {height:N0} DIP outside 260-380");
                            }

                            if (expanded && height > 760)
                            {
                                failures.Add($"{label}: expanded height {height:N0} DIP too tall");
                            }

                            failures.AddRange(trimmed.Select(t => $"{label}: trimmed '{t}'"));

                            // Edge cases: uninstalled browser + browser missing from RegisteredApplications.
                            var (_, trimmedEdge) = Layout(expanded, edgeCases: true);
                            failures.AddRange(trimmedEdge.Select(t => $"{label}/edge: trimmed '{t}'"));
                        }
                    }
                }
            }
            finally
            {
                Loc.Instance.SetLanguage("en");
                ThemeManager.SetPreference(ThemePreference.Light);
                ThemeManager.Shutdown();
            }
        });

        Assert.Empty(failures);
    }

    [Fact]
    public void Uninstalled_default_browser_still_shows_not_found()
    {
        List<string> shown = [];
        Sta.Run(() =>
        {
            EnsureApplication();
            try
            {
                Loc.Instance.SetLanguage("en");
                // Vivaldi is still the registered default but its executable is gone.
                _ = LayoutCore(optionsExpanded: false, edgeCases: true, defaultProgId: "VivaldiHTM", out shown);
            }
            finally
            {
                ThemeManager.Shutdown();
            }
        });

        Assert.Contains("Not found", shown);
        Assert.DoesNotContain("Default", shown); // the badge must not overlap the hint
    }

    [Fact]
    public void Trim_detection_actually_detects_trimming()
    {
        // Guard against a test that silently checks nothing (e.g. filtering on IsVisible of an unshown window).
        var detected = false;
        Sta.Run(() =>
        {
            var tb = new TextBlock { Text = new string('W', 200), TextTrimming = TextTrimming.CharacterEllipsis };
            var host = new Border { Width = 50, Child = tb };
            host.Measure(new Size(50, 100));
            host.Arrange(new Rect(0, 0, 50, 100));
            detected = IsTrimmed(tb);
        });
        Assert.True(detected);
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            var app = new BrowserFlip.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            ThemeManager.Initialize(app, ThemePreference.Light);
        }
    }

    private static (double Height, List<string> Trimmed) Layout(bool optionsExpanded, bool edgeCases)
        => LayoutCore(optionsExpanded, edgeCases, defaultProgId: "MSEdgeHTM", out _);

    private static (double Height, List<string> Trimmed) LayoutCore(bool optionsExpanded, bool edgeCases, string defaultProgId, out List<string> shownTexts)
    {
        var reg = new FakeRegistry();
        var fs = new FakeFileSystem();
        var browsers = new List<(string Id, string Exe, string ProgId, bool Installed, bool Registered)>
        {
            ("Brave", @"C:\B\brave.exe", "BraveHTML", true, true),
            ("Google Chrome", @"C:\C\chrome.exe", "ChromeHTML", true, true),
            ("Microsoft Edge", @"C:\E\msedge.exe", "MSEdgeHTM", true, true),
            ("Mozilla Firefox", @"C:\F\firefox.exe", "FirefoxURL", true, true),
        };
        if (edgeCases)
        {
            browsers.Add(("Vivaldi", @"C:\V\vivaldi.exe", "VivaldiHTM", false, true));   // uninstalled
            browsers.Add(("Opera", @"C:\O\opera.exe", "OperaStable", true, false));      // not in RegisteredApplications
        }

        foreach (var b in browsers)
        {
            reg.AddBrowser(LocalMachine, Registry64, b.Id, b.Id, $"\"{b.Exe}\"", b.ProgId, b.ProgId, b.Registered ? b.Id : null);
            if (b.Installed)
            {
                fs.Files.Add(b.Exe);
            }
        }

        var assoc = new FakeAssociations().With("http", defaultProgId).With("https", defaultProgId);
        var policy = new PolicyInspector(reg);
        var settings = UserSettings.InMemory();
        using var main = new MainViewModel(new BrowserDetector(reg, fs, new FakeStrings()), new Defaults.DefaultBrowserService(assoc),
            new SettingsLauncher(new FakeLauncher(), new FakeOs(26100), policy), policy, _ => null, settings, startTimer: false);
        main.Refresh();
        using var shell = new ShellViewModel(settings) { IsOptionsOpen = optionsExpanded };

        var window = new MainWindow { DataContext = main, Shell = shell };
        try
        {
            _ = new WindowInteropHelper(window).EnsureHandle(); // applies the Fluent theme like the real app (window stays hidden)
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(ContentWidth, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();

            shownTexts = Descendants<TextBlock>(root).Where(IsShown).Select(t => t.Text).ToList();
            var candidates = Descendants<TextBlock>(root)
                .Where(t => t.TextTrimming != TextTrimming.None && IsShown(t))
                .ToList();
            var trimmed = candidates.Where(IsTrimmed).Select(t => t.Text).ToList();
            if (candidates.Count < 8)
            {
                trimmed.Add($"<only {candidates.Count} trimmable text blocks were checked — test is not effective>");
            }

            return (root.DesiredSize.Height, trimmed);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Visible in the layout (an unshown window has IsVisible = false everywhere, so do not use it).</summary>
    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? d = element; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return element.ActualWidth > 0 && !string.IsNullOrEmpty((element as TextBlock)?.Text);
    }

    private static bool IsTrimmed(TextBlock tb)
    {
        var available = tb.ActualWidth;
        var probe = new TextBlock
        {
            Text = tb.Text,
            FontFamily = tb.FontFamily,
            FontSize = tb.FontSize,
            FontWeight = tb.FontWeight,
            FontStyle = tb.FontStyle,
            FontStretch = tb.FontStretch,
        };
        TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(tb));
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width > available + 0.5;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
