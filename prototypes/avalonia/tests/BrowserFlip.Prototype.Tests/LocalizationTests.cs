using System.Globalization;
using BrowserFlip.UI.Localization;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// The prototype must show the three product languages, reading the very same .resx files the
/// Windows product ships. These tests prove the linked resources really resolve at run time.
/// </summary>
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
        Assert.Contains(Strings.Get("Refresh"), Strings.Get("Refresh"), StringComparison.Ordinal);
        Assert.True(Strings.Get("Refresh").Any(c => c > 0x2E80), "Japanese translation looks like ASCII.");

        Strings.UseLanguage("zh-CN");
        Assert.True(Strings.Get("Refresh").Any(c => c > 0x2E80), "Chinese translation looks like ASCII.");
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
}
