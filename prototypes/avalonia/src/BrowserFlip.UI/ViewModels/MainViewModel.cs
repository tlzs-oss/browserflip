using System.Collections.ObjectModel;
using System.Diagnostics;
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

    /// <summary>Incremented per requested change, so a stale background watch stays silent.</summary>
    private int _switchGeneration;

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

    /// <summary>
    /// How this platform applies the change, in the UI language. These were English literals,
    /// which put an English sentence under the heading of an otherwise Japanese window.
    /// </summary>
    public string MechanismDescription => Strings.Get(Probe.Mechanism switch
    {
        SwitchMechanism.OpensSystemSettings => "Mech_OpensSystemSettings",
        SwitchMechanism.SystemConsentPrompt => "Mech_SystemConsentPrompt",
        SwitchMechanism.CommandLineTool => "Mech_CommandLineTool",
        _ => "Mech_Unsupported",
    });

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
                Strings.Format("Confirm_SetDefaultImmediate", item.DisplayName),
                cancellationToken).ConfigureAwait(true);
        }

        if (!consented)
        {
            StatusMessage = Strings.Get("Status_NoChange");
            return;
        }

        item.Busy = true;
        Busy = true;
        // A watch left over from an earlier request must not speak for this one: it would
        // overwrite the newer message with an older browser's name minutes later.
        var generation = ++_switchGeneration;
        // While the request is outstanding the honest line is that the OS is asking, not that
        // anything has happened yet.
        StatusMessage = Probe.Mechanism == SwitchMechanism.SystemConsentPrompt
            ? Strings.Format("Status_AwaitingConsent", item.DisplayName)
            : MechanismDescription;
        try
        {
            // The switcher is built only now, with consent stated as a fact.
            var switcher = _switcherFactory(true);
            var result = await switcher.RequestSwitchAsync(item.Browser, cancellationToken).ConfigureAwait(true);
            LastSwitchResult = result;
            Refresh();

            // The Core change tracker decides what actually happened, on every platform.
            var outcome = tracker.Evaluate(_state, userReturned: true);
            StatusMessage = Describe(result, outcome, item);

            // A request the OS has not finished with is not over. Keep watching in the
            // background so the window stops showing "still pending" the moment the user
            // answers the system dialog - measured at 44 s and at 474 s on this Mac, long
            // after any wait a button press may block on.
            PendingChange = result.StillPending && outcome is ChangeOutcome.NoChange or ChangeOutcome.Pending
                ? WatchPendingChangeAsync(item, generation, cancellationToken)
                : Task.CompletedTask;
        }
        finally
        {
            item.Busy = false;
            Busy = false;
        }
    }

    /// <summary>
    /// The background watch left running by the last still-pending change, so an unattended
    /// run (the self-test) can wait for the real answer instead of grading a timeout.
    /// Completed when nothing is pending.
    /// </summary>
    public Task PendingChange { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// What the switcher reported for the last requested change, including what the OS
    /// answered when the live association was read back. Diagnostics quote this rather than
    /// re-deriving a verdict of their own.
    /// </summary>
    public SwitchResult? LastSwitchResult { get; private set; }

    /// <summary>How long to keep watching an unanswered OS consent dialog.</summary>
    internal TimeSpan PendingWatchBudget { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How often the live association is re-read while watching.</summary>
    internal TimeSpan PendingWatchInterval { get; set; } = TimeSpan.FromSeconds(2);

    private async Task WatchPendingChangeAsync(
        BrowserItemViewModel item, int generation, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed < PendingWatchBudget && generation == _switchGeneration)
            {
                await Task.Delay(PendingWatchInterval, cancellationToken).ConfigureAwait(true);

                // Only the two association queries, not a full catalogue re-scan with icons:
                // this runs every couple of seconds for up to ten minutes.
                if (_defaults.GetState([.. Browsers.Select(b => b.Browser)]).RoleOf(item.Browser)
                    != BrowserDefaultRole.Both)
                {
                    continue;
                }

                if (generation != _switchGeneration)
                {
                    return;
                }

                Refresh();
                StatusMessage = Strings.Format("Status_Applied", item.DisplayName);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (generation != _switchGeneration)
        {
            return;
        }

        // Still nothing. The dialog may well still be open - it outlives this process - so the
        // only true statement is that BrowserFlip has stopped looking, not that it failed.
        StatusMessage = Strings.Format("Status_PendingUnknown", item.DisplayName);
    }

    /// <summary>
    /// Turns an outcome into the sentence shown to the user, in the UI language.
    /// <para>
    /// Two rules hold everywhere here. A platform's raw <see cref="SwitchResult.Error"/> is
    /// never the sentence - it is machine text in whatever language the OS produced it, so it
    /// is attached as a labelled technical detail. And a request the OS has not finished with
    /// is never worded as a failure or as "not changed": those are different facts.
    /// </para>
    /// </summary>
    private string Describe(SwitchResult result, ChangeOutcome outcome, BrowserItemViewModel item)
    {
        switch (result.Outcome)
        {
            case SwitchOutcome.Unsupported:
                return WithDetail(Strings.Get("Mech_Unsupported"), result.Error);
            case SwitchOutcome.BrowserUnavailable:
                return Strings.Format("Error_BrowserMissing", item.DisplayName);
            case SwitchOutcome.Failed:
                return string.IsNullOrWhiteSpace(result.Error)
                    ? Strings.Get("Status_RequestFailedUnknown")
                    : Strings.Format("Status_RequestFailed", result.Error);
            case SwitchOutcome.Declined:
                return WithDetail(Strings.Get("Status_Declined"), result.Error);
            default:
                break;
        }

        // Everything below is decided by what the OS actually holds now, read back by Refresh()
        // and evaluated by Core's ChangeTracker.
        if (outcome == ChangeOutcome.AppliedToTarget)
        {
            return Strings.Format("Status_Applied", item.DisplayName);
        }

        if (outcome == ChangeOutcome.PartiallyApplied)
        {
            return result.StillPending
                ? Strings.Format("Status_SecondProtocol", item.DisplayName)
                : Strings.Format("Status_Partial", item.DisplayName);
        }

        if (result.Outcome == SwitchOutcome.Applied)
        {
            // The switcher read the live association back and it IS the target. Core saw no
            // delta only because it already was the default before the request.
            return Strings.Format("Status_Applied", item.DisplayName);
        }

        if (result.StillPending)
        {
            return result.Outcome switch
            {
                SwitchOutcome.AwaitingConsent => Strings.Format("Status_AwaitingConsent", item.DisplayName),
                SwitchOutcome.TimedOutPending => Strings.Format("Status_StillPending", item.DisplayName),
                // Windows: the user was handed off to the Settings app and has not come back.
                _ => Strings.Format("Guide_AppPage", item.DisplayName),
            };
        }

        return outcome == ChangeOutcome.ChangedToOther
            ? Strings.Format("Status_ChangedOther", _state.HttpsBrowser?.DisplayName ?? "?")
            : Strings.Get("Status_NoChange");
    }

    /// <summary>Appends platform text as an explicitly labelled technical detail, never as the message.</summary>
    private static string WithDetail(string message, string? detail)
        => string.IsNullOrWhiteSpace(detail) ? message : Strings.Format("Detail_Technical", message, detail);

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
