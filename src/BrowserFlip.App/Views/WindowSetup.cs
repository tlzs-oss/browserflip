using System.ComponentModel;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;

namespace BrowserFlip.App.Views;

/// <summary>
/// The same look as the main window for the v0.3 secondary windows (Settings, About): Fluent control styles in
/// light / dark, a dark title bar in the dark theme, and the per-language font (CJK glyph styles).
/// </summary>
internal static class WindowSetup
{
    public static void Prepare(Window window)
    {
        ApplyLanguage(window);
        ThemeManager.UseFluent(window);
        PropertyChangedEventHandler onLanguage = (_, _) => ApplyLanguage(window);
        Loc.Instance.PropertyChanged += onLanguage;
        window.Closed += (_, _) => Loc.Instance.PropertyChanged -= onLanguage;
        window.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(window);
    }

    private static void ApplyLanguage(Window window)
    {
        window.Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag);
        window.FontFamily = new FontFamily(Loc.Instance.FontFamilyName);
    }
}
