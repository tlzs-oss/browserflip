using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using BrowserFlip.Core.Settings;

namespace BrowserFlip.App.ViewModels;

/// <summary>Severity of a message shown in the status area.</summary>
public enum MessageKind
{
    None,
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>A localizable message (resource key + format arguments).</summary>
public sealed record UiMessage(MessageKind Kind, string Key, params object?[] Args)
{
    public string Text => Args.Length == 0 ? Loc.Instance[Key] : Loc.Instance.Format(Key, Args);
}

/// <summary>A selectable UI language. Languages are shown in their own script; "auto" is localized.</summary>
public sealed class LanguageOption(string code, string nativeName) : ObservableObject
{
    public string Code { get; } = code;

    public string DisplayName => Code.Length == 0 ? Loc.Instance["Language_Auto"] : nativeName;

    public void Relocalize() => OnPropertyChanged(nameof(DisplayName));
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);
    internal static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(3);

    /// <summary>Extra wait when the user returns while only one of http/https has switched yet.</summary>
    internal static readonly TimeSpan PartialGrace = TimeSpan.FromSeconds(3);

    private readonly BrowserDetector _detector;
    private readonly DefaultBrowserService _defaults;
    private readonly SettingsLauncher _launcher;
    private readonly PolicyInspector _policy;
    private readonly Func<BrowserInfo, ImageSource?> _iconLoader;
    private readonly UserSettings _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer? _timer;

    private IReadOnlyList<BrowserInfo> _browsers = [];
    private DefaultBrowserState _state = DefaultBrowserState.Empty;
    private PolicyStatus _policyStatus = PolicyStatus.None;
    private ChangeTracker? _tracker;
    private DateTimeOffset _deadline;
    private bool _leftSinceLaunch;
    private bool _partialGraceUsed;
    private UiMessage? _message;
    private UiMessage? _guidance;
    private LanguageOption _selectedLanguage;

    public MainViewModel(
        BrowserDetector detector,
        DefaultBrowserService defaults,
        SettingsLauncher launcher,
        PolicyInspector policy,
        Func<BrowserInfo, ImageSource?> iconLoader,
        UserSettings settings,
        bool startTimer = true,
        Func<DateTimeOffset>? clock = null)
    {
        _detector = detector;
        _defaults = defaults;
        _launcher = launcher;
        _policy = policy;
        _iconLoader = iconLoader;
        _settings = settings;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        Languages =
        [
            new LanguageOption(string.Empty, string.Empty),
            new LanguageOption("en", "English"),
            new LanguageOption("ja", "日本語"),
            new LanguageOption("zh-CN", "简体中文"),
        ];
        _selectedLanguage = Languages.FirstOrDefault(l => string.Equals(l.Code, settings.Language, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];

        RefreshCommand = new RelayCommand(_ => Refresh());
        SelectBrowserCommand = new RelayCommand(p => Select(p as BrowserCardViewModel), p => p is BrowserCardViewModel);
        OpenDefaultAppsCommand = new RelayCommand(_ => OpenTopPage());

        if (startTimer)
        {
            _timer = new DispatcherTimer { Interval = PollInterval };
            _timer.Tick += (_, _) => Poll();
        }

        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    public ObservableCollection<BrowserCardViewModel> Browsers { get; } = [];

    public IReadOnlyList<LanguageOption> Languages { get; }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            _settings.Language = value.Code;
            _settings.Save();
            Loc.Instance.SetLanguage(value.Code);
        }
    }

    public ICommand RefreshCommand { get; }

    public ICommand SelectBrowserCommand { get; }

    public ICommand OpenDefaultAppsCommand { get; }

    public DefaultBrowserState State => _state;

    public bool IsMismatch => _state.Status == DefaultStatus.Mismatch;

    public string CurrentDefaultText => _state.Status switch
    {
        DefaultStatus.None => Loc.Instance["Current_None"],
        DefaultStatus.Consistent => Loc.Instance.Format("Current_Consistent", NameOf(_state.HttpBrowser, _state.Http)),
        DefaultStatus.Unrecognized => Loc.Instance.Format("Current_Unrecognized", NameOf(null, _state.Http)),
        _ => Loc.Instance.Format("Current_Mismatch", NameOf(_state.HttpBrowser, _state.Http), NameOf(_state.HttpsBrowser, _state.Https)),
    };

    public bool HasNoBrowsers => Browsers.Count == 0;

    public bool IsAssociationsManaged => _policyStatus.AssociationsManaged;

    public bool IsBlockedByPolicy => _policyStatus.BlocksNavigation;

    public bool IsWaitingForChange => _tracker is not null;

