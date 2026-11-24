using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BrowserFlip.Core.Defaults;
using BrowserFlip.Prototype.Tests;
using BrowserFlip.UI;
using BrowserFlip.UI.ViewModels;
using BrowserFlip.UI.Views;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace BrowserFlip.Prototype.Tests;

/// <summary>Builds the real <see cref="App"/> on Avalonia's headless platform, so the XAML,
/// the styles and the bindings are the production ones.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Avalonia UI tests: the windows are really constructed, laid out and bound, without a display.
/// They verify that the shared XAML works, not merely that it compiles.
/// </summary>
public class UiTests
{
    private static MainViewModel Model(string? https = "com.apple.Safari")
    {
        var associations = new FakeAssociations().With("http", https).With("https", https);
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var model = new MainViewModel(catalog, new FakeSwitcher(associations), new FakeIcons(),
            new InMemorySettingsStore(), new DefaultBrowserService(associations));
        model.Initialize();
        return model;
    }

    [AvaloniaFact]
    public void The_main_window_builds_lays_out_and_binds()
    {
        var model = Model();
        var window = new MainWindow { DataContext = model };
        window.Show();

        Assert.True(window.Bounds.Width > 0, "the window was never laid out");
        Assert.True(window.Bounds.Height > 0);
        Assert.Equal(model.Title, window.Title);
        Assert.NotNull(window.FindControl<Button>("RefreshButton"));
        Assert.NotNull(window.FindControl<ComboBox>("LanguageBox"));
    }

    [AvaloniaFact]
    public void The_browser_list_shows_one_row_per_browser()
    {
        var model = Model();
        var window = new MainWindow { DataContext = model };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var list = window.FindControl<ItemsControl>("BrowserList");
        Assert.NotNull(list);
        Assert.Equal(model.Browsers.Count, list.ItemCount);
    }

    [AvaloniaFact]
    public void The_language_box_offers_the_three_product_languages()
    {
        var window = new MainWindow { DataContext = Model() };
        window.Show();
        var box = window.FindControl<ComboBox>("LanguageBox")!;
        Assert.Equal<string[]>(["en", "ja", "zh-CN"], [.. box.ItemsSource!.Cast<string>()]);
    }

    [AvaloniaFact]
    public void Topmost_follows_the_persisted_setting()
    {
        var model = Model();
        var window = new MainWindow { DataContext = model };
        window.Show();
        Assert.False(window.Topmost);

        model.Topmost = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.Topmost);
    }

    [AvaloniaFact]
    public void The_floating_toolbar_is_an_undecorated_always_on_top_window()
    {
        var model = Model();
        var toolbar = new FloatingToolbar { DataContext = model };
        toolbar.Show();

        Assert.True(toolbar.Topmost);
        Assert.Equal(WindowDecorations.None, toolbar.WindowDecorations);
        Assert.False(toolbar.ShowInTaskbar);
        Assert.True(toolbar.Bounds.Width > 0);
    }

    [AvaloniaFact]
    public void The_toolbar_restores_its_persisted_position()
    {
        var model = Model();
        model.SaveToolbarPosition(275, 390);
        var toolbar = new FloatingToolbar { DataContext = model };
        toolbar.Show();
        toolbar.RestorePosition();

        Assert.Equal(275, toolbar.Position.X);
        Assert.Equal(390, toolbar.Position.Y);
    }

    [AvaloniaFact]
    public void The_window_renders_a_frame()
    {
        var window = new MainWindow { DataContext = Model() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0);
    }

    [AvaloniaFact]
    public void Changing_the_language_updates_the_visible_labels()
    {
        var model = Model();
        var window = new MainWindow { DataContext = model };
        window.Show();

        model.Language = "en";
        Dispatcher.UIThread.RunJobs();
        var english = model.LabelRefresh;

        model.Language = "ja";
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(english, model.LabelRefresh);
    }
}
