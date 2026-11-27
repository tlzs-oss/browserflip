using BrowserFlip.UI.Localization;
using BrowserFlip.UI.Menus;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// Regression tests for the status-area menu, added after the Owner's macOS menu-bar acceptance
/// failed (MAC-D6). Two things were untestable before, and one of them was broken:
/// <list type="number">
///   <item>the labels, one of which ("Quit") was an English literal shown beside five translated
///   items in a Japanese menu bar;</item>
///   <item>the bindings - every handler was an inline lambda inside the native-menu construction,
///   so nothing could check that choosing an item does what the item says.</item>
/// </list>
/// These run on every platform and need no display, so CI covers them on Windows and Linux too.
/// </summary>
[Collection(SerialUiCulture.Name)]
public class StatusAreaMenuTests
{
    private static IReadOnlyList<StatusMenuEntry> Build(List<string> log)
        => StatusAreaMenu.Build(
            showMainWindow: () => log.Add("show"),
            refresh: () => log.Add("refresh"),
            toggleToolbar: () => log.Add("toolbar"),
            showSettings: () => log.Add("settings"),
            quit: () => log.Add("quit"));

    [Fact]
    public void The_menu_is_the_five_commands_the_owner_accepts_plus_one_separator()
    {
        var entries = Build([]);

        Assert.Equal(
            ["AppTitle", "Refresh", "Menu_ShowToolbar", "Menu_Settings", "-", "Menu_Exit"],
            entries.Select(e => e.Key));
        Assert.Equal(5, entries.Count(e => !e.IsSeparator));
        Assert.Single(entries, e => e.IsSeparator);
    }

    [Fact]
    public void Choosing_an_item_runs_that_item_and_nothing_else()
    {
        foreach (var (key, expected) in new[]
                 {
                     ("AppTitle", "show"), ("Refresh", "refresh"), ("Menu_ShowToolbar", "toolbar"),
                     ("Menu_Settings", "settings"), ("Menu_Exit", "quit"),
                 })
        {
            var log = new List<string>();
            var entry = Build(log).Single(e => e.Key == key);

            entry.Invoke!();

            // Both halves matter: the right thing ran, and it was the only thing that ran.
            Assert.Equal([expected], log);
        }
    }

    [Fact]
    public void The_separator_has_no_action_to_invoke()
    {
        var separator = Build([]).Single(e => e.IsSeparator);

        Assert.Null(separator.Invoke);
    }

    [Fact]
    public void Every_label_is_looked_up_rather_than_typed_in()
    {
        try
        {
            foreach (var language in Strings.SupportedLanguages)
            {
                Strings.UseLanguage(language);
                foreach (var entry in Build([]).Where(e => !e.IsSeparator))
                {
                    var label = entry.Label;
                    Assert.False(string.IsNullOrWhiteSpace(label), $"{language}/{entry.Key} has no label");

                    // Strings.Get returns the key itself when a resource does not resolve, which
                    // is how a mistyped or missing key would reach the menu bar.
                    Assert.NotEqual(entry.Key, label);
                }
            }
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [Fact]
    public void No_item_falls_back_to_english_in_japanese_or_chinese()
    {
        try
        {
            Strings.UseLanguage("en");
            var english = Build([]).Where(e => !e.IsSeparator).ToDictionary(e => e.Key, e => e.Label, StringComparer.Ordinal);

            foreach (var language in new[] { "ja", "zh-CN" })
            {
                Strings.UseLanguage(language);
                foreach (var entry in Build([]).Where(e => !e.IsSeparator))
                {
                    // "BrowserFlip" is the product name and is deliberately the same everywhere.
                    if (entry.Key == "AppTitle")
                    {
                        continue;
                    }

                    // This is the assertion the live menu bar would have failed before the fix:
                    // Menu_Exit read "Quit" in Japanese because it was a literal, not a lookup.
                    Assert.NotEqual(english[entry.Key], entry.Label);
                }
            }
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [Fact]
    public void The_application_menu_keys_are_translated_too()
    {
        try
        {
            Strings.UseLanguage("en");
            var english = StatusAreaMenu.ApplicationMenuKeys.ToDictionary(k => k, Strings.Get, StringComparer.Ordinal);

            foreach (var language in new[] { "ja", "zh-CN" })
            {
                Strings.UseLanguage(language);
                foreach (var key in StatusAreaMenu.ApplicationMenuKeys)
                {
                    Assert.NotEqual(key, Strings.Get(key));
                    Assert.NotEqual(english[key], Strings.Get(key));
                }
            }
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }
}
