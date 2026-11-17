using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrowserFlip.App;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;
using BrowserFlip.App.Views;
using static BrowserFlip.Core.Tests.Desktop.DesktopCoordinatorTests;

namespace BrowserFlip.Core.Tests.Desktop;

/// <summary>
/// v0.3 multi-view main window: the view preference (persistence, fallbacks), immediate switching with one shared
/// core, per-view sizes at a shared position inside the work area, the menu bar, the single Settings / About
/// windows, Tray Mode and the tray quick switch. Real WPF windows parked off-screen and never activated.
/// </summary>
public sealed class MultiViewTests
{
    private static readonly MainView[] AllViews = [MainView.Classic, MainView.List, MainView.Card, MainView.Compact, MainView.Panel];

    private static string L(string key) => BrowserFlip.App.Localization.Loc.Instance[key];

    private static UserSettings Normal(MainView view = MainView.Classic)
    {
        var s = UserSettings.InMemory();
        s.PreferredView = view;
        return s;
    }

    // ------------------------------------------------------------------ settings file

    [Fact]
    public void New_users_and_v02_settings_files_start_in_the_classic_view()
    {
        Assert.Equal(MainView.Classic, UserSettings.InMemory().PreferredView);

        // A v0.2 file (no view fields): every v0.2 value is kept, the view is Classic.
        var v02 = new JsonObject
        {
            ["Language"] = "ja",
            ["Theme"] = "Dark",
            ["Mode"] = "Compact",
            ["Topmost"] = true,
            ["DockEdge"] = "Left",
            ["BarSize"] = "Medium",
            ["MainBounds"] = new JsonObject { ["Left"] = 10, ["Top"] = 20, ["Width"] = 1170, ["Height"] = 800 },
            ["MainScale"] = 2.25,
            ["MainWidthByUser"] = true,
        };
        var s = UserSettings.InMemory();
        s.Apply(v02);
        Assert.Equal(MainView.Classic, s.PreferredView);
        Assert.Empty(s.ViewSizes);
        Assert.Equal("ja", s.Language);
        Assert.Equal(ThemePreference.Dark, s.Theme);
        Assert.Equal(WindowMode.Compact, s.Mode);
        Assert.True(s.Topmost);
        Assert.Equal(DockEdge.Left, s.DockEdge);
        Assert.Equal(BarSize.Medium, s.BarSize);
        Assert.Equal(PixelRect.FromSize(10, 20, 1170, 800), s.MainBounds);
        Assert.True(s.MainWidthByUser);
    }

    [Theory]
    [InlineData("Hologram")]
    [InlineData("2")]
    [InlineData("")]
    [InlineData("classic")]
    public void An_unknown_or_damaged_view_falls_back_to_classic_without_touching_other_settings(string value)
    {
        var s = UserSettings.InMemory();
        s.Apply(new JsonObject { ["PreferredView"] = value, ["Theme"] = "Light", ["Topmost"] = true });
        Assert.Equal(MainView.Classic, s.PreferredView);
        Assert.Equal(ThemePreference.Light, s.Theme);
        Assert.True(s.Topmost);

        var wrongType = UserSettings.InMemory();
        wrongType.Apply(new JsonObject { ["PreferredView"] = 3, ["ViewSizes"] = "nonsense", ["BarSize"] = "Medium" });
        Assert.Equal(MainView.Classic, wrongType.PreferredView);
        Assert.Empty(wrongType.ViewSizes);
        Assert.Equal(BarSize.Medium, wrongType.BarSize);
    }

