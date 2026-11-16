using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace BrowserFlip.App.Localization;

/// <summary>
/// Runtime-switchable localized strings. XAML binds with
/// <c>{Binding [Key], Source={x:Static loc:Loc.Instance}}</c>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>Supported UI cultures. Empty string = follow Windows.</summary>
    public static readonly IReadOnlyList<string> SupportedCultures = ["en", "ja", "zh-CN"];

    private static readonly ResourceManager Resources = new("BrowserFlip.App.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>
    /// The user's Windows display language, captured before BrowserFlip overrides
    /// <see cref="CultureInfo.CurrentUICulture"/> (which starts as the user's UI language,
    /// unlike <see cref="CultureInfo.InstalledUICulture"/>, the OS install language).
    /// </summary>
    private static readonly CultureInfo UserUiCulture = CultureInfo.CurrentUICulture;

    private CultureInfo _culture;

    private Loc() => _culture = Resolve(string.Empty, UserUiCulture);

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo Culture => _culture;

    /// <summary>Font stack for the current language (keeps Han characters in the right regional glyph style).</summary>
    public string FontFamilyName => FontFamilyFor(_culture);

    public string this[string key] => Resources.GetString(key, _culture) ?? $"[{key}]";

    /// <summary>Formats the resource <paramref name="key"/> with <paramref name="args"/>.</summary>
    public string Format(string key, params object?[] args) => string.Format(_culture, this[key], args);

    /// <summary>Sets the UI language. Empty or unsupported values follow the Windows display language.</summary>
    public void SetLanguage(string? language)
    {
        _culture = Resolve(language, UserUiCulture);
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FontFamilyName)));
    }

    internal static CultureInfo Resolve(string? language, CultureInfo userUiCulture)
    {
        if (!string.IsNullOrWhiteSpace(language) && SupportedCultures.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo(language);
        }

        if (userUiCulture.TwoLetterISOLanguageName == "ja")
        {
            return CultureInfo.GetCultureInfo("ja");
        }

        // Any Chinese UI (zh-CN, zh-Hans, zh-SG, zh-TW...) uses the Simplified Chinese resources in the MVP.
        if (userUiCulture.TwoLetterISOLanguageName == "zh")
        {
            return CultureInfo.GetCultureInfo("zh-CN");
        }

        return CultureInfo.GetCultureInfo("en");
    }

    internal static string FontFamilyFor(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "ja" => "Segoe UI Variable Text, Segoe UI, Yu Gothic UI, Meiryo UI",
        "zh" => "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI",
        _ => "Segoe UI Variable Text, Segoe UI",
    };
}
