using BrowserFlip.Core.Defaults;
using BrowserFlip.UI.Platform;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// The shared view model, driven entirely by in-memory adapters: no OS call is made, so these
/// tests are identical on macOS, Windows and Linux.
/// </summary>
public class MainViewModelTests
{
    private static (MainViewModel Model, FakeSwitcher Switcher, InMemorySettingsStore Store) Build(
        string? http = "com.apple.Safari", string? https = "com.apple.Safari")
    {
        var associations = new FakeAssociations().With("http", http).With("https", https);
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"))
            .Add(Browsers.Mac("com.google.Chrome", "Google Chrome"));
        var switcher = new FakeSwitcher(associations);
        var store = new InMemorySettingsStore();
        var model = new MainViewModel(catalog, switcher, new FakeIcons(), store, new DefaultBrowserService(associations));
        model.Initialize();
        return (model, switcher, store);
    }

    [Fact]
    public void Initialize_loads_the_browsers_and_marks_the_default()
    {
        var (model, _, _) = Build();
        Assert.Equal(3, model.Browsers.Count);
        Assert.Equal(DefaultStatus.Consistent, model.Status);
        Assert.Single(model.Browsers, b => b.IsDefault);
        Assert.Equal("Safari", model.Browsers.Single(b => b.IsDefault).DisplayName);
    }

    [Fact]
    public void The_default_browser_cannot_be_set_again_but_the_others_can()
    {
        var (model, _, _) = Build();
        Assert.False(model.Browsers.Single(b => b.DisplayName == "Safari").CanSetDefault);
        Assert.True(model.Browsers.Single(b => b.DisplayName == "Firefox").CanSetDefault);
    }

    [Fact]
    public async Task Setting_a_default_asks_the_platform_and_re_reads_the_result()
    {
        var (model, switcher, _) = Build();
        var firefox = model.Browsers.Single(b => b.DisplayName == "Firefox");

        await model.SetDefaultAsync(firefox, TestContext.Current.CancellationToken);

        Assert.Equal(["org.mozilla.firefox"], switcher.Requested);
        Assert.True(model.Browsers.Single(b => b.DisplayName == "Firefox").IsDefault);
        Assert.False(model.Browsers.Single(b => b.DisplayName == "Safari").IsDefault);
        Assert.Contains("Firefox", model.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declined_switch_reports_that_nothing_changed()
    {
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var model = new MainViewModel(
            catalog,
            new FakeSwitcher(associations, SwitchOutcome.Requested),
            new FakeIcons(),
            new InMemorySettingsStore(),
            new DefaultBrowserService(associations));
        model.Initialize();

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"), TestContext.Current.CancellationToken);

        Assert.True(model.Browsers.Single(b => b.DisplayName == "Safari").IsDefault);
        Assert.False(string.IsNullOrWhiteSpace(model.StatusMessage));
    }

    [Fact]
    public async Task An_unsupported_platform_reports_instead_of_throwing()
    {
        var associations = new FakeAssociations();
        var catalog = new FakeCatalog().Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var switcher = new FakeSwitcher(associations, SwitchOutcome.Unsupported) { Mechanism = SwitchMechanism.Unsupported };
        var model = new MainViewModel(catalog, switcher, new FakeIcons(), new InMemorySettingsStore(),
            new DefaultBrowserService(associations));
        model.Initialize();

        await model.SetDefaultAsync(model.Browsers[0], TestContext.Current.CancellationToken);
        Assert.Equal(SwitchMechanism.Unsupported, model.Mechanism);
        Assert.False(string.IsNullOrWhiteSpace(model.StatusMessage));
    }

    [Fact]
    public void Settings_changes_are_persisted_immediately()
    {
        var (model, _, store) = Build();
        var before = store.SaveCount;

        model.Topmost = true;
        model.ShowFloatingToolbar = true;
        model.Language = "ja";

        Assert.True(store.SaveCount >= before + 3);
        Assert.True(store.Load().Topmost);
        Assert.True(store.Load().ShowFloatingToolbar);
        Assert.Equal("ja", store.Load().Language);
    }

    [Fact]
    public void The_launch_counter_increases_on_every_start()
    {
        var associations = new FakeAssociations();
        var store = new InMemorySettingsStore();
        for (var i = 1; i <= 3; i++)
        {
            var model = new MainViewModel(new FakeCatalog(), new FakeSwitcher(associations), new FakeIcons(), store,
                new DefaultBrowserService(associations));
            model.Initialize();
            Assert.Equal(i, model.LaunchCount);
        }
    }

    [Fact]
    public void The_toolbar_position_round_trips_through_the_store()
    {
        var (model, _, _) = Build();
        model.SaveToolbarPosition(640, 480);
        Assert.Equal((640, 480), model.ToolbarPosition);
    }

    [Fact]
    public void No_default_at_all_is_reported_rather_than_crashing()
    {
        var (model, _, _) = Build(http: null, https: null);
        Assert.Equal(DefaultStatus.None, model.Status);
        Assert.DoesNotContain(model.Browsers, b => b.IsDefault);
    }
}
