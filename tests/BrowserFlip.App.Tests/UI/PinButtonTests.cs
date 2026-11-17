using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using BrowserFlip.App;
using BrowserFlip.App.Desktop;
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
/// The always-on-top toggle: OFF = grey pin tilted 45 degrees on the normal background, ON = upright white pin
/// on a solid accent-blue background. Checked in the main window header and in the compact bar (horizontal,
/// vertical, docked right), Light and Dark, for OFF -> ON -> OFF driven both by the view model and by the user
/// (UI Automation toggle). Windows get an HWND but are never shown or activated.
/// </summary>
public sealed class PinButtonTests
{
    private const double OffAngle = 45;
    private const double OnAngle = 0;

    public enum BarMode
    {
        FloatingHorizontal,
        FloatingVertical,
        DockedRight,
    }

    public static TheoryData<ThemePreference> Themes => new() { ThemePreference.Light, ThemePreference.Dark };

    public static TheoryData<ThemePreference, BarMode> BarCases
    {
        get
        {
            var data = new TheoryData<ThemePreference, BarMode>();
            foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
            {
                foreach (var mode in Enum.GetValues<BarMode>())
                {
                    data.Add(theme, mode);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Main_window_pin_follows_IsTopmost_off_on_off(ThemePreference theme)
    {
        RunThemed(theme, () =>
        {
            var settings = UserSettings.InMemory();
            using var main = CreateMain(settings);
            using var shell = new ShellViewModel(settings);
            var window = new MainWindow { DataContext = main, Shell = shell };
            try
            {
                _ = new WindowInteropHelper(window).EnsureHandle();
                var root = (FrameworkElement)window.Content;
                Layout(root);
                var pin = window.PinToggleButton;
                Layout(root); // the first pass only builds the templates
                var size = new Size(pin.ActualWidth, pin.ActualHeight);
                Assert.True(Math.Abs(size.Width - 32) < 1 && Math.Abs(size.Height - 30) < 1, $"pin size {size}"); // device-pixel rounding

                AssertState(pin, on: false, theme, "main/initial");

                // Driven by the view model (tray menu, bar, settings load).
                shell.IsTopmost = true;
                Layout(root);
                AssertState(pin, on: true, theme, "main/vm on");
                Assert.Equal(size, new Size(pin.ActualWidth, pin.ActualHeight));

                shell.IsTopmost = false;
                Layout(root);
                AssertState(pin, on: false, theme, "main/vm off");

                // Driven by the user (click / Space / UIA Toggle all go through ToggleButton.OnToggle): one flip each.
                Toggle(pin);
                Layout(root);
                Assert.True(shell.IsTopmost);
                AssertState(pin, on: true, theme, "main/user on");

                Toggle(pin);
                Layout(root);
                Assert.False(shell.IsTopmost);
                AssertState(pin, on: false, theme, "main/user off");
                Assert.Equal(size, new Size(pin.ActualWidth, pin.ActualHeight));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [MemberData(nameof(BarCases))]
    public void Compact_bar_pin_follows_IsTopmost_without_changing_the_bar_layout(ThemePreference theme, BarMode mode)
    {
        RunThemed(theme, () =>
        {
            var settings = UserSettings.InMemory();
            using var main = CreateMain(settings);
            using var shell = new ShellViewModel(settings);
            switch (mode)
            {
                case BarMode.FloatingHorizontal:
                    shell.DockEnabled = false;
                    shell.FloatingOrientation = BarOrientation.Horizontal;
                    break;
                case BarMode.FloatingVertical:
                    shell.DockEnabled = false;
                    shell.FloatingOrientation = BarOrientation.Vertical;
                    break;
                default:
                    shell.DockEdge = DockEdge.Right;
                    shell.DockEnabled = true;
                    break;
            }

            var vertical = mode != BarMode.FloatingHorizontal;
            Assert.Equal(vertical, shell.IsVertical);

            var bar = new CompactBarWindow { DataContext = new DesktopContext(main, shell), AnimationsEnabled = false, ShowActivated = false };
            try
            {
                _ = new WindowInteropHelper(bar).EnsureHandle();
                var pin = bar.PinButton;
                var expectedDock = vertical ? Dock.Top : Dock.Left;

                _ = LayoutBar(bar); // the first pass only builds the templates / item containers
                var barSize = LayoutBar(bar);
                Assert.Equal(barSize, LayoutBar(bar)); // the baseline is stable before toggling
                var buttonSize = new Size(pin.ActualWidth, pin.ActualHeight);
                Assert.Equal(new Size(shell.BarButtonSize, shell.BarButtonSize), buttonSize);
                Assert.Equal(expectedDock, DockPanel.GetDock(pin));
                AssertState(pin, on: false, theme, $"bar/{mode}/initial");

                void AssertLayoutUnchanged(string label)
                {
                    var now = LayoutBar(bar);
                    Assert.True(barSize == now, $"{label}: bar size changed {barSize} -> {now}");
                    Assert.True(buttonSize == new Size(pin.ActualWidth, pin.ActualHeight), $"{label}: button size changed");
                    Assert.Equal(expectedDock, DockPanel.GetDock(pin));
                    Assert.Equal(vertical, shell.IsVertical);
                }

                shell.IsTopmost = true;
                AssertLayoutUnchanged("vm on");
                AssertState(pin, on: true, theme, $"bar/{mode}/vm on");

                shell.IsTopmost = false;
                AssertLayoutUnchanged("vm off");
                AssertState(pin, on: false, theme, $"bar/{mode}/vm off");

                Toggle(pin);
                AssertLayoutUnchanged("user on");
                Assert.True(shell.IsTopmost); // exactly one flip: no command + binding double toggle
                AssertState(pin, on: true, theme, $"bar/{mode}/user on");

                Toggle(pin);
                AssertLayoutUnchanged("user off");
                Assert.False(shell.IsTopmost);
                AssertState(pin, on: false, theme, $"bar/{mode}/user off");
            }
            finally
            {
                bar.Close();
            }
        });
    }

    [Fact]
    public void Tooltip_and_accessible_name_describe_the_action_in_every_language()
    {
        RunThemed(ThemePreference.Light, () =>
        {
            var settings = UserSettings.InMemory();
            using var main = CreateMain(settings);
            using var shell = new ShellViewModel(settings);
            var window = new MainWindow { DataContext = main, Shell = shell };
            var bar = new CompactBarWindow { DataContext = new DesktopContext(main, shell), AnimationsEnabled = false, ShowActivated = false };
            try
            {
                _ = new WindowInteropHelper(window).EnsureHandle();
                _ = new WindowInteropHelper(bar).EnsureHandle();
                Layout((FrameworkElement)window.Content);
                _ = LayoutBar(bar);
                var expected = new Dictionary<string, (string Off, string On)>
                {
                    ["en"] = ("Keep on top", "Stop keeping on top"),
                    ["ja"] = ("常に手前に表示する", "常に手前に表示を解除"),
                    ["zh-CN"] = ("置顶显示", "取消置顶"),
                };
                foreach (var (language, texts) in expected)
                {
                    Loc.Instance.SetLanguage(language);
                    foreach (var on in new[] { false, true })
                    {
                        shell.IsTopmost = on;
                        var text = on ? texts.On : texts.Off;
                        foreach (var pin in new[] { window.PinToggleButton, bar.PinButton })
                        {
                            // Stable name (the feature); the on/off state is the UIA toggle state, the tooltip names the action.
                            Assert.Equal(texts.Off, AutomationProperties.GetName(pin));
                            Assert.Equal(text, pin.ToolTip as string);
                            Assert.Equal(texts.Off, new ToggleButtonAutomationPeer(pin).GetName());
                        }
                    }
                }
            }
            finally
            {
                Loc.Instance.SetLanguage("en");
                bar.Close();
                window.Close();
            }
        });
    }

    [Fact]
    public void Pin_is_keyboard_operable_with_a_focus_rectangle()
    {
        RunThemed(ThemePreference.Light, () =>
        {
            var settings = UserSettings.InMemory();
            using var main = CreateMain(settings);
            using var shell = new ShellViewModel(settings);
            var window = new MainWindow { DataContext = main, Shell = shell };
            var bar = new CompactBarWindow { DataContext = new DesktopContext(main, shell), AnimationsEnabled = false, ShowActivated = false };
            try
            {
                _ = new WindowInteropHelper(window).EnsureHandle();
                _ = new WindowInteropHelper(bar).EnsureHandle();
                foreach (var pin in new[] { window.PinToggleButton, bar.PinButton })
                {
                    Assert.True(pin.Focusable);
                    Assert.True(KeyboardNavigation.GetIsTabStop(pin));
                    Assert.Same(pin.FindResource("FocusVisual"), pin.FocusVisualStyle);
                    Assert.Equal(AutomationControlType.Button, new ToggleButtonAutomationPeer(pin).GetAutomationControlType());
                }
            }
            finally
            {
                bar.Close();
                window.Close();
            }
        });
    }

    [Fact]
    public void Pin_icon_is_upright_when_not_rotated()
    {
        // The ON state shows the geometry unrotated: it must be a vertical pin (head on top, needle down).
        var geometry = Geometry.Parse("M5,1 H11 V3 H10 V8 L13,10.5 H3 L6,8 V3 H5 Z M7.25,10.5 H8.75 V15.5 H7.25 Z");
        RunThemed(ThemePreference.Light, () =>
        {
            var pin = new ToggleButton { Style = (Style)Application.Current.FindResource("PinToggle") };
            pin.ApplyTemplate();
            var icon = (Path)pin.Template.FindName("PinIcon", pin);
            var bounds = icon.Data.Bounds;
            Assert.Equal(geometry.Bounds, bounds);
            Assert.True(bounds.Height > bounds.Width * 1.3, $"pin is not vertical: {bounds}");
            Assert.Equal(8, (bounds.Left + bounds.Right) / 2); // centred on the rotation origin
            Assert.Equal(new Point(0.5, 0.5), icon.RenderTransformOrigin);
        });
    }

    [Fact]
    public void Every_theme_defines_the_pin_brushes_and_on_state_keeps_white_readable()
    {
        string[] keys =
        [
            "BfPinOffForegroundBrush", "BfPinOffHoverBorderBrush", "BfPinOnBackgroundBrush", "BfPinOnHoverBrush",
            "BfPinOnPressedBrush", "BfPinOnBorderBrush", "BfPinOnHoverBorderBrush", "BfPinOnForegroundBrush",
        ];
        RunThemed(ThemePreference.Light, () =>
        {
            foreach (var file in new[] { "Light", "Dark", "HighContrast" })
            {
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/BrowserFlip;component/Themes/{file}.xaml", UriKind.Absolute),
                };
                foreach (var key in keys)
                {
                    Assert.True(dictionary.Contains(key), $"{file}: {key} missing");
                }

                Color C(string key) => ((SolidColorBrush)dictionary[key]).Color;
                if (file == "HighContrast")
                {
                    // High Contrast uses the system selection pair, which Windows guarantees to be readable.
                    Assert.Equal(SystemColors.HighlightColor, C("BfPinOnBackgroundBrush"));
                    Assert.Equal(SystemColors.HighlightTextColor, C("BfPinOnForegroundBrush"));
                    Assert.Equal(SystemColors.WindowTextColor, C("BfPinOffForegroundBrush"));
                    continue;
                }

                Assert.Equal(Colors.White, C("BfPinOnForegroundBrush"));
                foreach (var background in new[] { "BfPinOnBackgroundBrush", "BfPinOnHoverBrush", "BfPinOnPressedBrush" })
                {
                    var ratio = Contrast(Colors.White, C(background));
                    Assert.True(ratio >= 4.5, $"{file}: white on {background} is {ratio:N2}:1");
                }

                Assert.True(IsBlue(C("BfPinOnBackgroundBrush")), $"{file}: ON background is not blue");
            }
        });
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertState(ToggleButton pin, bool on, ThemePreference theme, string label)
    {
        pin.ApplyTemplate();
        Assert.True(pin.IsChecked == on, $"{label}: IsChecked = {pin.IsChecked}");

        var peer = new ToggleButtonAutomationPeer(pin);
        Assert.Equal(on ? ToggleState.On : ToggleState.Off, ((IToggleProvider)peer).ToggleState);

        var border = (Border)pin.Template.FindName("Bd", pin);
        var icon = (Path)pin.Template.FindName("PinIcon", pin);
        var angle = Assert.IsType<RotateTransform>(icon.RenderTransform).Angle;
        Assert.True(angle == (on ? OnAngle : OffAngle), $"{label}: icon angle {angle}");

        var fill = Assert.IsType<SolidColorBrush>(icon.Fill).Color;
        var background = Assert.IsType<SolidColorBrush>(border.Background).Color;
        var onBackground = ((SolidColorBrush)pin.FindResource("BfPinOnBackgroundBrush")).Color;
        var accent = ((SolidColorBrush)pin.FindResource("BfAccentBrush")).Color;

        if (on)
        {
            Assert.True(fill == Colors.White, $"{label}: icon {fill} is not white");
            Assert.True(background == onBackground, $"{label}: background {background} is not the ON blue {onBackground}");
            Assert.True(IsBlue(background), $"{label}: background {background} is not blue");
            Assert.True(Contrast(fill, background) >= 4.5, $"{label}: icon contrast {Contrast(fill, background):N2}:1");
            if (theme == ThemePreference.Light)
            {
                Assert.True(background == accent, $"{label}: Light ON background {background} is not the accent {accent}");
            }
        }
        else
        {
            Assert.True(fill != Colors.White && fill.R == fill.G && fill.G == fill.B, $"{label}: icon {fill} is not grey");
            Assert.True(fill == ((SolidColorBrush)pin.FindResource("BfPinOffForegroundBrush")).Color, $"{label}: icon {fill}");
            Assert.True(background != onBackground && background != accent, $"{label}: OFF background {background} looks selected");
            Assert.False(IsBlue(background), $"{label}: OFF background {background} is blue");
        }
    }

    private static void Toggle(ToggleButton pin) => ((IToggleProvider)new ToggleButtonAutomationPeer(pin)).Toggle();

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(520 - 16, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
    }

    private static Size LayoutBar(CompactBarWindow bar)
    {
        var size = bar.MeasureExpanded(2000);
        bar.BarRoot.Arrange(new Rect(size));
        bar.BarRoot.UpdateLayout();
        return size;
    }

    private static bool IsBlue(Color c) => c.A == 255 && c.B > c.R + 60 && c.B > c.G + 30;

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte v)
        {
            var c = v / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static void RunThemed(ThemePreference theme, Action body)
    {
        Sta.Run(() =>
        {
            // Never on a thread that pumps its dispatcher (that would run the real App.OnStartup).
            DesktopCoordinatorTests.EnsureApplication();
            try
            {
                Loc.Instance.SetLanguage("en");
                UseTheme(theme);
                body();
            }
            finally
            {
                UseTheme(ThemePreference.Light);
                ThemeManager.Shutdown();
            }
        });
    }

    /// <summary>
    /// Swaps the app's theme dictionary the same way <see cref="ThemeManager"/> does. ThemeManager.SetPreference
    /// itself cannot be used here: it touches <see cref="Application.Windows"/>, which belongs to the throw-away
    /// thread that created the application, and it is a no-op once another test has called Shutdown.
    /// </summary>
    private static void UseTheme(ThemePreference theme)
    {
        var file = theme == ThemePreference.Dark ? "Dark" : "Light";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.First(d => d.Source?.OriginalString.Contains("/BrowserFlip;component/Themes/", StringComparison.Ordinal) == true);
        dictionaries[dictionaries.IndexOf(existing)] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/BrowserFlip;component/Themes/{file}.xaml", UriKind.Absolute),
        };
    }

    private static MainViewModel CreateMain(UserSettings settings)
    {
        var reg = new FakeRegistry();
        var fs = new FakeFileSystem();
        foreach (var (id, exe, progId) in new[]
        {
            ("Google Chrome", @"C:\C\chrome.exe", "ChromeHTML"),
            ("Microsoft Edge", @"C:\E\msedge.exe", "MSEdgeHTM"),
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
