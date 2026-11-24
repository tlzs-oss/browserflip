using BrowserFlip.UI.Platform;

namespace BrowserFlip.Prototype.Tests;

/// <summary>Settings persistence, against a real file in a temporary directory.</summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "browserflip-r2-" + Guid.NewGuid().ToString("N"));

    private JsonSettingsStore NewStore() => new(Path.Combine(_directory, "settings.json"));

    [Fact]
    public void Defaults_are_returned_when_nothing_is_stored()
    {
        var settings = NewStore().Load();
        Assert.Equal(PrototypeSettings.Default, settings);
        Assert.Null(settings.Language);
        Assert.False(settings.Topmost);
    }

    [Fact]
    public void Settings_round_trip_through_the_file()
    {
        var store = NewStore();
        var written = new PrototypeSettings
        {
            Language = "zh-CN",
            Topmost = true,
            ShowFloatingToolbar = true,
            ToolbarX = 321,
            ToolbarY = 654,
            LaunchCount = 42,
        };

        Assert.True(store.Save(written));
        Assert.Equal(written, NewStore().Load());
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_instead_of_throwing()
    {
        var store = NewStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.Location, "{ this is not json");
        Assert.Equal(PrototypeSettings.Default, store.Load());
    }

    [Fact]
    public void Saving_creates_the_directory()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "nested", "deeper", "settings.json"));
        Assert.True(store.Save(PrototypeSettings.Default with { LaunchCount = 7 }));
        Assert.True(File.Exists(store.Location));
        Assert.Equal(7, store.Load().LaunchCount);
    }

    [Fact]
    public void An_unwritable_location_reports_failure_rather_than_throwing()
    {
        // A path whose parent is an existing *file* can never become a directory.
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "x");
        var store = new JsonSettingsStore(Path.Combine(blocker, "settings.json"));
        Assert.False(store.Save(PrototypeSettings.Default));
        Assert.Equal(PrototypeSettings.Default, store.Load());
    }

    [Fact]
    public void Saving_and_restoring_leaves_the_file_exactly_as_it_was()
    {
        // The self-test writes a probe into the real settings store; it must put the user's own
        // settings back afterwards.
        var store = NewStore();
        var mine = new PrototypeSettings { Language = "en", Topmost = false, LaunchCount = 11 };
        Assert.True(store.Save(mine));

        var saved = store.Load();
        Assert.True(store.Save(new PrototypeSettings { Language = "ja", Topmost = true, LaunchCount = 999 }));
        Assert.True(store.Save(saved));

        Assert.Equal(mine, store.Load());
    }

    [Fact]
    public void The_default_location_is_under_the_per_user_configuration_directory()
    {
        var location = JsonSettingsStore.DefaultLocation();
        Assert.Contains("BrowserFlip", location, StringComparison.Ordinal);
        Assert.EndsWith("prototype-settings.json", location, StringComparison.Ordinal);
        if (OperatingSystem.IsMacOS())
        {
            Assert.Contains("Library/Application Support", location, StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory must never fail the test run.
        }
    }
}
