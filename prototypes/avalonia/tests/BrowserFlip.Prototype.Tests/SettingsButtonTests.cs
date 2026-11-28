using System.ComponentModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BrowserFlip.UI.Localization;
using BrowserFlip.UI.ViewModels;
using BrowserFlip.UI.Views;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// The main window's own way into the settings window, approved by the Owner after the settings
/// screen was twice reported as not found: until now the status-area menu was the only route to
/// it, and on macOS the application menu cannot carry one.
/// </summary>
[Collection(SerialUiCulture.Name)]
public class SettingsButtonTests
{
    private static MainViewModel Model()
    {
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog().Add(Browsers.Mac("com.apple.Safari", "Safari"));
        var model = new MainViewModel(
            catalog, _ => new FakeSwitcher(associations), new FakeIcons(),
            new InMemorySettingsStore(), new BrowserFlip.Core.Defaults.DefaultBrowserService(associations),
            new BrowserFlip.UI.Platform.FullCapabilities("test platform", osConfirms: true));
        model.Initialize();
        return model;
    }

    [AvaloniaFact]
    public void The_main_window_has_a_settings_button_beside_refresh()
    {
        var window = new MainWindow { DataContext = Model() };
        window.Show();

        var settings = window.FindControl<Button>("SettingsButton");
        var refresh = window.FindControl<Button>("RefreshButton");

        Assert.NotNull(settings);
        Assert.NotNull(refresh);
        Assert.Equal(Strings.Get("Menu_Settings"), settings.Content);
        // Same row, so it is where someone looking for it would look.
        Assert.Same(refresh.Parent, settings.Parent);
    }

    [AvaloniaFact]
    public void Pressing_it_asks_for_the_settings_window_rather_than_opening_one()
    {
        var window = new MainWindow { DataContext = Model() };
        window.Show();
        var asked = 0;
        window.SettingsRequested += () => asked++;
        var button = window.FindControl<Button>("SettingsButton")!;

        // A real click, routed through the button. An earlier version of this test reached the
        // event's backing field and invoked the delegate, which only asserted that Action.Invoke
        // works: deleting the button's whole Click handler left the suite green.
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent) { Source = button });

        Assert.Equal(1, asked);
        // The window must not open anything itself: App owns window lifetimes, which is what
        // keeps the settings window a single instance however it is reached.
        Assert.Empty(window.OwnedWindows);
    }

    [AvaloniaFact]
    public void The_button_is_relabelled_when_the_language_changes()
    {
        try
        {
            var model = Model();
            var window = new MainWindow { DataContext = model };
            window.Show();
            var button = window.FindControl<Button>("SettingsButton")!;

            model.Language = "ja";
            var japanese = button.Content;
            model.Language = "zh-CN";
            var chinese = button.Content;

            Assert.Equal("設定…", japanese);
            Assert.Equal("设置…", chinese);
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [Fact]
    public void Every_translated_label_is_notified_when_the_language_changes()
    {
        try
        {
            var model = Model();
            var raised = new HashSet<string>(StringComparer.Ordinal);
            ((INotifyPropertyChanged)model).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is { } name)
                {
                    raised.Add(name);
                }
            };

            model.Language = "ja";

            // The notification list is hand-maintained, so a label added without being listed
            // would silently keep the old language - which is how the picker's own entry came to
            // show Chinese in a Japanese window. This asserts the whole family, not one member.
            var labels = typeof(MainViewModel)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.Name.StartsWith("Label", StringComparison.Ordinal))
                .Select(p => p.Name)
                .ToList();

            Assert.NotEmpty(labels);
            var missed = labels.Where(l => !raised.Contains(l)).ToList();
            Assert.Empty(missed);
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }
}

/// <summary>
/// The settings window has to follow "always on top" however the toggle is reached. It is owned
/// by the main window, which it almost exactly overlaps, so an always-on-top owner above a
/// normal-level settings window hides it completely - the defect the Owner reported as "the
/// settings screen does not appear".
/// </summary>
[Collection(SerialUiCulture.Name)]
public class SettingsWindowTopmostTests
{
    private static (MainViewModel Main, SettingsViewModel Settings) Models()
    {
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog().Add(Browsers.Mac("com.apple.Safari", "Safari"));
        var capabilities = new BrowserFlip.UI.Platform.FullCapabilities("test platform", osConfirms: true);
        var main = new MainViewModel(
            catalog, _ => new FakeSwitcher(associations), new FakeIcons(),
            new InMemorySettingsStore(), new BrowserFlip.Core.Defaults.DefaultBrowserService(associations),
            capabilities);
        main.Initialize();
        return (main, new SettingsViewModel(main, capabilities));
    }

    [AvaloniaFact]
    public void It_opens_at_the_same_level_as_an_always_on_top_owner()
    {
        var (main, settings) = Models();
        main.Topmost = true;

        var window = new SettingsWindow { DataContext = settings };
        window.Show();

        Assert.True(window.Topmost);
    }

    [AvaloniaFact]
    public void It_rises_when_always_on_top_is_switched_on_from_the_main_window()
    {
        var (main, settings) = Models();
        var window = new SettingsWindow { DataContext = settings };
        window.Show();
        Assert.False(window.Topmost);

        // Not through this window's own toggle: the main window's. Before this the settings view
        // model only raised from its own setter, so the owner rose and this window did not.
        main.Topmost = true;

        Assert.True(window.Topmost);
        Assert.True(settings.Topmost);
    }

    [AvaloniaFact]
    public void Its_labels_follow_a_language_change_made_on_the_main_window()
    {
        try
        {
            var (main, settings) = Models();
            var window = new SettingsWindow { DataContext = settings };
            window.Show();

            main.Language = "ja";

            Assert.Equal(Strings.Get("Settings_Title"), window.Title);
            Assert.Equal("設定…", Strings.Get("Menu_Settings"));
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [AvaloniaFact]
    public void Closing_it_stops_it_following_the_main_view_model()
    {
        var (main, settings) = Models();
        var window = new SettingsWindow { DataContext = settings };
        window.Show();
        window.Close();

        // The main view model is the application's and outlives every settings window, so a
        // window that has gone must not still be listening to it.
        main.Topmost = true;

        Assert.False(window.Topmost);
    }
}
