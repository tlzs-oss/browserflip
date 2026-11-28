using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BrowserFlip.UI.Localization;
using BrowserFlip.UI.ViewModels;
using BrowserFlip.UI.Views;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// The product's own About window, which the macOS application menu's first item opens. Before
/// it existed that item was Avalonia's and opened Avalonia's About dialog - the Owner reported
/// seeing "About Avalonia" in BrowserFlip's menu bar.
/// </summary>
[Collection(SerialUiCulture.Name)]
public class AboutWindowTests
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
    public void It_shows_the_products_own_name_and_version_not_the_toolkits()
    {
        var model = new AboutViewModel(Model());

        Assert.Equal("BrowserFlip", model.ProductName);
        Assert.DoesNotContain("Avalonia", model.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia", model.ProductName, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia", model.Tagline, StringComparison.Ordinal);
        // The version comes from the assembly, so it cannot disagree with what was built.
        Assert.NotEqual("?", model.DisplayVersion);
        Assert.Contains(model.DisplayVersion, model.VersionLine, StringComparison.Ordinal);
        // The copyright comes from an assembly attribute set in Directory.Build.props; if that
        // stopped reaching this assembly the About box would quietly show a blank line.
        Assert.Contains("BrowserFlip", model.Copyright, StringComparison.Ordinal);
        Assert.StartsWith("https://", model.ProjectUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.3.0+a707d7512ab34cd", "0.3.0 (a707d75)")]
    [InlineData("0.3.0+abc", "0.3.0 (abc)")]
    [InlineData("0.3.0+", "0.3.0")]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void The_version_shows_the_build_and_a_short_commit(string? informational, string expected)
        => Assert.Equal(expected, AboutViewModel.Shorten(informational));

    [AvaloniaFact]
    public void Its_text_follows_a_language_change_made_elsewhere()
    {
        try
        {
            var main = Model();
            var about = new AboutViewModel(main);
            var window = new AboutWindow { DataContext = about };
            window.Show();

            main.Language = "ja";

            // Same defect class as the settings window before it was fixed: the labels are
            // resource lookups, so without following the main view model the whole window stays
            // in whatever language it opened in.
            Assert.Equal("BrowserFlip について", about.Title);
            Assert.Equal("BrowserFlip について", window.Title);

            main.Language = "zh-CN";
            Assert.Equal("关于 BrowserFlip", about.Title);
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [AvaloniaFact]
    public void Closing_it_stops_it_following_the_main_view_model()
    {
        var main = Model();
        var about = new AboutViewModel(main);
        var window = new AboutWindow { DataContext = about };
        window.Show();
        var raised = 0;
        about.PropertyChanged += (_, _) => raised++;

        window.Close();
        main.Language = "ja";

        try
        {
            // The main view model is the application's and outlives every About window, so a
            // window that has gone must not still be listening to it.
            Assert.Equal(0, raised);
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [AvaloniaFact]
    public void Its_close_button_closes_it()
    {
        var window = new AboutWindow { DataContext = new AboutViewModel(Model()) };
        window.Show();
        var close = window.FindControl<Button>("CloseButton");

        Assert.NotNull(close);
        // A real click, routed through the button, so deleting the handler fails this.
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent) { Source = close });

        Assert.False(window.IsVisible);
    }
}
