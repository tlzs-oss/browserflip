using System.Globalization;
using System.Resources;

namespace BrowserFlip.UI.Localization;

/// <summary>
/// UI strings, read from the product's existing <c>Strings.resx</c> / <c>Strings.ja.resx</c> /
/// <c>Strings.zh-CN.resx</c>. Those files are linked out of the WPF project, not copied, so the
/// three languages have a single source of truth shared by both UIs.
/// <para>
/// Text that exists only here - the capability table, the consent wording, the per-platform
/// mechanism description - must not be pushed into the shipping product's resources, so it lives
/// in this project's own <c>PrototypeStrings.resx</c> set and is looked up as a fallback. Both
/// sets carry the same three languages: nothing the prototype shows may be English-only.
/// </para>
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("BrowserFlip.UI.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>Keys the shipping product does not have, owned by the prototype.</summary>
    private static readonly ResourceManager PrototypeManager =
        new("BrowserFlip.UI.Localization.PrototypeStrings", typeof(Strings).Assembly);

    /// <summary>The languages the prototype offers (neutral plus the two translations).</summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["en", "ja", "zh-CN"];

    /// <summary>
    /// Returns the string for <paramref name="key"/> in the current UI culture, falling back to
    /// the neutral (English) value and finally to the key itself, so a missing key is visible
    /// rather than silently blank. The product's resources are consulted first and the
    /// prototype's own set second, so the prototype can never shadow a shared string.
    /// </summary>
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Lookup(Manager, key) ?? Lookup(PrototypeManager, key) ?? key;
    }

    /// <summary>
    /// Formats <paramref name="key"/>'s value with <paramref name="args"/> in the current
    /// culture. Centralised so no caller is tempted to concatenate an English literal onto a
    /// translated string.
    /// <para>
    /// A broken placeholder in one translation must degrade, never throw: a missing key already
    /// returns the key itself, which has no placeholders, and a translator who mistypes
    /// <c>{0}</c> would otherwise take down the window with a <see cref="FormatException"/>.
    /// The unformatted value plus the arguments is poor text; a crash is worse.
    /// </para>
    /// </summary>
    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args ?? []);
        }
        catch (FormatException)
        {
            return args is { Length: > 0 }
                ? template + " " + string.Join(", ", args.Select(a => a?.ToString() ?? string.Empty))
                : template;
        }
    }

    /// <summary>
    /// Every key the prototype's own resource set defines. Exists so a test can prove the three
    /// languages stay in step without that list being maintained by hand - a key added to the
    /// neutral file and forgotten in ja or zh-CN is exactly the defect this guards.
    /// </summary>
    internal static IReadOnlyList<string> PrototypeKeys()
    {
        try
        {
            var set = PrototypeManager.GetResourceSet(
                CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
            return set is null
                ? []
                : [.. set.Cast<System.Collections.DictionaryEntry>()
                    .Select(entry => (string)entry.Key)
                    .Order(StringComparer.Ordinal)];
        }
        catch (MissingManifestResourceException)
        {
            return [];
        }
    }

    private static string? Lookup(ResourceManager manager, string key)
    {
        try
        {
            return manager.GetString(key, CultureInfo.CurrentUICulture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
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
