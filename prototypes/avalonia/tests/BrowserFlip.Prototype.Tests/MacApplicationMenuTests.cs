using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using BrowserFlip.UI.Localization;
using BrowserFlip.UI.Menus;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// Regression tests for the macOS application menu - the menu under the product's name at the
/// left of the system menu bar. The Owner reported three defects in it, all of which these cover:
/// <list type="number">
///   <item>its first item was Avalonia's <c>About Avalonia</c>, not the product's own About;</item>
///   <item>it had no settings item at all;</item>
///   <item>it stayed in English when the UI was switched to Japanese.</item>
/// </list>
/// They run on every platform and need no display, so CI covers them on Windows and Linux too.
/// What they cannot cover is that Avalonia really exports the menu, which depends on it being
/// attached from <c>Initialize()</c>; that is measured on the device by self-test check 14.
/// </summary>
[Collection(SerialUiCulture.Name)]
public class MacApplicationMenuTests
{
    private const string AppName = "BrowserFlip";

    private static MacApplicationMenuDefinition Build(List<string> log)
        => MacApplicationMenu.Create(
            showAbout: () => log.Add("about"),
            showSettings: () => log.Add("settings"),
            applicationName: AppName);

    /// <summary>
    /// Appends what Avalonia's <c>PopulateStandardOSXMenuItems</c> appends when it exports the
    /// menu, with the same English literals and the same order its source uses. The real thing
    /// cannot run here - it needs the macOS backend - so the shape it produces is reproduced,
    /// and the live menu is checked against the real one by self-test check 14 on the device.
    /// </summary>
    private static void AppendAvaloniasStandardItems(NativeMenu menu, string applicationName)
    {
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(new NativeMenuItem("Services") { Menu = new NativeMenu() });
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(new NativeMenuItem("Hide " + applicationName)
        {
            Gesture = new KeyGesture(Key.H, KeyModifiers.Meta),
        });
        menu.Add(new NativeMenuItem("Hide Others")
        {
            // Avalonia really does type Q here.
            Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta | KeyModifiers.Alt),
        });
        menu.Add(new NativeMenuItem("Show All"));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(new NativeMenuItem("Quit") { Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta) });
    }

    private static List<NativeMenuItem> Commands(NativeMenu menu)
        => [.. menu.Items
            .Where(i => i is NativeMenuItem and not NativeMenuItemSeparator)
            .Cast<NativeMenuItem>()];

    [AvaloniaFact]
    public void It_contributes_the_products_own_about_and_a_settings_item_in_macos_order()
    {
        var definition = Build([]);

        Assert.Equal(["About_Title", "Menu_Settings"], definition.Items.Select(i => i.Key));
        Assert.Equal(2, definition.Menu.Items.Count);
        // About first, Settings second: the order every Mac application uses.
        Assert.Equal(
            definition.Items.Select(i => i.Item),
            Commands(definition.Menu));
    }

    [AvaloniaFact]
    public void The_settings_item_carries_the_macos_standard_shortcut()
    {
        var settings = Commands(Build([]).Menu).Last();

        // Command+, is what the Owner asked for by name, and what AppKit itself uses.
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), settings.Gesture);
    }

    [AvaloniaFact]
    public void Choosing_an_item_runs_that_item_and_nothing_else()
    {
        foreach (var (index, expected) in new[] { (0, "about"), (1, "settings") })
        {
            var log = new List<string>();
            var definition = Build(log);

            // RaiseClicked is the same bridge the platform exporters use to deliver a chosen item.
            ((Avalonia.Controls.INativeMenuItemExporterEventsImplBridge)definition.Menu.Items[index])
                .RaiseClicked();

            Assert.Equal([expected], log);
        }
    }

    [AvaloniaFact]
    public void Nothing_in_the_menu_names_the_toolkit()
    {
        try
        {
            foreach (var language in Strings.SupportedLanguages)
            {
                Strings.UseLanguage(language);
                var menu = Build([]).Menu;
                AppendAvaloniasStandardItems(menu, AppName);
                var items = MacApplicationMenu.Adopt(menu, AppName);
                MacApplicationMenu.Relabel([.. Build([]).Items, .. items], AppName);

                // "About Avalonia" is the exact item the Owner saw in BrowserFlip's menu bar.
                foreach (var item in Commands(menu))
                {
                    Assert.DoesNotContain("Avalonia", item.Header ?? string.Empty, StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [Fact]
    public void Every_label_is_looked_up_rather_than_typed_in()
    {
        try
        {
            var keys = new List<string> { MacApplicationMenu.AboutKey, MacApplicationMenu.SettingsKey };
            keys.AddRange(MacApplicationMenu.StandardItems(AppName).Select(s => s.Key));

            foreach (var language in Strings.SupportedLanguages)
            {
                Strings.UseLanguage(language);
                foreach (var key in keys)
                {
                    var label = MacApplicationMenu.Label(key, AppName);

                    Assert.False(string.IsNullOrWhiteSpace(label), $"{language}/{key} has no label");

                    // Strings.Get returns the key itself when a resource does not resolve, which
                    // is how a mistyped or missing key would reach the menu bar.
                    Assert.NotEqual(key, label);

                    // A native menu renders an access-key marker literally.
                    Assert.DoesNotContain('_', label);
                }
            }
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [Fact]
    public void No_label_stays_in_english_in_japanese_or_chinese()
    {
        try
        {
            var keys = new List<string> { MacApplicationMenu.AboutKey, MacApplicationMenu.SettingsKey };
            keys.AddRange(MacApplicationMenu.StandardItems(AppName).Select(s => s.Key));

            Strings.UseLanguage("en");
            var english = keys.ToDictionary(k => k, k => MacApplicationMenu.Label(k, AppName), StringComparer.Ordinal);

            foreach (var language in new[] { "ja", "zh-CN" })
            {
                Strings.UseLanguage(language);
                foreach (var key in keys)
                {
                    // This is the Owner's third complaint, asserted per item: an English literal
                    // in a Japanese menu bar. It covers Avalonia's five appended items too,
                    // which is where the English came from.
                    Assert.NotEqual(english[key], MacApplicationMenu.Label(key, AppName));
                }
            }
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [Fact]
    public void The_hide_and_quit_labels_carry_the_application_name()
    {
        try
        {
            Strings.UseLanguage("ja");

            Assert.Equal("BrowserFlip を隠す", MacApplicationMenu.Label("Menu_HideApp", AppName));
            Assert.Equal("BrowserFlip を終了", MacApplicationMenu.Label("Menu_QuitApp", AppName));

            // macOS says Quit where Windows says Exit, so the product's own Menu_Exit
            // ("Exit BrowserFlip") must not be what the application menu shows.
            Strings.UseLanguage("en");
            Assert.Equal("Quit BrowserFlip", MacApplicationMenu.Label("Menu_QuitApp", AppName));
            Assert.Equal("Exit BrowserFlip", Strings.Get("Menu_Exit"));
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [AvaloniaFact]
    public void It_takes_over_the_items_avalonia_appends_and_leaves_its_own_alone()
    {
        var definition = Build([]);
        AppendAvaloniasStandardItems(definition.Menu, AppName);

        var adopted = MacApplicationMenu.Adopt(definition.Menu, AppName);

        Assert.Equal(
            ["Menu_Services", "Menu_HideApp", "Menu_HideOthers", "Menu_ShowAll", "Menu_QuitApp"],
            adopted.Select(a => a.Key));
        // Its own two items are not adopted twice: App holds them from Create.
        Assert.DoesNotContain(definition.Items[0].Item, adopted.Select(a => a.Item));
        Assert.DoesNotContain(definition.Items[1].Item, adopted.Select(a => a.Item));
        // NativeMenuItemSeparator derives from NativeMenuItem, so it has to be excluded by type.
        Assert.DoesNotContain(adopted, a => a.Item is NativeMenuItemSeparator);
    }

    [AvaloniaFact]
    public void It_corrects_avalonias_wrong_hide_others_shortcut()
    {
        var definition = Build([]);
        AppendAvaloniasStandardItems(definition.Menu, AppName);
        var hideOthers = Commands(definition.Menu).Single(i => i.Header == "Hide Others");
        Assert.Equal(new KeyGesture(Key.Q, KeyModifiers.Meta | KeyModifiers.Alt), hideOthers.Gesture);

        MacApplicationMenu.Adopt(definition.Menu, AppName);

        // The macOS standard is Option+Command+H. Avalonia types Q, which is one keystroke from
        // Quit's Command+Q, and the Owner asked for standard macOS shortcuts.
        Assert.Equal(new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt), hideOthers.Gesture);
    }

    [AvaloniaFact]
    public void An_item_avalonia_has_renamed_is_left_alone_rather_than_mislabelled()
    {
        var definition = Build([]);
        definition.Menu.Add(new NativeMenuItem("Show Everything"));

        var adopted = MacApplicationMenu.Adopt(definition.Menu, AppName);

        // Matching on the header rather than the position is what makes this safe: an upstream
        // rename leaves one item in English - visible, still working, and caught by check 14 -
        // where position matching would have relabelled the wrong item.
        Assert.Empty(adopted);
        Assert.Equal("Show Everything", Commands(definition.Menu).Last().Header);
    }

    [AvaloniaFact]
    public void Relabelling_rewrites_every_label_into_the_new_language()
    {
        try
        {
            Strings.UseLanguage("zh-CN");
            var definition = Build([]);
            AppendAvaloniasStandardItems(definition.Menu, AppName);
            var items = new List<(NativeMenuItem Item, string Key)>(definition.Items);
            items.AddRange(MacApplicationMenu.Adopt(definition.Menu, AppName));
            MacApplicationMenu.Relabel(items, AppName);
            Assert.Contains("设置…", Commands(definition.Menu).Select(i => i.Header));

            Strings.UseLanguage("ja");
            MacApplicationMenu.Relabel(items, AppName);

            var headers = Commands(definition.Menu).Select(i => i.Header).ToList();
            Assert.Equal(
                ["BrowserFlip について", "設定…", "サービス", "BrowserFlip を隠す", "ほかを隠す", "すべてを表示", "BrowserFlip を終了"],
                headers);
            Assert.DoesNotContain("设置…", headers);
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [AvaloniaFact]
    public void Relabelling_keeps_the_separators_the_shortcuts_and_the_handlers()
    {
        var log = new List<string>();
        var definition = Build(log);
        AppendAvaloniasStandardItems(definition.Menu, AppName);
        var items = new List<(NativeMenuItem Item, string Key)>(definition.Items);
        items.AddRange(MacApplicationMenu.Adopt(definition.Menu, AppName));

        MacApplicationMenu.Relabel(items, AppName);

        // The items stay the same objects, so AppKit sees a title change rather than a menu
        // replacement - replacing an exported menu is the upstream crash of Avalonia#22287.
        Assert.IsType<NativeMenuItemSeparator>(definition.Menu.Items[2]);
        Assert.Equal(
            new KeyGesture(Key.OemComma, KeyModifiers.Meta),
            Commands(definition.Menu)[1].Gesture);
        ((Avalonia.Controls.INativeMenuItemExporterEventsImplBridge)definition.Menu.Items[1]).RaiseClicked();
        Assert.Equal(["settings"], log);
    }
}
