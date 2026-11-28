using BrowserFlip.UI.Localization;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.UI.ViewModels;

/// <summary>One row of the platform capability table shown in settings.</summary>
public sealed class CapabilityRow
{
    public CapabilityRow(string name, Capability capability)
    {
        Name = name;
        StateText = capability.State switch
        {
            CapabilityState.Available => Strings.Get("Capability_Available"),
            CapabilityState.Unsupported => Strings.Get("Capability_Unsupported"),
            _ => Strings.Get("Capability_Unavailable"),
        };
        StateBrush = capability.State switch
        {
            CapabilityState.Available => "#0a7",
            CapabilityState.Unsupported => "#a44",
            _ => "#d80",
        };
        Reason = capability.Reason ?? string.Empty;
    }

    public string Name { get; }

    public string StateText { get; }

    /// <summary>A colour literal the view binds straight to a Brush.</summary>
    public string StateBrush { get; }

    public string Reason { get; }
}

/// <summary>
/// Backs the settings window. It wraps <see cref="MainViewModel"/> rather than duplicating its
/// state, so a change made here takes effect immediately and is persisted once, in one place.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IPlatformCapabilities _capabilities;

    public SettingsViewModel(MainViewModel main, IPlatformCapabilities capabilities)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Capabilities = BuildCapabilityRows();

        // Everything this view model shows is a view onto the main view model, so a change made
        // ANYWHERE has to reach it. Without this it only ever raised from its own setters, which
        // left two defects of the same shape: ticking "always on top" on the main window raised
        // the main window above this one again - the exact symptom this round set out to fix -
        // and changing the language on the main window left this whole window in the old one.
        _main.PropertyChanged += OnMainPropertyChanged;
    }

    /// <summary>
    /// Rebuilt on a language change: the rows cache their own text, so raising only the label
    /// properties would leave the whole table in the previous language.
    /// </summary>
    public IReadOnlyList<CapabilityRow> Capabilities { get; private set; }

    public string Title => Strings.Get("Settings_Title");

    public string PlatformDescription => _capabilities.Description;

    public string GeneralLabel => Strings.Get("Settings_General");

    public string AppearanceLabel => Strings.Get("Settings_Appearance");

    public string LanguageLabel => Strings.Get("Language");

    public string TopmostLabel => Strings.Get("Menu_Topmost");

    public string ToolbarLabel => Strings.Get("Menu_ShowToolbar");

    public string CloseLabel => Strings.Get("Settings_Close");

    public string SettingsFileLabel => Strings.Get("Settings_SettingsFile");

    public string SettingsLocation => _main.SettingsLocation;

    public string CapabilitiesLabel => Strings.Get("Settings_Capabilities");

    public string CapabilitiesHint => Strings.Get("Settings_CapabilitiesHint");

    /// <summary>Same choices as the main window, including "follow the OS".</summary>
    public IReadOnlyList<LanguageChoice> LanguageChoices => _main.LanguageChoices;

    public LanguageChoice SelectedLanguage
    {
        get => _main.SelectedLanguage;
        set
        {
            _main.SelectedLanguage = value;
            Raise();
            RaiseLabels();
        }
    }

    /// <summary>Whether this platform can keep a window above others (Wayland cannot).</summary>
    public bool CanUseTopmost => _capabilities.AlwaysOnTop.IsAvailable;

    public string TopmostReason => _capabilities.AlwaysOnTop.Reason ?? string.Empty;

    /// <summary>The floating toolbar needs client-side positioning, which Wayland does not allow.</summary>
    public bool CanUseToolbar => _capabilities.WindowPositioning.IsAvailable;

    public string ToolbarReason => _capabilities.WindowPositioning.Reason ?? string.Empty;

    public bool Topmost
    {
        get => _main.Topmost && CanUseTopmost;
        set
        {
            if (CanUseTopmost)
            {
                _main.Topmost = value;
                Raise();
            }
        }
    }

    public bool ShowFloatingToolbar
    {
        get => _main.ShowFloatingToolbar && CanUseToolbar;
        set
        {
            if (CanUseToolbar)
            {
                _main.ShowFloatingToolbar = value;
                Raise();
            }
        }
    }

    /// <summary>
    /// Stops following the main view model. The main view model outlives this one - it is the
    /// application's - so the window that owns this must say when it is finished with it.
    /// </summary>
    public void Detach() => _main.PropertyChanged -= OnMainPropertyChanged;

    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Topmost):
                Raise(nameof(Topmost));
                break;
            case nameof(MainViewModel.ShowFloatingToolbar):
                Raise(nameof(ShowFloatingToolbar));
                break;
            case nameof(MainViewModel.Language):
                RaiseLabels();
                Raise(nameof(SelectedLanguage));
                break;
            default:
                break;
        }
    }

    private IReadOnlyList<CapabilityRow> BuildCapabilityRows()
        => [.. _capabilities.All().Select(c => new CapabilityRow(c.Name, c.Capability))];

    private void RaiseLabels()
    {
        Capabilities = BuildCapabilityRows();
        Raise(nameof(Capabilities));
        Raise(nameof(CapabilitiesLabel));
        Raise(nameof(CapabilitiesHint));
        Raise(nameof(Title));
        Raise(nameof(GeneralLabel));
        Raise(nameof(AppearanceLabel));
        Raise(nameof(LanguageLabel));
        Raise(nameof(TopmostLabel));
        Raise(nameof(ToolbarLabel));
        Raise(nameof(CloseLabel));
        Raise(nameof(SettingsFileLabel));
    }
}
