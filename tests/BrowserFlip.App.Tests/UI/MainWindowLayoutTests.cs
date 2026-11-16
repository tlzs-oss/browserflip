using System.Windows;
using System.Windows.Controls;
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
/// Lays out the real main window off-screen (no window is shown) and checks the compact design:
/// size close to 520 x 380 DIP, nothing clipped with an ellipsis, in every language and theme.
/// </summary>
public sealed class MainWindowLayoutTests
{
    private const double Width = 520;

    [Fact]
    public void Main_window_is_compact_and_nothing_is_trimmed_in_any_language_or_theme()
    {
        var failures = new List<string>();
        Sta.Run(() =>
        {
            EnsureApplication();
            foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
            {
                ThemeManager.SetPreference(theme);
                foreach (var language in Loc.SupportedCultures)
                {
                    Loc.Instance.SetLanguage(language);
                    foreach (var expanded in new[] { false, true })
                    {
                        var (height, trimmed) = Layout(expanded);
                        var label = $"{language}/{theme}/{(expanded ? "options" : "normal")}";
                        if (!expanded && height is < 260 or > 380)
                        {
                            failures.Add($"{label}: content height {height:N0} DIP outside 260-380");
                        }

                        if (expanded && height > 760)
                        {
                            failures.Add($"{label}: expanded height {height:N0} DIP too tall");
                        }

                        failures.AddRange(trimmed.Select(t => $"{label}: trimmed text '{t}'"));
                    }
                }
            }

            Loc.Instance.SetLanguage("en");
        });

        Assert.Empty(failures);
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

    private static (double Height, List<string> Trimmed) Layout(bool optionsExpanded)
    {
        var reg = new FakeRegistry();
        var fs = new FakeFileSystem();
        foreach (var (id, exe, prog) in new[]
                 {
                     ("Brave", @"C:\B\brave.exe", "BraveHTML"),
                     ("Google Chrome", @"C:\C\chrome.exe", "ChromeHTML"),
                     ("Microsoft Edge", @"C:\E\msedge.exe", "MSEdgeHTM"),
                     ("Mozilla Firefox", @"C:\F\firefox.exe", "FirefoxURL"),
                 })
        {
            reg.AddBrowser(LocalMachine, Registry64, id, id, $"\"{exe}\"", prog, prog, id);
            fs.Files.Add(exe);
        }

        var assoc = new FakeAssociations().With("http", "MSEdgeHTM").With("https", "MSEdgeHTM");
        var policy = new PolicyInspector(reg);
        var settings = UserSettings.InMemory();
        using var main = new MainViewModel(new BrowserDetector(reg, fs, new FakeStrings()), new Defaults.DefaultBrowserService(assoc),
            new SettingsLauncher(new FakeLauncher(), new FakeOs(26100), policy), policy, _ => null, settings, startTimer: false);
        main.Refresh();
        using var shell = new ShellViewModel(settings) { IsOptionsOpen = optionsExpanded };

        var window = new MainWindow { DataContext = main, Shell = shell };
        try
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(Width - 16, double.PositiveInfinity)); // window frame ~16 DIP
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();

            var trimmed = new List<string>();
            foreach (var tb in Descendants<TextBlock>(root).Where(t => t.TextTrimming != TextTrimming.None && t.IsVisible && !string.IsNullOrEmpty(t.Text)))
            {
                var available = tb.ActualWidth;
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (tb.DesiredSize.Width > available + 0.5)
                {
                    trimmed.Add(tb.Text);
                }
            }

            return (root.DesiredSize.Height, trimmed);
        }
        finally
        {
            window.Close();
        }
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
