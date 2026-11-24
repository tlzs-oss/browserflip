using System.Globalization;
using System.Resources;

namespace BrowserFlip.UI.Localization;

/// <summary>
/// UI strings, read from the product's existing <c>Strings.resx</c> / <c>Strings.ja.resx</c> /
/// <c>Strings.zh-CN.resx</c>. Those files are linked out of the WPF project, not copied, so the
/// three languages have a single source of truth shared by both UIs.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("BrowserFlip.UI.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>The languages the prototype offers (neutral plus the two translations).</summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["en", "ja", "zh-CN"];

    /// <summary>
    /// Returns the string for <paramref name="key"/> in the current UI culture, falling back to
    /// the neutral (English) value and finally to the key itself, so a missing key is visible
    /// rather than silently blank.
    /// </summary>
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        try
        {
            return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }

    /// <summary>Applies <paramref name="language"/> ("en" / "ja" / "zh-CN") or the OS default when null.</summary>
    public static void UseLanguage(string? language)
    {
        var culture = string.IsNullOrWhiteSpace(language)
            ? CultureInfo.InstalledUICulture
            : CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