    public UiMessage? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(MessageText));
                OnPropertyChanged(nameof(MessageKind));
            }
        }
    }

    public string MessageText => _message?.Text ?? string.Empty;

    public MessageKind MessageKind => _message?.Kind ?? MessageKind.None;

    public string GuidanceText => _guidance?.Text ?? string.Empty;

    public void Refresh()
    {
        _browsers = _detector.Detect();
        _policyStatus = _policy.Inspect();
        _state = _defaults.GetState(_browsers);

        Browsers.Clear();
        foreach (var b in _browsers)
        {
            if (!_iconCache.TryGetValue(b.Id, out var icon))
            {
                icon = _iconLoader(b);
                _iconCache[b.Id] = icon;
            }

            Browsers.Add(new BrowserCardViewModel(b, icon, _state.RoleOf(b)));
        }

        RaiseStateChanged();
        OnPropertyChanged(nameof(HasNoBrowsers));
        OnPropertyChanged(nameof(IsAssociationsManaged));
        OnPropertyChanged(nameof(IsBlockedByPolicy));
        if (_policyStatus.BlocksNavigation)
        {
            Message = new UiMessage(MessageKind.Error, "Error_Blocked");
        }
        else if (_message?.Key == "Error_Blocked")
        {
            Message = null;
        }
    }

    /// <summary>Re-reads only the http/https defaults (cheap; used while polling).</summary>
    public void RefreshState()
    {
        _state = _defaults.GetState(_browsers);
        foreach (var card in Browsers)
        {
            card.Role = _state.RoleOf(card.Browser);
        }

        RaiseStateChanged();
    }

    public void Select(BrowserCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        StopTracking();
        var target = card.Browser;
        var before = _defaults.GetState(_browsers);
        var result = _launcher.Open(target);
        switch (result.Kind)
        {
            case LaunchKind.BrowserUnavailable:
                Message = new UiMessage(MessageKind.Error, "Error_BrowserMissing", target.DisplayName);
                return;
            case LaunchKind.BlockedByPolicy:
                Message = new UiMessage(MessageKind.Error, "Error_Blocked");
                return;
            case LaunchKind.Failed:
                Message = new UiMessage(MessageKind.Error, "Error_LaunchFailed", result.Error);
                return;
        }

        if (before.RoleOf(target) == BrowserDefaultRole.Both)
        {
            Message = new UiMessage(MessageKind.Info, "Status_AlreadyDefault", target.DisplayName);
            return;
        }

        _tracker = new ChangeTracker(target, before);
        _deadline = _clock() + WaitLimit;
        _leftSinceLaunch = false;
        _partialGraceUsed = false;
        _guidance = new UiMessage(MessageKind.Info,
            result.Kind == LaunchKind.OpenedAppPage ? "Guide_AppPage" : "Guide_TopPage", target.DisplayName);
        Message = new UiMessage(MessageKind.Info, "Guide_Waiting");
        OnPropertyChanged(nameof(GuidanceText));
        OnPropertyChanged(nameof(IsWaitingForChange));
        _timer?.Start();
    }

    /// <summary>Called periodically while waiting for the user to confirm the change in Settings.</summary>
    public void Poll()
    {
        if (_tracker is null)
        {
            _timer?.Stop();
            return;
        }

        RefreshState();
        Handle(_tracker.Evaluate(_state, userReturned: _clock() >= _deadline));
    }

    /// <summary>The main window was activated (user came back to BrowserFlip).</summary>
    public void OnActivated()
    {
        if (_tracker is not null && _leftSinceLaunch)
        {
            RefreshState();
            var outcome = _tracker.Evaluate(_state, userReturned: true);
            if (outcome == ChangeOutcome.PartiallyApplied && !_partialGraceUsed)
            {
                // Windows may still be writing the second protocol; let Poll() decide a moment later.
                _partialGraceUsed = true;
                var graceEnd = _clock() + PartialGrace;
                _deadline = graceEnd < _deadline ? graceEnd : _deadline;
                return;
            }

            Handle(outcome);
            return;
        }

        if (_tracker is null)
        {
            // F04: re-check whenever the app is shown again (changes may have been made elsewhere).
            RefreshState();
        }
    }

    /// <summary>The main window lost activation (typically because Settings opened).</summary>
    public void OnDeactivated()
    {
        if (_tracker is not null)
        {
            _leftSinceLaunch = true;
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
    }

    private void OpenTopPage()
    {
        StopTracking();
        var result = _launcher.OpenTopPage();
        Message = result.Kind switch
        {
            LaunchKind.BlockedByPolicy => new UiMessage(MessageKind.Error, "Error_Blocked"),
            LaunchKind.Failed => new UiMessage(MessageKind.Error, "Error_LaunchFailed", result.Error),
            _ => null,
        };
    }

    private void Handle(ChangeOutcome outcome)
    {
        if (outcome == ChangeOutcome.Pending || _tracker is null)
        {
            return;
        }

        var target = _tracker.Target;
        Message = outcome switch
        {
            ChangeOutcome.AppliedToTarget => new UiMessage(MessageKind.Success, "Status_Applied", target.DisplayName),
            ChangeOutcome.PartiallyApplied => new UiMessage(MessageKind.Warning, "Status_Partial", target.DisplayName),
            ChangeOutcome.ChangedToOther => new UiMessage(MessageKind.Info, "Status_ChangedOther", NameOf(_state.HttpsBrowser ?? _state.HttpBrowser, _state.Https ?? _state.Http)),
            _ => new UiMessage(MessageKind.Info, "Status_NoChange"),
        };
        StopTracking();
    }

    private void StopTracking()
    {
        _timer?.Stop();
        _tracker = null;
        _guidance = null;
        OnPropertyChanged(nameof(GuidanceText));
        OnPropertyChanged(nameof(IsWaitingForChange));
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsMismatch));
        OnPropertyChanged(nameof(CurrentDefaultText));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var card in Browsers)
        {
            card.Relocalize();
        }

        foreach (var language in Languages)
        {
            language.Relocalize();
        }

        OnPropertyChanged(nameof(CurrentDefaultText));
        OnPropertyChanged(nameof(MessageText));
        OnPropertyChanged(nameof(GuidanceText));
    }

    private static string NameOf(BrowserInfo? browser, AssociationInfo? assoc)
        => browser?.DisplayName
           ?? (string.IsNullOrWhiteSpace(assoc?.FriendlyAppName) ? null : assoc.FriendlyAppName)
           ?? assoc?.ProgId
           ?? Loc.Instance["Current_NotSet"];
}
