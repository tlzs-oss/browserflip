using System.Globalization;
using BrowserFlip.Core.Defaults;
using BrowserFlip.UI.Localization;
using BrowserFlip.UI.Platform;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// The prototype must show the three product languages, reading the very same .resx files the
/// Windows product ships. These tests prove the linked resources really resolve at run time.
/// </summary>
[Collection(SerialUiCulture.Name)]
public class LocalizationTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("zh-CN")]
    public void Every_supported_language_resolves_a_known_key(string language)
    {
        Strings.UseLanguage(language);
        var value = Strings.Get("Refresh");
        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.NotEqual("Refresh", value); // the key itself would mean "not found"
    }

    [Fact]
    public void The_three_languages_give_three_different_translations()
    {
        var values = new List<string>();
        foreach (var language in Strings.SupportedLanguages)
        {
            Strings.UseLanguage(language);
            values.Add(Strings.Get("Refresh"));
        }

        Assert.Equal(3, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Japanese_and_chinese_really_contain_non_latin_text()
    {
        Strings.UseLanguage("ja");
        Assert.True(Strings.Get("Refresh").Any(c => c > 0x2E80), "Japanese translation looks like ASCII.");

        Strings.UseLanguage("zh-CN");
        Assert.True(Strings.Get("Refresh").Any(c => c > 0x2E80), "Chinese translation looks like ASCII.");
    }

    [Fact]
    public void Every_label_shown_on_a_browser_row_is_translated()
    {
        // Regression: the row's "Set as default" button and "default" badge were hardcoded
        // English in the XAML, so a Japanese or Chinese user saw English inside an otherwise
        // translated window. Acceptance on the installed .app caught it.
        var row = new BrowserItemViewModel(
            Browsers.Mac("org.mozilla.firefox", "Firefox"), iconPng: null)
        { Role = BrowserDefaultRole.Both };

        var seen = new List<string>();
        foreach (var language in Strings.SupportedLanguages)
        {
            Strings.UseLanguage(language);
            var button = row.SetDefaultLabel;
            var badge = row.DefaultBadgeLabel;
            Assert.False(string.IsNullOrWhiteSpace(button));
            Assert.False(string.IsNullOrWhiteSpace(badge));
            Assert.NotEqual("List_SetDefault", button);
            Assert.NotEqual("Badge_Default", badge);
            seen.Add(button);
        }

        // Three languages must give three different button captions.
        Assert.Equal(3, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_partial_badge_names_the_protocol_it_handles()
    {
        // Protocol names are the same in every language, which is why this one is not a resource.
        var browser = Browsers.Mac("org.mozilla.firefox", "Firefox");
        Assert.Equal("HTTP", new BrowserItemViewModel(browser, null) { Role = BrowserDefaultRole.HttpOnly }.PartialBadgeLabel);
        Assert.Equal("HTTPS", new BrowserItemViewModel(browser, null) { Role = BrowserDefaultRole.HttpsOnly }.PartialBadgeLabel);
        Assert.Empty(new BrowserItemViewModel(browser, null) { Role = BrowserDefaultRole.Both }.PartialBadgeLabel);
    }

    [Fact]
    public void An_unknown_key_returns_the_key_instead_of_throwing()
    {
        Strings.UseLanguage("en");
        Assert.Equal("NoSuchKey_R2", Strings.Get("NoSuchKey_R2"));
    }

    [Fact]
    public void A_null_language_follows_the_operating_system()
    {
        Strings.UseLanguage(null);
        Assert.Equal(CultureInfo.InstalledUICulture.Name, CultureInfo.CurrentUICulture.Name);
    }

    [Theory]
    [InlineData("AppTitle")]
    [InlineData("Section_Browsers")]
    [InlineData("Current_None")]
    [InlineData("Current_Consistent")]
    [InlineData("Badge_Default")]
    [InlineData("Empty_NoBrowsers")]
    [InlineData("Menu_Topmost")]
    [InlineData("Menu_ShowToolbar")]
    [InlineData("List_SetDefault")]
    [InlineData("Settings_SettingsFile")]
    [InlineData("Status_Applied")]
    [InlineData("Status_NoChange")]
    public void Every_key_the_prototype_uses_exists_in_all_three_languages(string key)
    {
        foreach (var language in Strings.SupportedLanguages)
        {
            Strings.UseLanguage(language);
            var value = Strings.Get(key);
            Assert.False(string.IsNullOrWhiteSpace(value), $"{key} is empty in {language}");
            Assert.NotEqual(key, value);
        }
    }

    // ---- the prototype's OWN resources: the strings that used to be English literals ----

    [Fact]
    public void The_prototype_keeps_its_own_strings_out_of_the_shipping_products_resx()
    {
        // The product's Strings.resx is linked in unchanged and must stay the single source of
        // truth for the shared UI. Prototype-only wording lives in PrototypeStrings.resx.
        var keys = Strings.PrototypeKeys();
        Assert.NotEmpty(keys);
        Assert.Contains("Mech_SystemConsentPrompt", keys);
        Assert.Contains("Status_StillPending", keys);
        Assert.Contains("Capability_AlwaysOnTop", keys);
    }

    [Fact]
    public void Every_prototype_only_key_resolves_in_all_three_languages()
    {
        foreach (var key in Strings.PrototypeKeys())
        {
            foreach (var language in Strings.SupportedLanguages)
            {
                Strings.UseLanguage(language);
                var value = Strings.Get(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{key} is empty in {language}");
                Assert.NotEqual(key, value);
            }
        }

        Strings.UseLanguage("en");
    }

    [Fact]
    public void Every_prototype_only_string_is_really_translated_rather_than_copied_english()
    {
        foreach (var key in Strings.PrototypeKeys())
        {
            Strings.UseLanguage("en");
            var english = Strings.Get(key);
            Strings.UseLanguage("ja");
            var japanese = Strings.Get(key);
            Strings.UseLanguage("zh-CN");
            var chinese = Strings.Get(key);

            Assert.NotEqual(english, japanese);
            Assert.NotEqual(english, chinese);
            Assert.NotEqual(japanese, chinese);
            Assert.True(japanese.Any(c => c > 0x2E80), $"{key}: the Japanese value looks like ASCII.");
            Assert.True(chinese.Any(c => c > 0x2E80), $"{key}: the Chinese value looks like ASCII.");
        }

        Strings.UseLanguage("en");
    }

    [Fact]
    public void A_format_placeholder_survives_every_translation()
    {
        // A translator dropping {0} would turn a status line into a sentence with no browser
        // name in it, which string.Format does not complain about.
        foreach (var key in Strings.PrototypeKeys())
        {
            Strings.UseLanguage("en");
            var placeholders = Strings.Get(key).Count(c => c == '{');
            foreach (var language in Strings.SupportedLanguages)
            {
                Strings.UseLanguage(language);
                Assert.Equal(placeholders, Strings.Get(key).Count(c => c == '{'));
            }
        }

        Strings.UseLanguage("en");
    }

    [Fact]
    public void The_capability_table_is_translated_down_to_the_state_column()
    {
        // Regression: the four capability names and the "available / unsupported /
        // unavailable" column were English literals, inside an otherwise translated window.
        var capabilities = new FullCapabilities("test platform", osConfirms: true);
        var seen = new List<string>();
        foreach (var language in Strings.SupportedLanguages)
        {
            Strings.UseLanguage(language);
            var rows = capabilities.All().Select(c => new CapabilityRow(c.Name, c.Capability)).ToList();
            Assert.Equal(4, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.False(string.IsNullOrWhiteSpace(row.Name));
                Assert.False(string.IsNullOrWhiteSpace(row.StateText));
                Assert.DoesNotContain("Capability_", row.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("Capability_", row.StateText, StringComparison.Ordinal);
            });
            seen.Add(string.Join("|", rows.Select(r => $"{r.Name}={r.StateText}")));
        }

        Assert.Equal(3, seen.Distinct(StringComparer.Ordinal).Count());
        Strings.UseLanguage("en");
    }

    [Fact]
    public void The_settings_capability_heading_and_hint_are_translated()
    {
        var seen = new List<string>();
        foreach (var language in Strings.SupportedLanguages)
        {
            Strings.UseLanguage(language);
            seen.Add(Strings.Get("Settings_Capabilities") + "/" + Strings.Get("Settings_CapabilitiesHint"));
        }

        Assert.Equal(3, seen.Distinct(StringComparer.Ordinal).Count());
        Strings.UseLanguage("en");
    }

    [Fact]
    public void A_missing_key_degrades_to_the_key_in_every_language_instead_of_throwing()
    {
        // The prototype's resources are consulted as a fallback behind the product's, so a
        // lookup can miss both. Missing text must show the key, never take the window down -
        // and the same holds on Linux and Windows, which share this UI.
        foreach (var language in Strings.SupportedLanguages)
        {
            Strings.UseLanguage(language);
            Assert.Equal("NoSuchPrototypeKey_R3R4", Strings.Get("NoSuchPrototypeKey_R3R4"));
            Assert.Equal("NoSuchPrototypeKey_R3R4", Strings.Format("NoSuchPrototypeKey_R3R4", "Firefox"));
        }

        Strings.UseLanguage("en");
    }

    [Fact]
    public void No_translation_can_throw_while_it_is_being_formatted()
    {
        // A translator who mistypes a brace would otherwise turn one status line into a
        // FormatException in the middle of a UI action.
        foreach (var key in Strings.PrototypeKeys())
        {
            foreach (var language in Strings.SupportedLanguages)
            {
                Strings.UseLanguage(language);
                Assert.False(string.IsNullOrWhiteSpace(Strings.Format(key, "Firefox", "detail")));
            }
        }

        Strings.UseLanguage("en");
    }
}
