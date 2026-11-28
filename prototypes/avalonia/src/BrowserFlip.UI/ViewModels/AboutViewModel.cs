using System.ComponentModel;
using System.Reflection;
using BrowserFlip.UI.Localization;

namespace BrowserFlip.UI.ViewModels;

/// <summary>
/// Backs the About window the macOS application menu's first item opens.
/// <para>
/// It exists because that item used to be Avalonia's own <c>About Avalonia</c>, which the Owner
/// reported: a product menu must not advertise its toolkit. The text is the shipping product's
/// own About resources - <c>About_Title</c>, <c>About_VersionLabel</c>, <c>About_License</c>,
/// <c>About_ThirdParty</c>, <c>Tagline</c> - in all three languages, so nothing new was written
/// and nothing here can drift from the Windows product's About box.
/// </para>
/// </summary>
public sealed class AboutViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public AboutViewModel(MainViewModel main)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));

        // Same reason as SettingsViewModel: the language is the main view model's, so a change
        // made on any window has to reach this one rather than leaving it in the old language.
        _main.PropertyChanged += OnMainPropertyChanged;
    }

    public string Title => Strings.Get("About_Title");

    public string ProductName => Strings.Get("AppTitle");

    /// <summary>"Version 0.3.0 (a707d75)" - the label is translated, the version is not.</summary>
    public string VersionLine => Strings.Get("About_VersionLabel") + " " + DisplayVersion;

    public string Tagline => Strings.Get("Tagline");

    public string License => Strings.Get("About_License");

    public string ThirdParty => Strings.Get("About_ThirdParty");

    public string WebsiteLabel => Strings.Get("About_Website");

    public string CloseLabel => Strings.Get("Settings_Close");

    // Instance members, not statics: Avalonia's compiled bindings resolve a path against the
    // x:DataType instance, so a static property would not bind.

    /// <summary>The project page, shown as text. Nothing here launches a browser.</summary>
    public string ProjectUrl => "https://github.com/tlzs-oss/browserflip";

    /// <summary>From the assembly, so it cannot disagree with what was built.</summary>
    public string Copyright =>
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? string.Empty;

    /// <summary>
    /// The informational version with the commit shortened: "0.3.0+a707d75…" becomes
    /// "0.3.0 (a707d75)". Same rule as the WPF product's About box.
    /// </summary>
    public string DisplayVersion => Shorten(
        typeof(AboutViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Stops following the main view model. Called when the window closes.</summary>
    public void Detach() => _main.PropertyChanged -= OnMainPropertyChanged;

    internal static string Shorten(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "?";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return informational;
        }

        var commit = informational[(plus + 1)..];
        return commit.Length == 0
            ? informational[..plus]
            : $"{informational[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Language))
        {
            return;
        }

        Raise(nameof(Title));
        Raise(nameof(ProductName));
        Raise(nameof(VersionLine));
        Raise(nameof(Tagline));
        Raise(nameof(License));
        Raise(nameof(ThirdParty));
        Raise(nameof(WebsiteLabel));
        Raise(nameof(CloseLabel));
    }
}
