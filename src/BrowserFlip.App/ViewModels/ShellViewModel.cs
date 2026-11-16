using System.ComponentModel;
using System.Windows.Input;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;

namespace BrowserFlip.App.ViewModels;

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
        DockEdges =
        [
            new(DockEdge.Left, "Dock_Left"),
            new(DockEdge.Right, "Dock_Right"),
            new(DockEdge.Top, "Dock_Top"),
            new(DockEdge.Bottom, "Dock_Bottom"),
        ];
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

        ToggleTopmostCommand = new RelayCommand(_ => IsTopmost = !IsTopmost);
        EnterCompactCommand = new RelayCommand(_ => IsCompact = true);
        ShowNormalCommand = new RelayCommand(_ =>
        {
            IsCompact = false;
            ShowMainRequested?.Invoke(this, EventArgs.Empty);
        });
        ToggleCompactCommand = new RelayCommand(_ => IsCompact = !IsCompact);
        ToggleAutoHideCommand = new RelayCommand(_ => AutoHide = !AutoHide);
        ToggleDockCommand = new RelayCommand(_ => DockEnabled = !DockEnabled);
        DockToCommand = new RelayCommand(p =>
        {
            if (p is DockEdge edge)
            {
                DockEdge = edge;
                DockEnabled = true;
            }
        });
        OpenOptionsCommand = new RelayCommand(_ =>
        {
            IsCompact = false;
            IsOptionsOpen = true;
            ShowMainRequested?.Invoke(this, EventArgs.Empty);
        });
        ExitCommand = new RelayCommand(_ => ExitRequested?.Invoke(this, EventArgs.Empty));

        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>The main window should be shown and activated.</summary>
    public event EventHandler? ShowMainRequested;

    /// <summary>The application should exit (explicit "Exit").</summary>
    public event EventHandler? ExitRequested;

    public UserSettings Settings => _settings;

    public IReadOnlyList<Choice<DockEdge>> DockEdges { get; }

    public IReadOnlyList<Choice<ThemePreference>> Themes { get; }

    public IReadOnlyList<Choice<BarSize>> BarSizes { get; }

    public IReadOnlyList<Choice<BarOrientation>> Orientations { get; }

    public ICommand ToggleTopmostCommand { get; }

    public ICommand EnterCompactCommand { get; }

    public ICommand ShowNormalCommand { get; }

    public ICommand ToggleCompactCommand { get; }

    public ICommand ToggleAutoHideCommand { get; }

    public ICommand ToggleDockCommand { get; }

    public ICommand DockToCommand { get; }

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
            nameof(DockEnabled), nameof(Orientation), nameof(IsVertical), nameof(IsAutoHideActive), nameof(SelectedDockEdge));
    }

    public DockEdge DockEdge
    {
        get => _settings.DockEdge;
        set => Update(_settings.DockEdge, value, v => _settings.DockEdge = v,
            nameof(DockEdge), nameof(Orientation), nameof(IsVertical), nameof(SelectedDockEdge));
    }

    public Choice<DockEdge> SelectedDockEdge
    {
        get => DockEdges.First(c => c.Value == DockEdge);
        set
        {
            if (value is not null)
            {
                DockEdge = value.Value;
            }
        }
    }

    /// <summary>User preference for auto show / hide (only effective while docked).</summary>
    public bool AutoHide
    {
        get => _settings.AutoHide;
        set => Update(_settings.AutoHide, value, v => _settings.AutoHide = v, nameof(AutoHide), nameof(IsAutoHideActive));
    }

    /// <summary>Auto-hide actually runs only for a docked compact bar.</summary>
    public bool IsAutoHideActive => _settings.AutoHide && _settings.DockEnabled;

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

    public bool TrayEnabled
    {
        get => _settings.TrayEnabled;
        set => Update(_settings.TrayEnabled, value, v => _settings.TrayEnabled = v, nameof(TrayEnabled), nameof(IsCloseToTrayAvailable));
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set => Update(_settings.CloseToTray, value, v => _settings.CloseToTray = v, nameof(CloseToTray));
    }

    public bool IsCloseToTrayAvailable => _settings.TrayEnabled;

    /// <summary>True when closing the main window should hide it instead of exiting.</summary>
    public bool ShouldHideOnClose => _settings.TrayEnabled && _settings.CloseToTray;

    /// <summary>Effective bar orientation (docking edge wins over the floating preference).</summary>
    public BarOrientation Orientation => DockLayout.OrientationFor(_settings.DockEnabled ? _settings.DockEdge : null, _settings.FloatingOrientation);

    public bool IsVertical => Orientation == BarOrientation.Vertical;

    /// <summary>WPF panel orientation for the bar's item list.</summary>
    public System.Windows.Controls.Orientation PanelOrientation
        => IsVertical ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;

    /// <summary>Bar thickness in DIP: horizontal 44/52, vertical 48/56 (Small/Medium).</summary>
    public double BarThickness => (IsVertical, BarSize) switch
    {
        (false, BarSize.Small) => 44,
        (false, _) => 52,
        (true, BarSize.Small) => 48,
        _ => 56,
    };

    public double BarButtonSize => BarSize == BarSize.Small ? 36 : 44;

    public double BarIconSize => BarSize == BarSize.Small ? 24 : 28;

    /// <summary>Thickness of the collapsed tab in DIP.</summary>
    public static double TabThickness => 8;

    /// <summary>Length of the collapsed tab in DIP.</summary>
    public static double TabLength => 56;

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
        foreach (var c in DockEdges)
        {
            c.Relocalize();
        }

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
    }
}
