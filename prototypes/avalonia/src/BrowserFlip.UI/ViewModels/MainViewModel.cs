using System.Collections.ObjectModel;
using System.Globalization;
using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;
using BrowserFlip.UI.Localization;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.UI.ViewModels;

/// <summary>
/// The whole prototype's logic, with no reference to Avalonia (or WPF). It composes
/// <c>BrowserFlip.Core</c> — <see cref="DefaultBrowserService"/>, <see cref="DefaultBrowserState"/>
/// and <see cref="BrowserDefaultRole"/> are used exactly as the Windows product uses them — over
/// platform adapters supplied by the host.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IBrowserCatalog _catalog;

    /// <summary>
    /// Builds a switcher, given whether the user has consented. It is a factory rather than a
    /// single instance because a platform that applies the change with no OS prompt (Linux)
    /// must not be able to hold a pre-consented switcher: consent is stated at the moment it is
    /// obtained, and the type refuses to be constructed without that statement.
    /// </summary>
    private readonly Func<bool, IDefaultBrowserSwitcher> _switcherFactory;

    private readonly IPlatformCapabilities _capabilities;
    private readonly IUserConfirmation _confirmation;
    private IDefaultBrowserSwitcher? _probe;
    private readonly IAppIconProvider _icons;
    private readonly ISettingsStore _store;
    private readonly DefaultBrowserService _defaults;

    private DefaultBrowserState _state = DefaultBrowserState.Empty;
    private PrototypeSettings _settings = PrototypeSettings.Default;
    private string _statusMessage = string.Empty;
    private bool _busy;

    public MainViewModel(
        IBrowserCatalog catalog,
        Func<bool, IDefaultBrowserSwitcher> switcherFactory,
        IAppIconProvider icons,
        ISettingsStore store,
        DefaultBrowserService defaults,
        IPlatformCapabilities capabilities,
        IUserConfirmation? confirmation = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _switcherFactory = switcherFactory ?? throw new ArgumentNullException(nameof(switcherFactory));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        // Fail closed: with no prompt attached, a platform that needs one can never proceed.
        _confirmation = confirmation ?? NeverConfirms.Instance;
    }

    /// <summary>An unconsented switcher, safe to hold, used only to read platform metadata.</summary>
    private IDefaultBrowserSwitcher Probe => _probe ??= _switcherFactory(false);

    public ObservableCollection<BrowserItemViewModel> Browsers { get; } = [];

    public string Title => Strings.Get("AppTitle") + " — Avalonia prototype";

    public string SettingsLocation => _store.Location;

    /// <summary>
    /// Language choices for the UI, with "follow the OS" first. The view binds to these rather
    /// than to the raw codes, because the stored value for "follow the OS" is null and a null
    /// SelectedItem shows an empty combo box with no way back to it.
    /// </summary>
    public IReadOnlyList<LanguageChoice> LanguageChoices { get; } =
    [
        LanguageChoice.FollowSystem,
        .. Strings.SupportedLanguages.Select(code => new LanguageChoice(code)),
    ];

    /// <summary>The currently selected choice, kept in step with <see cref="Language"/>.</summary>
    public LanguageChoice SelectedLanguage
    {
        get => LanguageChoices.FirstOrDefault(c =>
                   string.Equals(c.Code, Language, StringComparison.OrdinalIgnoreCase))
               ?? LanguageChoice.FollowSystem;
        set => Language = value?.Code;
    }

    /// <summary>How this platform applies a change, for honest UI wording.</summary>
    public SwitchMechanism Mechanism => Probe.Mechanism;

    /// <summary>True when BrowserFlip itself must ask, because the OS will not.</summary>
    public bool RequiresOwnConfirmation => !_capabilities.OsConfirmsDefaultBrowserChange;

    /// <summary>
    /// Where consent for a change comes from, read off the object that is actually asked rather
    /// than re-derived, so a diagnostics report cannot claim a dialog that was never shown.
    /// </summary>
    public string ConsentSource => RequiresOwnConfirmation
        ? _confirmation.ConsentSource
        : "the operating system's own prompt";

    // The whole point of the capability model is that the UI disables what the platform cannot
    // do, rather than offering a control that silently does nothing. These drive IsEnabled.

    /// <summary>Whether "always on top" can actually work here (Wayland: no).</summary>
    public bool CanUseTopmost => _capabilities.AlwaysOnTop.IsAvailable;

    /// <summary>Why not, for the tooltip. Empty when it is available.</summary>
    public string TopmostUnavailableReason => _capabilities.AlwaysOnTop.Reason ?? string.Empty;

    /// <summary>The floating toolbar needs client-side positioning (Wayland: not allowed).</summary>
    public bool CanUseFloatingToolbar => _capabilities.WindowPositioning.IsAvailable;

    public string ToolbarUnavailableReason => _capabilities.WindowPositioning.Reason ?? string.Empty;

    public string MechanismDescription => Probe.Mechanism switch
    {
        SwitchMechanism.OpensSystemSettings => "Windows Settings opens; you confirm there.",
        SwitchMechanism.SystemConsentPrompt => "macOS shows its own confirmation prompt; you confirm there.",
        SwitchMechanism.CommandLineTool => "xdg-settings applies the change.",
        _ => "Changing the default browser is not supported on this system.",
    };

    public int LaunchCount => _settings.LaunchCount;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool Busy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }

    public string? Language
    {
        get => _settings.Language;
        set
        {
            if (string.Equals(_settings.Language, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings = _settings with { Language = value };
            Strings.UseLanguage(value);
            Persist();
            Raise();
            Raise(nameof(SelectedLanguage));
            // Rebuild the rows: their labels are read at construction, so a language change that
            // only raised the parent's properties would leave every button in the old language.
            Refresh();
        }
    }

    public bool Topmost
    {
        // Report false where the platform cannot honour it, so the checkbox does not sit ticked
        // over a window that is not actually on top.
        get => _settings.Topmost && CanUseTopmost;
        set
        {
            if (!CanUseTopmost || _settings.Topmost == value)
            {
                return;
            }

            _settings = _settings with { Topmost = value };
            Persist();
            Raise();
        }
    }

    public bool ShowFloatingToolbar
    {
        get => _settings.ShowFloatingToolbar && CanUseFloatingToolbar;
        set
        {
            if (!CanUseFloatingToolbar || _settings.ShowFloatingToolbar == value)
            {
                return;
            }

            _settings = _settings with { ShowFloatingToolbar = value };
            Persist();
            Raise();
        }
    }

    // ---- localized labels, so the whole window re-reads its text on a language change ----

    public string LabelBrowsers => Strings.Get("Section_Browsers");

    public string LabelRefresh => Strings.Get("Refresh");

    public string LabelLanguage => Strings.Get("Language");

    public string LabelLanguageAuto => Strings.Get("Language_Auto");

    public string LabelTopmost => Strings.Get("Menu_Topmost");

    public string LabelShowToolbar => Strings.Get("Menu_ShowToolbar");

    public string LabelSetDefault => Strings.Get("List_SetDefault");

    public string LabelBadgeDefault => Strings.Get("Badge_Default");

    public string LabelEmpty => Strings.Get("Empty_NoBrowsers");

    public string LabelSettingsFile => Strings.Get("Settings_SettingsFile");

    /// <summary>The headline line, built from the Core status enumeration.</summary>
    public string CurrentDefaultText => _state.Status switch
    {
        DefaultStatus.None => Strings.Get("Current_None"),
        DefaultStatus.Consistent => string.Format(
            CultureInfo.CurrentCulture, Strings.Get("Current_Consistent"), _state.HttpsBrowser?.DisplayName ?? "?"),
        DefaultStatus.Unrecognized => string.Format(
            CultureInfo.CurrentCulture, Strings.Get("Current_Consistent"), _state.Https?.FriendlyAppName ?? _state.Https?.ProgId ?? "?"),
        _ => string.Format(
            CultureInfo.CurrentCulture,
            Strings.Get("Current_Mismatch"),
            _state.Http?.FriendlyAppName ?? _state.Http?.ProgId ?? "-",
            _state.Https?.FriendlyAppName ?? _state.Https?.ProgId ?? "-"),
    };

    public DefaultStatus Status => _state.Status;

    /// <summary>Loads settings, applies the language, then reads browsers and the current default.</summary>
    public void Initialize()
    {
        _settings = _store.Load();
        _settings = _settings with { LaunchCount = _settings.LaunchCount + 1 };
        Persist();
        Strings.UseLanguage(_settings.Language);
        Raise(nameof(Language));
        Raise(nameof(Topmost));
        Raise(nameof(ShowFloatingToolbar));
        Raise(nameof(LaunchCount));
        Refresh();
    }

    /// <summary>Re-reads the installed browsers and the effective default.</summary>
    public void Refresh()
    {
        var browsers = _catalog.GetBrowsers();
        _state = _defaults.GetState(browsers);

        Browsers.Clear();
        foreach (var browser in browsers)
        {
            Browsers.Add(new BrowserItemViewModel(browser, _icons.GetIconPng(browser.IconPath))
            {
                Role = _state.RoleOf(browser),
            });
        }

        RaiseLocalizedText();
    }

    /// <summary>
    /// Asks the OS to make <paramref name="item"/> the default. The OS owns the confirmation, so
    /// this never changes the association by itself.
    /// </summary>
    public async Task SetDefaultAsync(BrowserItemViewModel item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanSetDefault)
        {
            return;
        }

        var before = _state;
        var tracker = new ChangeTracker(item.Browser, before);

        // Where the OS does not ask, BrowserFlip must. Asking BEFORE anything is constructed or
        // invoked means a declined prompt cannot leave a half-applied change behind.
        var consented = true;
        if (RequiresOwnConfirmation)
        {
            consented = await _confirmation.ConfirmAsync(
                Strings.Get("AppTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    "Make {0} the default browser?\n\nThis system applies the change immediately, "
                    + "without asking again, and BrowserFlip cannot undo it.",
                    item.DisplayName),
                cancellationToken).ConfigureAwait(true);
        }

        if (!consented)
        {
            StatusMessage = Strings.Get("Status_NoChange");
            return;
        }

        item.Busy = true;
        Busy = true;
        StatusMessage = MechanismDescription;
        try
        {
            // The switcher is built only now, with consent stated as a fact.
            var switcher = _switcherFactory(true);
            var result = await switcher.RequestSwitchAsync(item.Browser, cancellationToken).ConfigureAwait(true);
            Refresh();

            // The Core change tracker decides what actually happened, on every platform.
            var outcome = tracker.Evaluate(_state, userReturned: true);
            StatusMessage = Describe(result, outcome, item);
        }
        finally
        {
            item.Busy = false;
            Busy = false;
        }
    }

    private string Describe(SwitchResult result, ChangeOutcome outcome, BrowserItemViewModel item)
    {
        if (result.Outcome == SwitchOutcome.Unsupported)
        {
            return result.Error ?? MechanismDescription;
        }

        if (result.Outcome == SwitchOutcome.BrowserUnavailable)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.Get("Error_BrowserMissing"), item.DisplayName);
        }

        if (result.Outcome == SwitchOutcome.Failed)
        {
            return result.Error ?? "The request failed.";
        }

        // The OS accepted the request but had not applied it when we stopped polling: the prompt
        // may still be open. Saying "not changed" here would be wrong.
        if (result.Outcome == SwitchOutcome.Requested && outcome is ChangeOutcome.NoChange or ChangeOutcome.Pending)
        {
            return result.Error ?? Strings.Get("Guide_AppPage");
        }

        return outcome switch
        {
            ChangeOutcome.AppliedToTarget => string.Format(CultureInfo.CurrentCulture, Strings.Get("Status_Applied"), item.DisplayName),
            ChangeOutcome.PartiallyApplied => string.Format(CultureInfo.CurrentCulture, Strings.Get("Status_Partial"), item.DisplayName),
            ChangeOutcome.ChangedToOther => string.Format(
                CultureInfo.CurrentCulture, Strings.Get("Status_ChangedOther"), _state.HttpsBrowser?.DisplayName ?? "?"),
            _ => Strings.Get("Status_NoChange"),
        };
    }

    /// <summary>Stores the toolbar position (called when the user drags it).</summary>
    public void SaveToolbarPosition(double x, double y)
    {
        _settings = _settings with { ToolbarX = x, ToolbarY = y };
        Persist();
    }

    public (double X, double Y) ToolbarPosition => (_settings.ToolbarX, _settings.ToolbarY);

    private void Persist() => _store.Save(_settings);

    private void RaiseLocalizedText()
    {
        Raise(nameof(Title));
        Raise(nameof(CurrentDefaultText));
        Raise(nameof(Status));
        Raise(nameof(LabelBrowsers));
        Raise(nameof(LabelRefresh));
        Raise(nameof(LabelLanguage));
        Raise(nameof(LabelLanguageAuto));
        Raise(nameof(LabelTopmost));
        Raise(nameof(LabelShowToolbar));
        Raise(nameof(LabelSetDefault));
        Raise(nameof(LabelBadgeDefault));
        Raise(nameof(LabelEmpty));
        Raise(nameof(LabelSettingsFile));
    }
}