    [Fact]
    public void View_and_sizes_survive_a_restart_and_invalid_sizes_are_dropped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bf-multiview-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "settings.json");
        try
        {
            var s = UserSettings.Load(file);
            s.PreferredView = MainView.Card;
            s.ViewSizes[MainView.Card] = new ViewSize(700, 500);
            s.ViewSizes[MainView.Panel] = new ViewSize(900.26, 640);
            s.Language = "zh-CN";
            s.Save();

            var reloaded = UserSettings.Load(file);
            Assert.Equal(MainView.Card, reloaded.PreferredView);
            Assert.Equal(new ViewSize(700, 500), reloaded.ViewSizes[MainView.Card]);
            Assert.Equal(900.3, reloaded.ViewSizes[MainView.Panel].Width, 6); // saved with one decimal
            Assert.Equal("zh-CN", reloaded.Language);

            var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            json["ViewSizes"] = new JsonObject
            {
                ["Card"] = new JsonObject { ["Width"] = 700, ["Height"] = 500 },
                ["Classic"] = new JsonObject { ["Width"] = 700, ["Height"] = 500 },   // classic has its own rules
                ["List"] = new JsonObject { ["Width"] = 50, ["Height"] = 500 },       // too small
                ["Panel"] = new JsonObject { ["Width"] = "wide", ["Height"] = 500 },  // not a number
                ["Hologram"] = new JsonObject { ["Width"] = 700, ["Height"] = 500 },  // unknown view
                ["1"] = new JsonObject { ["Width"] = 700, ["Height"] = 500 },         // numeric name
            };
            File.WriteAllText(file, json.ToJsonString());
            var filtered = UserSettings.Load(file);
            Assert.Equal([MainView.Card], filtered.ViewSizes.Keys);

            File.WriteAllText(file, "{ not json");
            Assert.Equal(MainView.Classic, UserSettings.Load(file).PreferredView);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Set_view_command_accepts_a_view_or_its_name_and_ignores_anything_else()
    {
        var settings = UserSettings.InMemory();
        using var shell = new ShellViewModel(settings);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shell.SetViewCommand.Execute(MainView.List);
        Assert.Equal(MainView.List, settings.PreferredView);
        Assert.True(shell.IsListView);
        Assert.False(shell.IsClassicView);
        Assert.Contains(nameof(ShellViewModel.IsListView), changed);
        Assert.Contains(nameof(ShellViewModel.IsClassicView), changed);

        shell.SetViewCommand.Execute("panel");
        Assert.Equal(MainView.Panel, shell.CurrentView);
        Assert.Equal(MainView.Panel, shell.SelectedView.Value);

        foreach (var junk in new object?[] { "Hologram", "4", null, 42, (MainView)99 })
        {
            shell.SetViewCommand.Execute(junk);
            Assert.Equal(MainView.Panel, shell.CurrentView);
        }

        shell.CurrentView = (MainView)99; // never stored as an undefined value
        Assert.Equal(MainView.Classic, shell.CurrentView);
        Assert.Equal(AllViews, shell.Views.Select(v => v.Value));
    }

    [Fact]
    public void Compact_is_the_smallest_view_and_every_view_has_sane_minimums()
    {
        var compact = MainWindow.LayoutFor(MainView.Compact);
        foreach (var view in new[] { MainView.Classic, MainView.List, MainView.Card, MainView.Panel })
        {
            Assert.True(compact.Width < MainWindow.LayoutFor(view).Width, $"{view} wider than compact");
        }

        foreach (var view in AllViews)
        {
            var l = MainWindow.LayoutFor(view);
            Assert.InRange(l.MinWidth, 280, l.Width);
            Assert.True(l.MinHeight >= 150, $"{view} min height");
            Assert.Equal(view is MainView.Classic or MainView.Compact, l.HeightFollowsContent);
        }

        Assert.Equal(MainWindow.LayoutFor(MainView.Classic), MainWindow.LayoutFor((MainView)99));
    }

    [Fact]
    public void Fit_inside_keeps_the_whole_window_in_the_work_area()
    {
        var work = new PixelRect(0, 0, 1920, 1032);
        Assert.Equal(PixelRect.FromSize(100, 100, 800, 600), DesktopCoordinator.FitInside(PixelRect.FromSize(100, 100, 800, 600), work));
        Assert.Equal(PixelRect.FromSize(1120, 432, 800, 600), DesktopCoordinator.FitInside(PixelRect.FromSize(1500, 900, 800, 600), work));
        Assert.Equal(PixelRect.FromSize(0, 0, 800, 600), DesktopCoordinator.FitInside(PixelRect.FromSize(-500, -300, 800, 600), work));
        Assert.Equal(PixelRect.FromSize(0, 0, 3000, 2000), DesktopCoordinator.FitInside(PixelRect.FromSize(50, 50, 3000, 2000), work));
    }

    [Theory]
    [InlineData("0.3.0+2657df857f6a22c7592f4f22c89b7f6b2409da8b", "0.3.0 (2657df8)")]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("0.3.0+", "0.3.0")]
    [InlineData("0.3.0+abc", "0.3.0 (abc)")]
    [InlineData(null, "?")]
    public void About_shows_the_version_and_short_commit(string? informational, string expected)
        => Assert.Equal(expected, AboutContent.DisplayVersion(informational));

    // ------------------------------------------------------------------ switching (real windows)

    [Fact]
    public void Every_view_is_shown_immediately_from_the_view_menu_and_pages_are_reused()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(), Primary);
            var window = h.Coordinator.MainWindow;
            Assert.Equal(MainView.Classic, window.View);
            Assert.Null(window.CurrentPage);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("RootScroller")).Visibility);

            for (var round = 0; round < 3; round++)
            {
                foreach (var view in AllViews.Skip(1).Append(MainView.Classic))
                {
                    h.Shell.SetViewCommand.Execute(view);
                    Harness.Pump();
                    Assert.Equal(view, window.View);
                    Assert.Equal(view, h.Settings.PreferredView);
                    Assert.True(window.IsVisible, "the main window stays shown");
                    Assert.Equal(view == MainView.Classic, ((FrameworkElement)window.FindName("RootScroller")).Visibility == Visibility.Visible);
                    Assert.Equal(view switch
                    {
                        MainView.List => typeof(ListViewPage),
                        MainView.Card => typeof(CardViewPage),
                        MainView.Compact => typeof(CompactViewPage),
                        MainView.Panel => typeof(PanelViewPage),
                        _ => null,
                    }, window.CurrentPage?.GetType());
                    AssertMenuChecks(window, view);
                    var rules = MainWindow.LayoutFor(view);
                    Assert.Equal(rules.HeightFollowsContent || rules.ContentHeightUntilResized ? SizeToContent.Height : SizeToContent.Manual, window.SizeToContent);
                    if (window.CurrentPage is { } page)
                    {
                        // The page fills the window width (not docked at its own desired width).
                        var area = (FrameworkElement)window.FindName("ViewArea");
                        Assert.True(area.ActualWidth > 100, "view area laid out");
                        Assert.Equal(area.ActualWidth, page.ActualWidth, 1);
                    }
                }
            }

            Assert.Equal(4, window.CreatedPages); // each view created once, never again (no repeated setup)

            // Clicking the view that is already shown: the checkable item unchecks itself, the command puts it back.
            var classic = (MenuItem)window.FindName("ClassicViewMenuItem");
            Assert.True(classic.IsCheckable);
            classic.SetCurrentValue(MenuItem.IsCheckedProperty, false);
            classic.Command.Execute(classic.CommandParameter);
            Harness.Pump();
            AssertMenuChecks(window, MainView.Classic);
            Assert.Equal(15, h.Coordinator.ViewSwitches); // the repeated Classic click above switched nothing
            Assert.False(h.Shell.IsCompact); // a view switch never changes the surface (main window / toolbar)
        });
    }

    [Fact]
    public void Every_view_shows_the_shared_state_and_switches_through_the_shared_command()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(), Primary);
            foreach (var view in AllViews.Skip(1))
            {
                h.Shell.CurrentView = view;
                Harness.Pump();
                var page = h.Coordinator.MainWindow.CurrentPage!;
                var buttons = Descendants<Button>(page).Where(b => b.CommandParameter is BrowserCardViewModel).ToList();
                var cards = buttons.Select(b => (BrowserCardViewModel)b.CommandParameter).Distinct().ToList();
                Assert.Equal(h.Main.Browsers.Select(b => b.DisplayName).Order(), cards.Select(c => c.DisplayName).Order());
                Assert.All(buttons, b => Assert.Same(h.Main.SelectBrowserCommand, b.Command));

                // The default (Edge, from the fake registry) is the one marked, in every view.
                var edge = buttons.First(b => ((BrowserCardViewModel)b.CommandParameter).DisplayName == "Microsoft Edge");
                Assert.True(((BrowserCardViewModel)edge.CommandParameter).IsDefault);
                if (view != MainView.Compact)
                {
                    Assert.Contains(Descendants<TextBlock>(edge), t => t.Text == L("Badge_Default") && IsShown(t));
                }

                Assert.Contains(Descendants<TextBlock>(page), t => IsShown(t) && t.Text.Contains("Microsoft Edge", StringComparison.Ordinal)
                                                                    && t.DataContext is MainViewModel);
            }

            // Clicking a browser in a new view = the same flow as a Classic card: Windows Settings opens and the
            // app waits for Windows to report the change. Nothing is shown as switched before that.
            h.Shell.CurrentView = MainView.List;
            Harness.Pump();
            var chrome = Descendants<Button>(h.Coordinator.MainWindow.CurrentPage!)
                .First(b => (b.CommandParameter as BrowserCardViewModel)?.DisplayName == "Google Chrome");
            chrome.Command.Execute(chrome.CommandParameter);
            Assert.True(h.Main.IsWaitingForChange);
            Assert.Equal("Guide_Waiting", h.Main.Message?.Key);
            Assert.Equal("Microsoft Edge", h.Main.CurrentDefaultName);
            Assert.False(h.Main.Browsers.First(b => b.DisplayName == "Google Chrome").IsDefault);
        });
    }

    [Fact]
    public void Sizes_are_kept_per_view_and_the_position_is_shared()
    {
        Run(() =>
        {
            var settings = Normal();
            settings.MainBounds = PixelRect.FromSize(400, 300, 1170, 900);
            settings.MainScale = 2.25;
            settings.MainWidthByUser = false;
            using var h = new Harness(settings, Primary);

            // The user moved the classic window, then switched to Card (default size, same place).
            h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(500, 350, 1170, 900));
            h.Shell.CurrentView = MainView.Card;
            Harness.Pump();
            var window = h.Coordinator.MainWindow;
            Assert.Equal(680, window.Width, 1);
            Assert.Equal(SizeToContent.Height, window.SizeToContent); // UI-02: as tall as the cards, no empty half window
            Assert.False(window.ViewHeightByUser);
            Assert.Equal((500, 350), (h.Placement.GetBounds(h.MainHandle).Left, h.Placement.GetBounds(h.MainHandle).Top));
            Assert.Equal((500, 350), (h.Settings.MainBounds!.Value.Left, h.Settings.MainBounds.Value.Top));

            // Only the width changed so far: the saved height stays "follow the content" (0).
            h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(600, 400, 1800, 1000));
            h.Shell.CurrentView = MainView.List;
            Harness.Pump();
            Assert.Equal(new ViewSize(800, 0), h.Settings.ViewSizes[MainView.Card]);
            Assert.Equal(440, window.Width, 1);
            Assert.Equal(SizeToContent.Height, window.SizeToContent);

            // Card resized in height by the user (what WM_EXITSIZEMOVE records), then List: Card's size is kept.
            h.Shell.CurrentView = MainView.Card;
            Harness.Pump();
            window.ViewHeightByUser = true;
            window.SizeToContent = SizeToContent.Manual;
            h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(600, 400, 1800, 1350));
            h.Shell.CurrentView = MainView.List;
            Harness.Pump();
            Assert.Equal(new ViewSize(800, 600), h.Settings.ViewSizes[MainView.Card]);
            Assert.Equal(PixelRect.FromSize(600, 400, 1170, 900), h.Settings.MainBounds); // classic width rules kept

            // Back to Card: its own size again.
            h.Shell.CurrentView = MainView.Card;
            Harness.Pump();
            Assert.Equal(800, window.Width, 1);
            Assert.Equal(600, window.Height, 1);
            Assert.Equal(SizeToContent.Manual, window.SizeToContent); // the user's height wins over the content
            Assert.True(window.ViewHeightByUser);

            // Back to Classic: the v0.2 rules (app-chosen width, height from the content).
            h.Shell.CurrentView = MainView.Classic;
            Harness.Pump();
            Assert.Equal(SizeToContent.Height, window.SizeToContent);
            Assert.True(window.Width >= MainWindow.DefaultWidth - 0.5);
            Assert.True(window.WidthIsAutomatic);
        });
    }

    [Fact]
    public void A_switched_view_stays_inside_the_work_area_even_after_a_monitor_was_disconnected()
    {
        Run(() =>
        {
            // Saved near the bottom-right corner: the larger Panel view is moved in, not cut off.
            var settings = Normal();
            settings.MainBounds = PixelRect.FromSize(3500, 2000, 1170, 300);
            settings.MainScale = 2.25;
            using (var h = new Harness(settings, Primary))
            {
                h.Placement.SetBounds(h.MainHandle, PixelRect.FromSize(3500, 2000, 1170, 300));
                h.Shell.CurrentView = MainView.Panel;
                Harness.Pump();
                var b = h.Placement.GetBounds(h.MainHandle);
                Assert.Equal(3840 - (int)Math.Round(740 * 2.25), b.Left);
                Assert.Equal(2292 - (int)Math.Round(500 * 2.25), b.Top);
            }

            // Saved on a monitor that is gone: started in the Card view on the remaining monitor, fully inside.
            var gone = Normal(MainView.Card);
            gone.MainBounds = PixelRect.FromSize(5000, 100, 1000, 700);
            gone.ViewSizes[MainView.Card] = new ViewSize(700, 500);
            using (var h = new Harness(gone, Primary))
            {
                var b = h.Placement.GetBounds(h.MainHandle);
                var work = Primary.WorkArea;
                Assert.Equal(MainView.Card, h.Coordinator.MainWindow.View);
                Assert.InRange(b.Left, work.Left, work.Right - (int)Math.Round(700 * 2.25));
                Assert.InRange(b.Top, work.Top, work.Bottom - (int)Math.Round(500 * 2.25));
            }

            // A saved size larger than the work area is capped to it.
            var huge = Normal(MainView.Panel);
            huge.ViewSizes[MainView.Panel] = new ViewSize(5000, 5000);
            huge.MainBounds = PixelRect.FromSize(0, 0, 1170, 900);
            using (var h = new Harness(huge, Primary))
            {
                var window = h.Coordinator.MainWindow;
                Assert.True(window.Width <= (3840 / 2.25) + 0.5);
                Assert.True(window.Height <= (2292 / 2.25) - 16 + 0.5);
            }
        });
    }

    [Fact]
    public void The_view_used_last_comes_back_at_start()
    {
        Run(() =>
        {
            foreach (var view in AllViews)
            {
                using var h = new Harness(Normal(view), Primary);
                Assert.Equal(view, h.Coordinator.MainWindow.View);
                AssertMenuChecks(h.Coordinator.MainWindow, view);
            }
        });
    }

    [Fact]
    public void The_floating_toolbar_and_back_keep_the_view()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(MainView.List), Primary);
            h.Shell.EnterCompactCommand.Execute(null); // View > Show Floating Toolbar
            Harness.Pump();
            Assert.True(h.Shell.IsCompact);
            Assert.True(h.Coordinator.Bar.IsVisible);
            Assert.False(h.Coordinator.MainWindow.IsVisible);

            h.Shell.ShowNormalCommand.Execute(null);
            Harness.Pump();
            Assert.True(h.Coordinator.MainWindow.IsVisible);
            Assert.Equal(MainView.List, h.Coordinator.MainWindow.View);
            Assert.Equal(SizeToContent.Height, h.Coordinator.MainWindow.SizeToContent); // List: height from its content

            // A view chosen while the toolbar is shown (Settings window) is the one the main window comes back in.
            h.Shell.EnterCompactCommand.Execute(null);
            Harness.Pump();
            h.Shell.CurrentView = MainView.Panel;
            h.Shell.ShowNormalCommand.Execute(null);
            Harness.Pump();
            Assert.Equal(MainView.Panel, h.Coordinator.MainWindow.View);
            Assert.True(h.Coordinator.Bar.IsVisible is false);
        });
    }

    // ------------------------------------------------------------------ Settings / About / tray

    [Fact]
    public void One_settings_window_opens_on_the_requested_page_and_cancel_undoes_its_changes()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(), Primary);
            Assert.Null(h.Coordinator.SettingsWindow);
            h.Shell.OpenSettingsCommand.Execute(null);
            Harness.Pump();
            var settings = h.Coordinator.SettingsWindow;
            Assert.NotNull(settings);
            Assert.True(settings.IsVisible);
            Assert.Same(h.Coordinator.MainWindow, settings.Owner);
            Assert.Equal(0, ((TabControl)settings.FindName("Pages")).SelectedIndex);

            // Asked again (menu bar "Floating Toolbar Settings", tray, Panel view): the same window, other page.
            h.Shell.OpenSettingsCommand.Execute(SettingsPage.FloatingToolbar);
            h.Coordinator.OpenSettings(SettingsPage.FloatingToolbar);
            Harness.Pump();
            Assert.Same(settings, h.Coordinator.SettingsWindow);
            Assert.Equal(2, ((TabControl)settings.FindName("Pages")).SelectedIndex);

            // The user had clicked another page; asking for a page again activates the window, which gives the focus
            // back to that page's header, and a TabItem selects itself on focus (found on the real desktop). The
            // requested page is applied once more after that.
            var advanced = (TabItem)settings.FindName("AdvancedTab");
            advanced.IsSelected = true;
            h.Coordinator.OpenSettings(SettingsPage.General);
            advanced.IsSelected = true; // what the focus restore does during the activation
            Harness.Pump();
            Assert.Equal(0, settings.PageIndex);

            // Live changes (same view models as the Classic options) ...
            // (Not the theme here: applying it touches the shared Application, which lives on another test thread.)
            h.Shell.FloatingOrientation = BarOrientation.Vertical;
            h.Shell.BarSize = BarSize.Medium;
            h.Shell.IsTopmost = true;
            h.Shell.CurrentView = MainView.Card;
            Harness.Pump();
            Assert.Equal(MainView.Card, h.Coordinator.MainWindow.View);

            // ... undone by Cancel.
            ((Button)settings.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Harness.Pump();
            Assert.Equal(BarOrientation.Horizontal, h.Shell.FloatingOrientation);
            Assert.Equal(BarSize.Small, h.Shell.BarSize);
            Assert.False(h.Shell.IsTopmost);
            Assert.Equal(MainView.Classic, h.Coordinator.MainWindow.View);
            Assert.Null(h.Coordinator.SettingsWindow);

            // Close keeps the changes.
            h.Coordinator.OpenSettings(SettingsPage.Appearance);
            Harness.Pump();
            h.Shell.BarSize = BarSize.Medium;
            ((Button)h.Coordinator.SettingsWindow!.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Harness.Pump();
            Assert.Equal(BarSize.Medium, h.Shell.BarSize);
            Assert.Null(h.Coordinator.SettingsWindow);
        });
    }

    [Fact]
    public void Toolbar_options_button_opens_the_settings_window_outside_the_classic_view()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(MainView.Card), Primary);
            h.Shell.EnterCompactCommand.Execute(null);
            Harness.Pump();
            h.Shell.OpenOptionsCommand.Execute(null); // the gear on the floating toolbar
            Harness.Pump();
            Assert.True(h.Shell.IsCompact); // the toolbar stays
            Assert.NotNull(h.Coordinator.SettingsWindow);
            Assert.Equal(2, ((TabControl)h.Coordinator.SettingsWindow!.FindName("Pages")).SelectedIndex);
            h.Coordinator.SettingsWindow.Close();

            // Classic: the v0.2 behavior (main window with the display options open).
            h.Shell.CurrentView = MainView.Classic;
            h.Shell.OpenOptionsCommand.Execute(null);
            Harness.Pump();
            Assert.False(h.Shell.IsCompact);
            Assert.True(h.Shell.IsOptionsOpen);
            Assert.Null(h.Coordinator.SettingsWindow);
        });
    }

    [Fact]
    public void One_about_window()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(MainView.Panel), Primary);
            h.Shell.OpenAboutCommand.Execute(null);
            Harness.Pump();
            var about = h.Coordinator.AboutWindow;
            Assert.NotNull(about);
            h.Shell.OpenAboutCommand.Execute(null);
            Assert.Same(about, h.Coordinator.AboutWindow);
            var version = AboutContent.DisplayVersion(typeof(AboutWindow).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion);
            var content = Descendants<AboutContent>(about).Single();
            var line = (TextBlock)content.FindName("VersionText");
            Assert.Equal($"{L("About_VersionLabel")} {version}", string.Concat(line.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)));
            Assert.StartsWith("0.3.0", version, StringComparison.Ordinal);
            about.Close();
            Harness.Pump();
            Assert.Null(h.Coordinator.AboutWindow);
        });
    }

    [Fact]
    public void Tray_menu_shows_the_current_default_and_switches_through_windows_settings()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(), Primary);
            var model = h.Coordinator.BuildMenuModel();
            Assert.True(model[0].IsDefault);
            var current = model.Single(i => i.Text == BrowserFlip.App.Localization.Loc.Instance.Format("Tray_Current", "Microsoft Edge"));
            Assert.False(current.IsEnabled);

            var switcher = model.Single(i => i.Text == L("Tray_Switch"));
            Assert.True(switcher.IsEnabled);
            Assert.Equal(h.Main.Browsers.Select(b => b.DisplayName), switcher.Children!.Select(c => c.Text));
            Assert.Equal(["Microsoft Edge"], switcher.Children!.Where(c => c.IsChecked).Select(c => c.Text));

            // Choosing Chrome: Windows Settings opens (fake launcher); the default is only what Windows reports.
            h.Coordinator.HideToTray();
            switcher.Children!.First(c => c.Text == "Google Chrome").OnClick!();
            Harness.Pump();
            Assert.True(h.Main.IsWaitingForChange);
            Assert.Equal("Microsoft Edge", h.Main.CurrentDefaultName);
            Assert.True(h.Coordinator.IsTrayOnly); // no error: nothing pops up

            // Settings from the tray opens the Settings window (main window shown first, so the test window stays
            // centered on the off-screen main window instead of the real screen).
            h.Tray!.Click();
            Harness.Pump();
            model.Single(i => i.Text == L("Menu_Settings")).OnClick!();
            Harness.Pump();
            Assert.NotNull(h.Coordinator.SettingsWindow);
            Assert.Same(h.Coordinator.MainWindow, h.Coordinator.SettingsWindow!.Owner);
            h.Coordinator.SettingsWindow!.Close();
        });
    }

    [Fact]
    public void Every_view_and_the_settings_window_lay_out_in_every_language_without_clipped_text()
    {
        var problems = new List<string>();
        Run(() =>
        {
            var before = BrowserFlip.App.Localization.Loc.Instance.Culture.Name;
            try
            {
                foreach (var language in BrowserFlip.App.Localization.Loc.SupportedCultures)
                {
                    BrowserFlip.App.Localization.Loc.Instance.SetLanguage(language);
                    using var h = new Harness(Normal(), Primary);
                    foreach (var view in AllViews.Skip(1))
                    {
                        h.Shell.CurrentView = view;
                        Harness.Pump();
                        var page = h.Coordinator.MainWindow.CurrentPage!;
                        if (view == MainView.Panel)
                        {
                            // Every Panel page, one after the other.
                            foreach (var nav in new[] { "NavHome", "NavBrowsers", "NavSettings", "NavAbout" })
                            {
                                ((ListBoxItem)page.FindName(nav)).IsSelected = true;
                                Harness.Pump();
                                problems.AddRange(Clipped(page).Select(t => $"{language}/{view}/{nav}: '{t}'"));
                            }
                        }
                        else
                        {
                            problems.AddRange(Clipped(page).Select(t => $"{language}/{view}: '{t}'"));
                        }
                    }

                    h.Coordinator.OpenSettings(SettingsPage.General);
                    Harness.Pump();
                    var settings = h.Coordinator.SettingsWindow!;
                    var pages = (TabControl)settings.FindName("Pages");
                    for (var i = 0; i < pages.Items.Count; i++)
                    {
                        pages.SelectedIndex = i;
                        Harness.Pump();
                        problems.AddRange(Clipped(settings).Select(t => $"{language}/Settings[{i}]: '{t}'"));
                    }

                    settings.Close();
                }
            }
            finally
            {
                BrowserFlip.App.Localization.Loc.Instance.SetLanguage(before);
            }
        });

        Assert.Empty(problems);
    }

    /// <summary>Shown text cut off with an ellipsis (text that is designed to trim carries a tooltip and is skipped).</summary>
    private static IEnumerable<string> Clipped(DependencyObject root)
    {
        foreach (var tb in Descendants<TextBlock>(root).Where(t => IsShown(t) && t.TextTrimming != TextTrimming.None && t.ToolTip is null))
        {
            var available = tb.ActualWidth;
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var needed = tb.DesiredSize.Width;
            tb.InvalidateMeasure();
            if (tb.TextWrapping == TextWrapping.NoWrap && needed > available + 0.5)
            {
                yield return tb.Text;
            }
        }
    }

    [Fact]
    public void Tray_mode_hides_everything_and_the_tray_brings_back_the_same_view()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(MainView.Card), Primary);
            h.Coordinator.OpenSettings(SettingsPage.General);
            Harness.Pump();

            h.Shell.TrayModeCommand.Execute(null); // View > Tray Mode
            Harness.Pump();
            Assert.True(h.Coordinator.IsTrayOnly);
            Assert.False(h.Coordinator.MainWindow.IsVisible);
            Assert.False(h.Coordinator.Bar.IsVisible);
            Assert.Null(h.Coordinator.SettingsWindow);
            Assert.True(h.Tray!.Visible);
            Assert.False(h.Tray.Disposed);

            h.Tray.Click();
            Harness.Pump();
            Assert.True(h.Coordinator.MainWindow.IsVisible);
            Assert.Equal(MainView.Card, h.Coordinator.MainWindow.View);
            Assert.False(h.Coordinator.IsTrayOnly);
        });
    }

    [Fact]
    public void File_exit_ends_the_app_and_releases_the_tray_icon()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(MainView.Compact), Primary);
            var exit = (MenuItem)h.Coordinator.MainWindow.FindName("ExitMenuItem");
            exit.Command.Execute(null);
            Harness.Pump();
            Assert.True(h.Tray!.Disposed);
            Assert.False(h.Coordinator.MainWindow.IsVisible);
        });
    }

    // ------------------------------------------------------------------ Phase 3 (UI polish)

    [Fact]
    public void List_and_card_start_as_tall_as_their_content_and_scroll_when_there_are_many_browsers()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(MainView.List), Primary);
            var window = h.Coordinator.MainWindow;
            Harness.Pump();

            // UI-01 / UI-02: three browsers, no empty half window; nothing to scroll; the footer is inside the window.
            Assert.Equal(SizeToContent.Height, window.SizeToContent);
            Assert.InRange(window.ActualHeight, 200, 420);
            var scroller = Descendants<ScrollViewer>(window.CurrentPage!).First();
            Assert.Equal(0, scroller.ScrollableHeight, 1);
            AssertFooterInside(window);

            h.Shell.CurrentView = MainView.Card;
            Harness.Pump();
            Assert.InRange(window.ActualHeight, 200, 420);
            AssertFooterInside(window);

            // Many browsers: the window grows only up to the work area; the list scrolls; the footer stays visible.
            for (var i = 0; i < 60; i++)
            {
                var exe = $@"C:\X{i}\browser{i}.exe";
                h.Registry.AddBrowser(Core.Platform.RegistryRoot.LocalMachine, Core.Platform.RegistryViewKind.Registry64,
                    $"Browser {i:00}", $"Browser {i:00}", $"\"{exe}\"", $"B{i}HTML", $"B{i}HTML", $"Browser {i:00}");
                h.Files.Files.Add(exe);
            }

            h.Main.Refresh();
            foreach (var view in new[] { MainView.Card, MainView.List })
            {
                h.Shell.CurrentView = view;
                Harness.Pump();
                Assert.Equal(63, h.Main.Browsers.Count);
                // The cap is the work area of the (real) monitor the parked test window is on.
                Assert.True(double.IsFinite(window.MaxHeight) && window.ActualHeight <= window.MaxHeight + 0.5, $"{view}: {window.ActualHeight:N0} DIP, cap {window.MaxHeight:N0}");
                Assert.True(Descendants<ScrollViewer>(window.CurrentPage!).First().ScrollableHeight > 0, $"{view}: the browsers scroll");
                AssertFooterInside(window);
            }
        });
    }

    [Fact]
    public void Panel_default_size_is_smaller_and_four_tiles_fit_in_one_row()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(MainView.Panel), Primary);
            Harness.Pump();
            var window = h.Coordinator.MainWindow;
            Assert.Equal(740, window.Width, 1);
            Assert.Equal(500, window.Height, 1);
            var page = window.CurrentPage!;
            var tiles = Descendants<Button>((DependencyObject)page.FindName("HomeTiles")).Where(b => b.CommandParameter is BrowserCardViewModel).ToList();
            Assert.Equal(3, tiles.Count);
            Assert.Single(tiles.Select(t => Math.Round(t.TranslatePoint(new Point(), page).Y)).Distinct()); // one row

            // Every line of every tile's text lies inside the tile (a two-line hint must not be cut, found on the real desktop).
            foreach (var tile in tiles)
            {
                foreach (var text in Descendants<TextBlock>(tile).Where(t => IsShown(t)))
                {
                    // Content taller than the tile overflows its bottom (the tile clips it).
                    var bottom = text.TranslatePoint(new Point(0, text.ActualHeight), tile).Y;
                    Assert.True(bottom <= tile.ActualHeight - 1, $"'{text.Text}' cut in its tile (bottom {bottom:N1} of {tile.ActualHeight:N1})");
                }
            }

            // UI-05: the name is shown once (no "Current default browser: Microsoft Edge" sentence under it).
            var home = (FrameworkElement)page.FindName("HomePage");
            Assert.DoesNotContain(Descendants<TextBlock>(home), t => IsShown(t) && t.Text == h.Main.CurrentDefaultText);
            Assert.Equal("Microsoft Edge", ((TextBlock)page.FindName("HomeCurrentName")).Text);
        });
    }

    [Fact]
    public void Tile_texts_are_never_cut_in_any_language()
    {
        var problems = new List<string>();
        Run(() =>
        {
            var before = BrowserFlip.App.Localization.Loc.Instance.Culture.Name;
            try
            {
                foreach (var language in BrowserFlip.App.Localization.Loc.SupportedCultures)
                {
                    BrowserFlip.App.Localization.Loc.Instance.SetLanguage(language);
                    using var h = new Harness(Normal(MainView.Panel), Primary);
                    Harness.Pump();
                    foreach (var view in new[] { MainView.Panel, MainView.Card })
                    {
                        h.Shell.CurrentView = view;
                        Harness.Pump();
                        foreach (var tile in Descendants<Button>(h.Coordinator.MainWindow.CurrentPage!).Where(b => b.CommandParameter is BrowserCardViewModel && b.Height > 0))
                        {
                            // The tile clips its content at the inner edge (padding + border): every line must end above it.
                            var inner = tile.ActualHeight - tile.Padding.Bottom - tile.BorderThickness.Bottom;
                            foreach (var text in Descendants<TextBlock>(tile).Where(IsShown))
                            {
                                var bottom = text.TranslatePoint(new Point(0, text.ActualHeight), tile).Y;
                                if (bottom > inner + 0.5)
                                {
                                    problems.Add($"{language}/{view}: '{text.Text}' ends at {bottom:N1}, tile content ends at {inner:N1}");
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                BrowserFlip.App.Localization.Loc.Instance.SetLanguage(before);
            }
        });

        Assert.Empty(problems);
    }

    [Fact]
    public void Panel_settings_page_summarizes_the_live_settings_without_a_second_copy()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(MainView.Panel), Primary);
            var page = h.Coordinator.MainWindow.CurrentPage!;
            ((ListBoxItem)page.FindName("NavSettings")).IsSelected = true;
            Harness.Pump();
            Assert.Equal(h.Shell.SelectedView.DisplayName, ((TextBlock)page.FindName("SummaryView")).Text);
            Assert.Equal(L("State_Off"), ((TextBlock)page.FindName("SummaryTopmost")).Text);
            Assert.Equal(h.Main.SelectedLanguage.DisplayName, ((TextBlock)page.FindName("SummaryLanguage")).Text);

            h.Shell.IsTopmost = true; // e.g. changed in the Settings window
            Harness.Pump();
            Assert.Equal(L("State_On"), ((TextBlock)page.FindName("SummaryTopmost")).Text);
            h.Shell.IsTopmost = false;
        });
    }

    [Fact]
    public void Settings_close_and_x_keep_changes_and_cancel_never_undoes_what_was_set_before_opening()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(), Primary);
            h.Shell.BarSize = BarSize.Medium; // set before the window opens

            h.Coordinator.OpenSettings(SettingsPage.FloatingToolbar);
            Harness.Pump();
            h.Shell.FloatingOrientation = BarOrientation.Vertical;
            h.Coordinator.SettingsWindow!.Close(); // title bar X = Close
            Harness.Pump();
            Assert.Equal(BarOrientation.Vertical, h.Shell.FloatingOrientation);

            h.Coordinator.OpenSettings(SettingsPage.FloatingToolbar);
            Harness.Pump();
            h.Shell.FloatingOrientation = BarOrientation.Horizontal;
            h.Shell.IsTopmost = true;
            ((Button)h.Coordinator.SettingsWindow!.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Harness.Pump();
            Assert.Equal(BarOrientation.Vertical, h.Shell.FloatingOrientation); // back to the value at opening
            Assert.False(h.Shell.IsTopmost);
            Assert.Equal(BarSize.Medium, h.Shell.BarSize); // set before opening: untouched
            Assert.Equal(BarSize.Medium, h.Settings.BarSize);
        });
    }

    [Fact]
    public void Tooltips_name_the_browser_and_say_whether_it_is_the_default()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(), Primary);
            var edge = h.Main.Browsers.Single(b => b.DisplayName == "Microsoft Edge");
            var chrome = h.Main.Browsers.Single(b => b.DisplayName == "Google Chrome");
            Assert.Equal($"Microsoft Edge — {L("Home_Current")}", edge.ToolTipTitle);
            Assert.Equal("Google Chrome", chrome.ToolTipTitle);
            Assert.StartsWith(chrome.ToolTipTitle, chrome.ToolTipText, StringComparison.Ordinal);
            Assert.EndsWith(chrome.DetailText, chrome.ToolTipText, StringComparison.Ordinal);

            // The views show the same text (one source: the shared card view model).
            h.Shell.CurrentView = MainView.Card;
            Harness.Pump();
            var tile = Descendants<Button>(h.Coordinator.MainWindow.CurrentPage!).First(b => b.CommandParameter == edge);
            Assert.Equal(edge.ToolTipText, tile.ToolTip);
        });
    }

    [Fact]
    public void Tray_menu_has_no_duplicate_entries_and_clean_separators()
    {
        Run(() =>
        {
            using var h = new Harness(Normal(), Primary);
            for (var round = 0; round < 3; round++)
            {
                h.Main.Refresh(); // refreshing never adds entries
                var model = h.Coordinator.BuildMenuModel();
                var texts = model.Where(i => !i.IsSeparator).Select(i => i.Text).ToList();
                Assert.Equal(texts.Count, texts.Distinct().Count());
                Assert.False(model[0].IsSeparator || model[^1].IsSeparator);
                Assert.DoesNotContain(model.Zip(model.Skip(1)), p => p.First.IsSeparator && p.Second.IsSeparator);
                var browsers = model.Single(i => i.Text == L("Tray_Switch")).Children!;
                Assert.Equal(3, browsers.Count);
                Assert.Single(browsers, b => b.IsChecked);
            }
        });
    }

    private static void AssertFooterInside(MainWindow window)
    {
        var page = window.CurrentPage!;
        var link = Descendants<Button>(page).First(b => b.Content is string s && s == L("OpenDefaultApps"));
        var bottom = link.TranslatePoint(new Point(0, link.ActualHeight), page).Y;
        Assert.True(bottom <= page.ActualHeight + 0.5, $"footer cut off ({bottom:N1} > {page.ActualHeight:N1})");
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertMenuChecks(MainWindow window, MainView view)
    {
        var items = new Dictionary<MainView, string>
        {
            [MainView.Classic] = "ClassicViewMenuItem",
            [MainView.List] = "ListViewMenuItem",
            [MainView.Card] = "CardViewMenuItem",
            [MainView.Compact] = "CompactViewMenuItem",
            [MainView.Panel] = "PanelViewMenuItem",
        };
        foreach (var (v, name) in items)
        {
            Assert.Equal(v == view, ((MenuItem)window.FindName(name)).IsChecked);
        }
    }

    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? d = element; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return element.ActualWidth > 0;
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var d in Descendants<T>(child))
            {
                yield return d;
            }
        }
    }
}
