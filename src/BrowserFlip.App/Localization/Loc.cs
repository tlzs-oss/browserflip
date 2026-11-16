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

    private CultureInfo _culture = Resolve(string.Empty);

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo Culture => _culture;

    public string this[string key] => Resources.GetString(key, _culture) ?? $"[{key}]";

    /// <summary>Formats the resource <paramref name="key"/> with <paramref name="args"/>.</summary>
    public string Format(string key, params object?[] args) => string.Format(_culture, this[key], args);

    /// <summary>Sets the UI language. Empty or unsupported values follow the Windows display language.</summary>
    public void SetLanguage(string? language)
    {
        _culture = Resolve(language);
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
    }

    internal static CultureInfo Resolve(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language) && SupportedCultures.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo(language);
        }

        var ui = CultureInfo.InstalledUICulture;
        if (ui.TwoLetterISOLanguageName == "ja")
        {
            return CultureInfo.GetCultureInfo("ja");
        }

        // Any Chinese UI (zh-CN, zh-Hans, zh-SG, zh-TW...) uses the Simplified Chinese resources in the MVP.
        if (ui.TwoLetterISOLanguageName == "zh")
        {
            return CultureInfo.GetCultureInfo("zh-CN");
        }

        return CultureInfo.GetCultureInfo("en");
    }
}
