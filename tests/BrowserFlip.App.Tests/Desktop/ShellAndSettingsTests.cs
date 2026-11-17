using System.IO;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;

namespace BrowserFlip.Core.Tests.Desktop;

public sealed class ShellAndSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "browserflip-tests-" + Guid.NewGuid().ToString("N"));

    private string File(string name = "settings.json")
    {
        Directory.CreateDirectory(_dir);
        return Path.Combine(_dir, name);
    }

    public void Dispose()
    {
        Loc.Instance.SetLanguage("en");
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Defaults_match_the_specification()
    {
        var s = UserSettings.InMemory();
        Assert.Equal(WindowMode.Normal, s.Mode);
        Assert.False(s.Topmost);
        Assert.True(s.DockEnabled); // docked out of the box ...
        Assert.Equal(DockEdge.Right, s.DockEdge); // ... at the right edge ...
        Assert.Equal(string.Empty, s.DockMonitor); // ... of the primary monitor
        Assert.Equal(ThemePreference.System, s.Theme);
    }

    [Fact]
    public void V01_settings_file_is_still_readable()
    {
        var path = File();
        System.IO.File.WriteAllText(path, "{\n  \"Language\": \"ja\"\n}");
        var s = UserSettings.Load(path);
        Assert.Equal("ja", s.Language);
        Assert.Equal(WindowMode.Normal, s.Mode);
        Assert.True(s.DockEnabled);
    }

    [Fact]
    public void Removed_options_in_old_files_are_ignored()
    {
        var path = File();
        System.IO.File.WriteAllText(path, "{ \"AutoHide\": false, \"TrayEnabled\": false, \"CloseToTray\": false, \"Language\": \"en\" }");
        var s = UserSettings.Load(path);
        Assert.Equal("en", s.Language);
        var json = s.ToJson();
        Assert.False(json.ContainsKey("AutoHide"));
        Assert.False(json.ContainsKey("TrayEnabled"));
        Assert.False(json.ContainsKey("CloseToTray"));
        using var shell = new ShellViewModel(s);
        Assert.True(shell.IsAutoHideActive); // fixed behavior: a docked bar always auto-hides
    }

    [Fact]
    public void All_fields_round_trip()
    {
        var path = File();
        var s = UserSettings.Load(path);
        s.Language = "zh-CN";
        s.Theme = ThemePreference.Dark;
        s.Mode = WindowMode.Compact;
        s.Topmost = true;
        s.DockEnabled = true;
        s.DockEdge = DockEdge.Top;
        s.DockPosition = 0.3;
        s.DockMonitor = @"\\.\DISPLAY2";
        s.BarSize = BarSize.Medium;
        s.FloatingOrientation = BarOrientation.Vertical;
        s.MainBounds = PixelRect.FromSize(-1500, 100, 900, 1000);
        s.CompactBounds = PixelRect.FromSize(10, 20, 300, 44);
        s.Save();

        var r = UserSettings.Load(path);
        Assert.Equal(s.ToJson().ToJsonString(), r.ToJson().ToJsonString());
        Assert.False(System.IO.File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Invalid_values_fall_back_individually()
    {
        var path = File();
        System.IO.File.WriteAllText(path, """
            {
              "Language": "ja",
              "Theme": "Purple",
              "Mode": 7,
              "DockEdge": "1",
              "DockPosition": "abc",
              "Topmost": "yes",
              "AutoHide": true,
              "MainBounds": { "Left": 0, "Top": 0, "Width": -5, "Height": 10 },
              "CompactBounds": { "Left": 999999999, "Top": 0, "Width": 5, "Height": 10 },
              "DockMonitor": 42
            }
            """);
        var s = UserSettings.Load(path);
        Assert.Equal("ja", s.Language);
        Assert.Equal(ThemePreference.System, s.Theme);
        Assert.Equal(WindowMode.Normal, s.Mode);
        Assert.Equal(DockEdge.Right, s.DockEdge); // numeric strings are not accepted as enum values
        Assert.Equal(0.5, s.DockPosition);
        Assert.False(s.Topmost);
        Assert.Null(s.MainBounds);
        Assert.Null(s.CompactBounds);
        Assert.Equal(string.Empty, s.DockMonitor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"Language\": \"ja\", \"Language\": \"en\" }")] // duplicate key (JsonObject throws ArgumentException)
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Corrupt_files_give_defaults(string content)
    {
        var path = File();
        System.IO.File.WriteAllText(path, content);
        var s = UserSettings.Load(path);
        Assert.Equal(WindowMode.Normal, s.Mode);
        Assert.Equal(string.Empty, s.Language);
    }

    [Fact]
    public void Shell_changes_are_persisted_immediately()
    {
        var path = File();
        using var shell = new ShellViewModel(UserSettings.Load(path));
        shell.IsTopmost = true;
        shell.DockEnabled = false;
        shell.IsCompact = true;
        shell.DockEdge = DockEdge.Left;
        shell.FloatingOrientation = BarOrientation.Vertical;

        var r = UserSettings.Load(path);
        Assert.True(r.Topmost);
        Assert.False(r.DockEnabled);
        Assert.Equal(BarOrientation.Vertical, r.FloatingOrientation);
        Assert.Equal(WindowMode.Compact, r.Mode);
        Assert.Equal(DockEdge.Left, r.DockEdge);
    }

    [Fact]
    public void Orientation_and_thickness_follow_dock_edge()
    {
        using var shell = new ShellViewModel(UserSettings.InMemory());
        Assert.True(shell.IsVertical); // default: docked right = vertical
        shell.DockEnabled = false;
        Assert.False(shell.IsVertical); // floating default: horizontal
        Assert.Equal(44, shell.BarThickness);

        shell.DockEdge = DockEdge.Left;
        shell.DockEnabled = true;
        Assert.True(shell.IsVertical);
        Assert.Equal(48, shell.BarThickness);
        Assert.Equal(System.Windows.Controls.Orientation.Vertical, shell.PanelOrientation);

        shell.BarSize = BarSize.Medium;
        Assert.Equal(56, shell.BarThickness);

        shell.DockEdge = DockEdge.Bottom;
        Assert.False(shell.IsVertical);
        Assert.Equal(52, shell.BarThickness);
    }

    [Fact]
    public void Bar_sizes_meet_spec_ranges()
    {
        using var shell = new ShellViewModel(UserSettings.InMemory());
        foreach (var size in new[] { BarSize.Small, BarSize.Medium })
        {
            shell.BarSize = size;
            shell.DockEnabled = false;
            Assert.InRange(shell.BarThickness, 44, 52); // horizontal: 44-52 DIP
            shell.DockEnabled = true;
            shell.DockEdge = DockEdge.Right;
            Assert.InRange(shell.BarThickness, 48, 60); // vertical: 48-60 DIP
        }

        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 3.0 })
        {
            Assert.InRange(DockLayout.HintThickness(scale), 3, 5); // visible strip of a hidden bar: 3-5 px
            Assert.InRange(DockLayout.TriggerDepth(scale), 12, 20); // expand trigger distance: 12-20 px
        }
    }

    [Fact]
    public void AutoHide_is_the_fixed_behavior_of_a_docked_bar()
    {
        using var shell = new ShellViewModel(UserSettings.InMemory());
        Assert.True(shell.IsAutoHideActive); // default: docked, so it auto-hides
        shell.DockEnabled = false;
        Assert.False(shell.IsAutoHideActive); // a floating bar never hides
        shell.DockEnabled = true;
        Assert.True(shell.IsAutoHideActive);
    }

    [Fact]
    public void Topmost_is_suspended_for_full_screen_apps()
    {
        using var shell = new ShellViewModel(UserSettings.InMemory());
        shell.IsTopmost = true;
        Assert.True(shell.EffectiveTopmost);
        shell.IsFullScreenAppActive = true;
        Assert.False(shell.EffectiveTopmost);
        Assert.True(shell.IsTopmost); // preference unchanged
        shell.IsFullScreenAppActive = false;
        Assert.True(shell.EffectiveTopmost);
    }

    [Fact]
    public void Commands_raise_requests()
    {
        using var shell = new ShellViewModel(UserSettings.InMemory());
        var shown = 0;
        var exited = 0;
        shell.ShowMainRequested += (_, _) => shown++;
        shell.ExitRequested += (_, _) => exited++;

        shell.EnterCompactCommand.Execute(null);
        Assert.True(shell.IsCompact);
        shell.ShowNormalCommand.Execute(null);
        Assert.False(shell.IsCompact);
        Assert.Equal(1, shown);

        shell.OpenOptionsCommand.Execute(null);
        Assert.True(shell.IsOptionsOpen);
        Assert.Equal(2, shown);

        shell.ToggleTopmostCommand.Execute(null);
        Assert.True(shell.IsTopmost);
        shell.ExitCommand.Execute(null);
        Assert.Equal(1, exited);
    }

    [Fact]
    public void Choices_are_localized()
    {
        using var shell = new ShellViewModel(UserSettings.InMemory());
        Loc.Instance.SetLanguage("ja");
        Assert.Equal("縦", shell.Orientations.Single(e => e.Value == BarOrientation.Vertical).DisplayName);
        Loc.Instance.SetLanguage("zh-CN");
        Assert.Equal("深色", shell.Themes.Single(e => e.Value == ThemePreference.Dark).DisplayName);
    }
}
