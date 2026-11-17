using System.ComponentModel;
using System.Windows.Input;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;

namespace BrowserFlip.App.ViewModels;

/// <summary>Pages of the Settings window.</summary>
public enum SettingsPage
{
    General,
    Appearance,
    FloatingToolbar,
    Startup,
    Advanced,
}

/// <summary>A localized choice for a combo box.</summary>
public sealed class Choice<T>(T value, string resourceKey) : ObservableObject
{
    public T Value { get; } = value;

    public string DisplayName => Loc.Instance[resourceKey];

    public void Relocalize() => OnPropertyChanged(nameof(DisplayName));
}

/// <summary>
/// Window-level state shared by the main window, the compact bar and the tray menu:
/// always-on-top, mode, docking, auto-hide, theme and tray options. Every change is persisted.
/// Window manipulation is done by <c>DesktopCoordinator</c>, which observes this view model.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly UserSettings _settings;
    private bool _isBarCollapsed;
    private bool _isOptionsOpen;
    private bool _isFullScreenAppActive;

    public ShellViewModel(UserSettings settings)
    {
        _settings = settings;
        Themes =
        [
            new(ThemePreference.System, "Theme_System"),
            new(ThemePreference.Light, "Theme_Light"),
            new(ThemePreference.Dark, "Theme_Dark"),
        ];
        BarSizes =
        [
            new(BarSize.Small, "BarSize_Small"),
            new(BarSize.Medium, "BarSize_Medium"),
        ];
        Orientations =
        [
            new(BarOrientation.Horizontal, "Orientation_Horizontal"),
            new(BarOrientation.Vertical, "Orientation_Vertical"),
        ];
        Views =
        [
            new(MainView.Classic, "View_Classic_Name"),
            new(MainView.List, "View_List_Name"),
            new(MainView.Card, "View_Card_Name"),
            new(MainView.Compact, "View_Compact_Name"),
            new(MainView.Panel, "View_Panel_Name"),
        ];

        ToggleTopmostCommand = new RelayCommand(_ => IsTopmost = !IsTopmost);
        EnterCompactCommand = new RelayCommand(_ =>
        {
            // The coordinator places the bar next to the main window (BF-03); without one, just switch.
            if (EnterCompactRequested is { } handler)
            {
                handler(this, EventArgs.Empty);
            }
            else
            {
                IsCompact = true;
            }
        });
        ShowNormalCommand = new RelayCommand(_ =>
        {
            IsCompact = false;
            ShowMainRequested?.Invoke(this, EventArgs.Empty);
        });
        ToggleCompactCommand = new RelayCommand(_ => IsCompact = !IsCompact);
        OpenOptionsCommand = new RelayCommand(_ =>
        {
            IsCompact = false;
            IsOptionsOpen = true;
            ShowMainRequested?.Invoke(this, EventArgs.Empty);
        });
        ExitCommand = new RelayCommand(_ => ExitRequested?.Invoke(this, EventArgs.Empty));
        SetViewCommand = new RelayCommand(p =>
        {
            if (ParseView(p) is { } view)
            {
                CurrentView = view;
            }
        });
        OpenSettingsCommand = new RelayCommand(p => SettingsRequested?.Invoke(this, ParseSettingsPage(p)));
        OpenAboutCommand = new RelayCommand(_ => AboutRequested?.Invoke(this, EventArgs.Empty));
        TrayModeCommand = new RelayCommand(_ => TrayModeRequested?.Invoke(this, EventArgs.Empty));

        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>The user asked to switch from the main window to the compact bar (compact button).</summary>
    public event EventHandler? EnterCompactRequested;

    /// <summary>The main window should be shown and activated.</summary>
    public event EventHandler? ShowMainRequested;

    /// <summary>The application should exit (explicit "Exit").</summary>
    public event EventHandler? ExitRequested;

    /// <summary>The Settings window should be shown (one window shared by every view, the tray and the toolbar).</summary>
    public event EventHandler<SettingsPage>? SettingsRequested;

    /// <summary>The About window should be shown.</summary>
    public event EventHandler? AboutRequested;

    /// <summary>Tray mode: hide every window to the notification area (the same as X on the main window).</summary>
    public event EventHandler? TrayModeRequested;

    public UserSettings Settings => _settings;

    public IReadOnlyList<Choice<ThemePreference>> Themes { get; }

    public IReadOnlyList<Choice<BarSize>> BarSizes { get; }

    public IReadOnlyList<Choice<BarOrientation>> Orientations { get; }

    public IReadOnlyList<Choice<MainView>> Views { get; }

    /// <summary>Switches the main window view (parameter: a <see cref="MainView"/> or its name).</summary>
    public ICommand SetViewCommand { get; }

    /// <summary>Opens the Settings window (parameter: optional <see cref="SettingsPage"/> or its name).</summary>
    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenAboutCommand { get; }

    public ICommand TrayModeCommand { get; }

    /// <summary>Main window layout; every change is saved and applied at once (no restart).</summary>
    public MainView CurrentView
    {
        get => _settings.PreferredView;
        set => Update(_settings.PreferredView, Enum.IsDefined(value) ? value : MainView.Classic, v => _settings.PreferredView = v,
            nameof(CurrentView), nameof(SelectedView), nameof(IsClassicView), nameof(IsListView), nameof(IsCardView),
            nameof(IsCompactView), nameof(IsPanelView));
    }

    public Choice<MainView> SelectedView
    {
        get => Views.First(c => c.Value == CurrentView);
        set
        {
            if (value is not null)
            {
                CurrentView = value.Value;
            }
        }
    }

    public bool IsClassicView => CurrentView == MainView.Classic;

    public bool IsListView => CurrentView == MainView.List;

    public bool IsCardView => CurrentView == MainView.Card;

    public bool IsCompactView => CurrentView == MainView.Compact;

    public bool IsPanelView => CurrentView == MainView.Panel;

    public ICommand ToggleTopmostCommand { get; }

    public ICommand EnterCompactCommand { get; }

    public ICommand ShowNormalCommand { get; }

    public ICommand ToggleCompactCommand { get; }

    public ICommand OpenOptionsCommand { get; }

    public ICommand ExitCommand { get; }

    /// <summary>User preference: always on top.</summary>
    public bool IsTopmost
    {
        get => _settings.Topmost;
        set => Update(_settings.Topmost, value, v => _settings.Topmost = v, nameof(IsTopmost), nameof(EffectiveTopmost));
    }

    /// <summary>Topmost actually applied: suspended while a full-screen app or presentation is active.</summary>
    public bool EffectiveTopmost => _settings.Topmost && !_isFullScreenAppActive;

    public bool IsFullScreenAppActive
    {
        get => _isFullScreenAppActive;
        set
        {
            if (SetProperty(ref _isFullScreenAppActive, value))
            {
                OnPropertyChanged(nameof(EffectiveTopmost));
            }
        }
    }

    public bool IsCompact
    {
        get => _settings.Mode == WindowMode.Compact;
        set => Update(_settings.Mode, value ? WindowMode.Compact : WindowMode.Normal, v => _settings.Mode = v, nameof(IsCompact));
    }

    public bool DockEnabled
    {
        get => _settings.DockEnabled;
        set => Update(_settings.DockEnabled, value, v => _settings.DockEnabled = v,
            nameof(DockEnabled), nameof(Orientation), nameof(IsVertical), nameof(IsAutoHideActive));
    }

    public DockEdge DockEdge
    {
        get => _settings.DockEdge;
        set => Update(_settings.DockEdge, value, v => _settings.DockEdge = v,
            nameof(DockEdge), nameof(Orientation), nameof(IsVertical));
    }

    /// <summary>
    /// Auto show / hide is the fixed behavior of a docked bar (no setting): it hides at its edge and
    /// comes back when the mouse approaches. A floating bar never hides.
    /// </summary>
    public bool IsAutoHideActive => _settings.DockEnabled;

    public ThemePreference Theme
    {
        get => _settings.Theme;
        set => Update(_settings.Theme, value, v => _settings.Theme = v, nameof(Theme), nameof(SelectedTheme));
    }

    public Choice<ThemePreference> SelectedTheme
    {
        get => Themes.First(c => c.Value == Theme);
        set
        {
            if (value is not null)
            {
                Theme = value.Value;
            }
        }
    }

    public BarSize BarSize
    {
        get => _settings.BarSize;
        set => Update(_settings.BarSize, value, v => _settings.BarSize = v,
            nameof(BarSize), nameof(SelectedBarSize), nameof(BarThickness), nameof(BarIconSize), nameof(BarButtonSize));
    }

    public Choice<BarSize> SelectedBarSize
    {
        get => BarSizes.First(c => c.Value == BarSize);
        set
        {
            if (value is not null)
            {
                BarSize = value.Value;
            }
        }
    }

    public BarOrientation FloatingOrientation
    {
        get => _settings.FloatingOrientation;
        set => Update(_settings.FloatingOrientation, value, v => _settings.FloatingOrientation = v,
            nameof(FloatingOrientation), nameof(SelectedOrientation), nameof(Orientation), nameof(IsVertical));
    }

    public Choice<BarOrientation> SelectedOrientation
    {
        get => Orientations.First(c => c.Value == FloatingOrientation);
        set
        {
            if (value is not null)
            {
                FloatingOrientation = value.Value;
            }
        }
    }

    /// <summary>Effective bar orientation (docking edge wins over the floating preference).</summary>
    public BarOrientation Orientation => DockLayout.OrientationFor(_settings.DockEnabled ? _settings.DockEdge : null, _settings.FloatingOrientation);

    public bool IsVertical => Orientation == BarOrientation.Vertical;

    /// <summary>WPF panel orientation for the bar's item list.</summary>
    public System.Windows.Controls.Orientation PanelOrientation
        => IsVertical ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;

    /// <summary>Bar thickness in DIP: horizontal 44/52, vertical 48/56 (Small/Medium).</summary>
    public double BarThickness => BarThicknessFor(IsVertical);

    /// <summary>Bar thickness in DIP for an orientation (also used for the docking preview).</summary>
    public double BarThicknessFor(bool vertical) => (vertical, BarSize) switch
    {
        (false, BarSize.Small) => 44,
        (false, _) => 52,
        (true, BarSize.Small) => 48,
        _ => 56,
    };

    public double BarButtonSize => BarSize == BarSize.Small ? 36 : 44;

    public double BarIconSize => BarSize == BarSize.Small ? 24 : 28;

    /// <summary>Set by the coordinator: the docked bar is collapsed to its tab.</summary>
    public bool IsBarCollapsed
    {
        get => _isBarCollapsed;
        set => SetProperty(ref _isBarCollapsed, value);
    }

    public bool IsOptionsOpen
    {
        get => _isOptionsOpen;
        set => SetProperty(ref _isOptionsOpen, value);
    }

    public void Dispose() => Loc.Instance.PropertyChanged -= OnLanguageChanged;

    private void Update<T>(T current, T value, Action<T> assign, params string[] changed)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        assign(value);
        _settings.Save();
        foreach (var name in changed)
        {
            OnPropertyChanged(name);
        }

        if (changed.Contains(nameof(Orientation)))
        {
            OnPropertyChanged(nameof(BarThickness));
            OnPropertyChanged(nameof(PanelOrientation));
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var c in Themes)
        {
            c.Relocalize();
        }

        foreach (var c in BarSizes)
        {
            c.Relocalize();
        }

        foreach (var c in Orientations)
        {
            c.Relocalize();
        }

        foreach (var c in Views)
        {
            c.Relocalize();
        }
    }

    private static MainView? ParseView(object? parameter) => parameter switch
    {
        MainView v when Enum.IsDefined(v) => v,
        string s when !int.TryParse(s, out _) && Enum.TryParse<MainView>(s, ignoreCase: true, out var v) && Enum.IsDefined(v) => v,
        _ => null,
    };

    private static SettingsPage ParseSettingsPage(object? parameter) => parameter switch
    {
        SettingsPage p when Enum.IsDefined(p) => p,
        string s when !int.TryParse(s, out _) && Enum.TryParse<SettingsPage>(s, ignoreCase: true, out var p) && Enum.IsDefined(p) => p,
        _ => SettingsPage.General,
    };
}
