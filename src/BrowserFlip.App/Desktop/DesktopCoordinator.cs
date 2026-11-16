using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;

namespace BrowserFlip.App.Desktop;

/// <summary>
/// Owns the main window, the compact bar and the optional tray icon, and applies
/// <see cref="ShellViewModel"/> state to them: mode, always-on-top, docking, auto-hide,
/// placement across monitors / DPI, and display-change recovery.
/// </summary>
/// <remarks>
/// Re-entrancy: every programmatic move happens inside <see cref="_applying"/>; window events
/// raised during our own moves (LocationChanged, DpiChanged) are ignored, so placement and
/// auto-hide cannot feed back into each other.
/// </remarks>
public sealed class DesktopCoordinator : IDisposable
{
    private const double SnapThresholdDip = 24;
    private const double DockMarginDip = 4;
    private const double CursorHysteresisDip = 8;
    private const double TabProximityDip = 6;

    private readonly Application _app;
    private readonly MainViewModel _main;
    private readonly ShellViewModel _shell;
    private readonly IMonitorService _monitors;
    private readonly IWindowPlacement _placement;
    private readonly Func<ITrayIcon> _trayFactory;
    private readonly AutoHideController _autoHide = new();
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DispatcherTimer _fullScreenTimer;
    private readonly MainWindow _mainWindow;
    private readonly CompactBarWindow _bar;

    private ITrayIcon? _tray;
    private bool _applying;
    private bool _exiting;
    private bool _menuOpen;
    private bool _mainHidden;
    private PixelRect _expandedRect;
    private PixelRect _tabRect;
    private PixelRect _lastTarget;
    private PixelRect _barWorkArea;
    private PixelRect _dragStartRect;
    private int _verifyAttempts;
    private double _barScale = 1;

    public DesktopCoordinator(
        Application app,
        MainViewModel main,
        ShellViewModel shell,
        IMonitorService monitors,
        IWindowPlacement placement,
        Func<ITrayIcon> trayFactory)
    {
        _app = app;
        _main = main;
        _shell = shell;
        _monitors = monitors;
        _placement = placement;
        _trayFactory = trayFactory;

        _mainWindow = new MainWindow { DataContext = main, Shell = shell };
        _bar = new CompactBarWindow { DataContext = new DesktopContext(main, shell) };

        _autoHideTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
        _autoHideTimer.Tick += (_, _) => AutoHideTick();
        _fullScreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fullScreenTimer.Tick += (_, _) => _shell.IsFullScreenAppActive = _monitors.IsFullScreenOrPresentationActive();

        _mainWindow.Closing += OnMainClosing;
        _mainWindow.Topmost = _shell.EffectiveTopmost;
        _bar.Topmost = _shell.EffectiveTopmost;
        _bar.Closing += OnBarClosing;
        _bar.DragStarted += (_, _) => OnBarDragStarted();
        _bar.DragCompleted += (_, _) => OnBarDragCompleted();
        _bar.TabInvoked += (_, _) => Expand(activate: true);
        _bar.MenuRequested += (_, _) => ShowMenu(fromTray: false);
        _bar.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (e.NewValue is true && IsKeyboardInput())
            {
                Expand(activate: false);
            }
        };
        _bar.DpiChanged += (_, _) => ScheduleLayout();

        // In compact mode the bar is the active surface: re-check the default browser when the
        // user comes back (e.g. from Windows Settings), exactly like the main window does.
        _bar.Activated += (_, _) => _main.OnActivated();
        _bar.Deactivated += (_, _) => _main.OnDeactivated();

        _shell.PropertyChanged += OnShellChanged;
        _shell.ShowMainRequested += (_, _) => ShowMain();
        _shell.ExitRequested += (_, _) => Exit();
        _monitors.DisplayChanged += (_, _) => OnDisplayChanged();
        _main.PropertyChanged += OnMainChanged;
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        _app.SessionEnding += (_, _) => Exit(); // logoff / shutdown: persist placement, release the tray icon
    }

    public MainWindow MainWindow => _mainWindow;

    public CompactBarWindow Bar => _bar;

    /// <summary>Creates window handles, restores saved placement and shows the active surface.</summary>
    public void Start()
    {
        _ = new WindowInteropHelper(_mainWindow).EnsureHandle();
        _ = new WindowInteropHelper(_bar).EnsureHandle();
        RestoreMainBounds();
        UpdateTray();
        ApplyMode();
        UpdateTimers();
    }

    /// <summary>Explicit exit: saves state, releases the tray icon and shuts down.</summary>
    public void Exit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        SaveMainBounds();
        _autoHideTimer.Stop();
        _fullScreenTimer.Stop();
        _tray?.Dispose();
        _tray = null;
        _bar.Close();
        _mainWindow.Close();
        _app.Shutdown();
    }

    public void Dispose()
    {
        _autoHideTimer.Stop();
        _fullScreenTimer.Stop();
        _shell.PropertyChanged -= OnShellChanged;
        _main.PropertyChanged -= OnMainChanged;
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        _tray?.Dispose();
        _tray = null;
    }

    // ---------------------------------------------------------------- mode / visibility

    private void ApplyMode()
    {
        if (_shell.IsCompact)
        {
            SaveMainBounds(); // remember the main window position before hiding it
            _mainWindow.Hide();
            _autoHide.SetEnabled(_shell.IsAutoHideActive, DateTimeOffset.UtcNow);
            _autoHide.ForceExpand(DateTimeOffset.UtcNow); // always start expanded, never as a bare tab
            _shell.IsBarCollapsed = _autoHide.IsCollapsed;
            Layout();
            if (!_bar.IsVisible)
            {
                _bar.Show();
            }
        }
        else
        {
            _bar.Hide();

            // Leaving compact mode always brings the main window back (never "tray only").
            _mainHidden = false;
            ShowMainWindow(activate: false);
        }

        UpdateTimers();
    }

    private void ShowMain()
    {
        _mainHidden = false;
        if (_shell.IsCompact)
        {
            Expand(activate: true);
            return;
        }

        ShowMainWindow(activate: true);
    }

    private void ShowMainWindow(bool activate)
    {
        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        EnsureMainVisible();
        if (activate)
        {
            _ = _mainWindow.Activate();
        }
    }

    private void OnMainClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        if (_shell.ShouldHideOnClose)
        {
            e.Cancel = true;
            SaveMainBounds();
            _mainHidden = true;
            _mainWindow.Hide();
            return;
        }

        // Closing the main window exits the app (unchanged from v0.1 when the tray option is off).
        e.Cancel = true;
        _mainWindow.Dispatcher.BeginInvoke(Exit);
    }

    /// <summary>
    /// Alt+F4 (or any WM_CLOSE) on the compact bar must never destroy it: that would leave the
    /// process running with no surface. Instead return to the normal window.
    /// </summary>
    private void OnBarClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        e.Cancel = true;
        _bar.Dispatcher.BeginInvoke(() =>
        {
            _mainHidden = false;
            if (_shell.IsCompact)
            {
                _shell.IsCompact = false; // ApplyMode shows the main window
            }
            else
            {
                ShowMainWindow(activate: true);
            }
        });
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateTrayTooltip();
        ScheduleLayout(); // label widths may change
    }

    private static bool IsKeyboardInput()
        => System.Windows.Input.InputManager.Current.MostRecentInputDevice is System.Windows.Input.KeyboardDevice;

    // ---------------------------------------------------------------- shell state

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.EffectiveTopmost):
                _mainWindow.Topmost = _shell.EffectiveTopmost;
                _bar.Topmost = _shell.EffectiveTopmost;
                break;
            case nameof(ShellViewModel.IsTopmost):
                UpdateTimers();
                break;
            case nameof(ShellViewModel.IsCompact):
                ApplyMode();
                break;
            case nameof(ShellViewModel.IsAutoHideActive):
                _autoHide.SetEnabled(_shell.IsAutoHideActive, DateTimeOffset.UtcNow);
                _shell.IsBarCollapsed = _autoHide.IsCollapsed;
                ScheduleLayout();
                UpdateTimers();
                break;
            case nameof(ShellViewModel.DockEnabled):
            case nameof(ShellViewModel.DockEdge):
            case nameof(ShellViewModel.Orientation):
            case nameof(ShellViewModel.BarSize):
                // Deferred: dependent properties (panel orientation, button sizes) are raised after
                // this one, and the bar must re-measure with the new values.
                ScheduleLayout();
                break;
            case nameof(ShellViewModel.Theme):
                ThemeManager.SetPreference(_shell.Theme);
                ScheduleLayout();
                break;
            case nameof(ShellViewModel.TrayEnabled):
                UpdateTray();
                break;
        }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentDefaultText))
        {
            UpdateTrayTooltip();
            ScheduleLayout(); // browser list may have changed size
        }
        else if (e.PropertyName == nameof(MainViewModel.MessageKind)
                 && _main.MessageKind == MessageKind.Error && _shell.IsCompact && !_exiting)
        {
            // Errors (browser missing, blocked by policy, Settings cannot open) are explained in the
            // main window; the compact bar has no room for them, so bring the main window back.
            _shell.IsCompact = false;
            ShowMain();
        }
    }

    private void UpdateTimers()
    {
        var autoHide = _shell.IsCompact && _shell.IsAutoHideActive && !_exiting;
        if (autoHide)
        {
            _autoHideTimer.Start();
        }
        else
        {
            _autoHideTimer.Stop();
        }

        if (_shell.IsTopmost && !_exiting)
        {
            _fullScreenTimer.Start();
        }
        else
        {
            _fullScreenTimer.Stop();
            _shell.IsFullScreenAppActive = false;
        }
    }

    // ---------------------------------------------------------------- placement

    private void ScheduleLayout() => _bar.Dispatcher.BeginInvoke(DispatcherPriority.Background, Layout);

    /// <summary>Computes and applies the compact bar rectangle (expanded or tab).</summary>
    private void Layout()
    {
        // Never fight the user's drag (DragMove's modal loop still runs dispatcher work, e.g. on DPI change).
        if (!_shell.IsCompact || _applying || _exiting || _bar.IsDragging)
        {
            return;
        }

        _applying = true;
        try
        {
            var hwnd = new WindowInteropHelper(_bar).EnsureHandle();
            var monitor = TargetMonitor();
            _barScale = monitor.Scale;
            _barWorkArea = monitor.WorkArea;
            var work = monitor.WorkArea;
            var vertical = _shell.IsVertical;

            var maxLengthDip = ((vertical ? work.Height : work.Width) / monitor.Scale) - (2 * DockMarginDip);
            var dip = _bar.MeasureExpanded(Math.Max(_shell.BarThickness, maxLengthDip));
            var width = Px(dip.Width, monitor.Scale);
            var height = Px(dip.Height, monitor.Scale);

            PixelRect target;
            if (_shell.DockEnabled)
            {
                _expandedRect = DockLayout.Docked(work, _shell.DockEdge, width, height, _shell.Settings.DockPosition, Px(DockMarginDip, monitor.Scale));
                _tabRect = DockLayout.Tab(work, _shell.DockEdge, _expandedRect,
                    Px(ShellViewModel.TabThickness, monitor.Scale), Px(ShellViewModel.TabLength, monitor.Scale));

                target = _shell.IsBarCollapsed ? _tabRect : _expandedRect;
            }
            else
            {
                var saved = _shell.Settings.CompactBounds;
                // Default floating position is well outside the snap zone (24 DIP).
                var origin = saved ?? PixelRect.FromSize(work.Right - width - Px(64, monitor.Scale), work.Top + Px(64, monitor.Scale), width, height);
                var rect = PixelRect.FromSize(origin.Left, origin.Top, width, height);
                target = DockLayout.EnsureVisible(rect, _monitors.GetMonitors(), Px(32, monitor.Scale));
                target = DockLayout.ClampInto(target, _monitors.FromPoint(target.Left + (target.Width / 2), target.Top + (target.Height / 2)).WorkArea);
                _expandedRect = target;
                _tabRect = default;
                _shell.IsBarCollapsed = false;
            }

            // Keep WPF's own DIP size in sync, otherwise WPF re-applies a stale size after a DPI change.
            _bar.Width = target.Width / monitor.Scale;
            _bar.Height = target.Height / monitor.Scale;

            var current = _monitors.FromWindow(hwnd);
            if (!string.Equals(current.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(current.Scale - monitor.Scale) > 0.001)
            {
                // Crossing to a monitor with another DPI: move first so Windows/WPF rescale the window
                // (WM_DPICHANGED), then set the exact rectangle in the verification pass.
                _placement.Move(hwnd, target.Left, target.Top);
            }
            else
            {
                _placement.SetBounds(hwnd, target);
            }

            _lastTarget = target;
            _verifyAttempts = 0;
            _bar.Dispatcher.BeginInvoke(DispatcherPriority.Background, VerifyPlacement);
            _bar.Topmost = _shell.EffectiveTopmost;
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// Runs after WPF has processed any DPI change caused by our move and re-applies the exact
    /// rectangle if WPF adjusted it. Bounded to a few attempts so it can never loop.
    /// </summary>
    private void VerifyPlacement()
    {
        if (!_shell.IsCompact || _exiting || _applying || _lastTarget.IsEmpty)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(_bar).Handle;
        if (hwnd == IntPtr.Zero || _placement.GetBounds(hwnd) == _lastTarget || ++_verifyAttempts > 3)
        {
            return;
        }

        _applying = true;
        try
        {
            var scale = _monitors.FromWindow(hwnd).Scale;
            _bar.Width = _lastTarget.Width / scale;
            _bar.Height = _lastTarget.Height / scale;
            _placement.SetBounds(hwnd, _lastTarget);
        }
        finally
        {
            _applying = false;
        }

        _bar.Dispatcher.BeginInvoke(DispatcherPriority.Background, VerifyPlacement);
    }

    private MonitorInfo TargetMonitor()
    {
        var all = _monitors.GetMonitors();
        if (_shell.DockEnabled)
        {
            return all.FirstOrDefault(m => string.Equals(m.DeviceName, _shell.Settings.DockMonitor, StringComparison.OrdinalIgnoreCase))
                   ?? all.FirstOrDefault(m => m.IsPrimary)
                   ?? all[0];
        }

        if (_shell.Settings.CompactBounds is { } b)
        {
            return _monitors.FromPoint(b.Left + (b.Width / 2), b.Top + (b.Height / 2));
        }

        return all.FirstOrDefault(m => m.IsPrimary) ?? all[0];
    }

    private void OnBarDragStarted()
    {
        _autoHide.ForceExpand(DateTimeOffset.UtcNow);
        var hwnd = new WindowInteropHelper(_bar).Handle;
        _dragStartRect = hwnd == IntPtr.Zero ? default : _placement.GetBounds(hwnd);
    }

    private void OnBarDragCompleted()
    {
        var hwnd = new WindowInteropHelper(_bar).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var rect = _placement.GetBounds(hwnd);
        if (rect == _dragStartRect)
        {
            return; // a plain click on the bar frame is not a drag: never re-dock or re-orient
        }

        var monitor = _monitors.FromPoint(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));
        var edge = DockLayout.SnapEdge(rect, monitor.WorkArea, Px(SnapThresholdDip, monitor.Scale));
        if (edge is { } e)
        {
            _shell.Settings.DockMonitor = monitor.DeviceName;
            _shell.Settings.DockPosition = DockLayout.PositionAlong(rect, monitor.WorkArea, e);
            var changed = !_shell.DockEnabled || _shell.DockEdge != e;
            _shell.DockEdge = e;
            _shell.DockEnabled = true;
            _shell.Settings.Save();
            if (!changed)
            {
                Layout();
            }
        }
        else
        {
            _shell.Settings.CompactBounds = rect;
            _shell.Settings.Save();
            if (_shell.DockEnabled)
            {
                _shell.DockEnabled = false; // dragged away from the edge: float
            }
            else
            {
                Layout();
            }
        }

        _autoHide.ForceExpand(DateTimeOffset.UtcNow);
    }

    private void OnDisplayChanged()
    {
        // Monitors, resolution, scaling, taskbar or resume: re-place everything inside a work area.
        Layout();
        EnsureMainVisible();
        _tray?.UpdateIcon();
    }

    private void RestoreMainBounds()
    {
        if (_shell.Settings.MainBounds is not { } saved)
        {
            return;
        }

        // Size is restored in DIP (WPF owns it and rescales it per monitor DPI); only the position is
        // applied in physical pixels. This keeps size consistent across monitors with different scaling.
        var savedScale = _shell.Settings.MainScale;
        var widthDip = Math.Max(_mainWindow.MinWidth, saved.Width / savedScale);
        var heightDip = Math.Max(_mainWindow.MinHeight, saved.Height / savedScale);

        var target = _monitors.FromPoint(saved.Left + (saved.Width / 2), saved.Top + (saved.Height / 2));
        var estimated = PixelRect.FromSize(saved.Left, saved.Top, Px(widthDip, target.Scale), Px(heightDip, target.Scale));
        var fixedRect = DockLayout.EnsureVisible(estimated, _monitors.GetMonitors(), 48);
        var finalMonitor = _monitors.FromPoint(fixedRect.Left + (fixedRect.Width / 2), fixedRect.Top + (fixedRect.Height / 2));

        // Width is restored (never wider than the work area); the height follows the content
        // (SizeToContent=Height, capped at the work area by the window itself).
        _mainWindow.Width = Math.Min(widthDip, finalMonitor.WorkArea.Width / finalMonitor.Scale);
        _mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        _placement.Move(new WindowInteropHelper(_mainWindow).Handle, fixedRect.Left, fixedRect.Top);
    }

    private void EnsureMainVisible()
    {
        var hwnd = new WindowInteropHelper(_mainWindow).Handle;
        if (hwnd == IntPtr.Zero || !_mainWindow.IsVisible || _mainWindow.WindowState != WindowState.Normal)
        {
            return;
        }

        var current = _placement.GetBounds(hwnd);
        var fixedRect = DockLayout.EnsureVisible(current, _monitors.GetMonitors(), 48);
        if (fixedRect.Left != current.Left || fixedRect.Top != current.Top)
        {
            _placement.Move(hwnd, fixedRect.Left, fixedRect.Top); // WPF keeps its DIP size
        }
    }

    private void SaveMainBounds()
    {
        var hwnd = new WindowInteropHelper(_mainWindow).Handle;
        if (hwnd == IntPtr.Zero || _mainWindow.WindowState != WindowState.Normal || !_mainWindow.IsVisible)
        {
            return;
        }

        _shell.Settings.MainBounds = _placement.GetBounds(hwnd);
        _shell.Settings.MainScale = _monitors.FromWindow(hwnd).Scale;
        _shell.Settings.Save();
    }

    // ---------------------------------------------------------------- auto-hide

    private void AutoHideTick()
    {
        if (!_shell.IsCompact || !_shell.IsAutoHideActive)
        {
            _autoHideTimer.Stop();
            return;
        }

        var cursor = _monitors.GetCursorPosition();
        if (cursor is null)
        {
            return; // secure desktop / locked: keep the current state
        }

        var (x, y) = cursor.Value;
        var hysteresis = Px(CursorHysteresisDip, _barScale);
        var overBar = _expandedRect.Inflate(hysteresis, hysteresis).Contains(x, y);
        var near = Px(TabProximityDip, _barScale);
        // Proximity zone stays inside this monitor's work area: no expansion from the taskbar or a neighbor monitor.
        var tabZone = _tabRect.IsEmpty ? default : _tabRect.Inflate(near, near).Intersect(_barWorkArea);
        var overTab = !tabZone.IsEmpty && tabZone.Contains(x, y);

        // Keyboard focus only holds the bar open while the user is actually using the keyboard on it;
        // a mouse click (which also focuses) must not keep it open forever.
        var keyboardFocus = _bar.IsKeyboardFocusWithin && IsKeyboardInput();
        var suppressed = _bar.IsDragging
                         || _menuOpen
                         || keyboardFocus
                         || (overBar && _monitors.IsMouseButtonDown());

        var wasCollapsed = _autoHide.IsCollapsed;
        _ = _autoHide.Update(new AutoHideInput(DateTimeOffset.UtcNow, overBar, overTab, suppressed));
        if (wasCollapsed != _autoHide.IsCollapsed)
        {
            ApplyCollapsed();
        }
    }

    private void Expand(bool activate)
    {
        if (!_shell.IsCompact)
        {
            ShowMainWindow(activate);
            return;
        }

        _autoHide.ForceExpand(DateTimeOffset.UtcNow);
        if (_shell.IsBarCollapsed)
        {
            ApplyCollapsed();
        }

        if (!_bar.IsVisible)
        {
            _bar.Show();
        }

        if (activate)
        {
            _ = _bar.Activate();
        }
    }

    private void ApplyCollapsed()
    {
        _shell.IsBarCollapsed = _autoHide.IsCollapsed;
        Layout();
        if (!_shell.IsBarCollapsed)
        {
            _bar.PlayExpandAnimation();
        }
    }

    // ---------------------------------------------------------------- tray / menu

    private void UpdateTray()
    {
        if (_shell.TrayEnabled && _tray is null && !_exiting)
        {
            _tray = _trayFactory();
            _tray.Activated += (_, _) => ShowMain();
            _tray.MenuRequested += (_, _) => ShowMenu(fromTray: true);
            UpdateTrayTooltip();
            _tray.Visible = true;
        }
        else if (!_shell.TrayEnabled && _tray is not null)
        {
            _tray.Dispose();
            _tray = null;
            if (_mainHidden && !_shell.IsCompact)
            {
                ShowMain(); // never leave the app without a visible surface
            }
        }
    }

    private void UpdateTrayTooltip()
    {
        if (_tray is not null)
        {
            _tray.ToolTip = Loc.Instance.Format("Tray_Tooltip", _main.State.HttpBrowser?.DisplayName ?? _main.State.Http?.FriendlyAppName ?? "—");
        }
    }

    private void ShowMenu(bool fromTray)
    {
        var model = BuildMenuModel();
        if (fromTray && _tray is not null)
        {
            // Native menu loop is synchronous: suppress auto-hide for its whole duration.
            _menuOpen = true;
            try
            {
                _tray.ShowMenu(model);
            }
            finally
            {
                _menuOpen = false;
            }

            return;
        }

        var menu = ToContextMenu(model);
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => _menuOpen = false;
        menu.PlacementTarget = _bar;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    /// <summary>One menu definition for both the notification-area menu and the compact bar menu.</summary>
    internal IReadOnlyList<TrayMenuItem> BuildMenuModel()
    {
        var edges = _shell.DockEdges
            .Select(choice => new TrayMenuItem(choice.DisplayName, () => _shell.DockToCommand.Execute(choice.Value))
            {
                IsChecked = _shell.DockEnabled && _shell.DockEdge == choice.Value,
            })
            .ToList();

        return
        [
            new(Loc.Instance["Menu_Show"], ShowMain) { IsDefault = true },
            TrayMenuItem.Separator,
            new(Loc.Instance["Menu_Topmost"], () => _shell.ToggleTopmostCommand.Execute(null)) { IsChecked = _shell.IsTopmost },
            new(Loc.Instance["Menu_Compact"], () => _shell.ToggleCompactCommand.Execute(null)) { IsChecked = _shell.IsCompact },
            new(Loc.Instance["Menu_Dock"], () => _shell.ToggleDockCommand.Execute(null)) { IsChecked = _shell.DockEnabled },
            new(Loc.Instance["Menu_DockEdge"]) { Children = edges },
            new(Loc.Instance["Menu_AutoHide"], () => _shell.ToggleAutoHideCommand.Execute(null))
            {
                IsChecked = _shell.AutoHide,
                IsEnabled = _shell.DockEnabled,
            },
            new(Loc.Instance["Menu_Settings"], () => _shell.OpenOptionsCommand.Execute(null)),
            TrayMenuItem.Separator,
            new(Loc.Instance["Menu_Exit"], Exit),
        ];
    }

    private static ContextMenu ToContextMenu(IReadOnlyList<TrayMenuItem> model)
    {
        var menu = new ContextMenu();
        foreach (var item in model)
        {
            menu.Items.Add(ToMenuItem(item));
        }

        return menu;

        static object ToMenuItem(TrayMenuItem item)
        {
            if (item.IsSeparator)
            {
                return new Separator();
            }

            var menuItem = new MenuItem
            {
                Header = item.Text,
                IsChecked = item.IsChecked,
                IsEnabled = item.IsEnabled,
                FontWeight = item.IsDefault ? FontWeights.SemiBold : FontWeights.Normal,
            };
            if (item.Children is { } children)
            {
                foreach (var child in children)
                {
                    menuItem.Items.Add(ToMenuItem(child));
                }
            }
            else if (item.OnClick is { } onClick)
            {
                menuItem.Click += (_, _) => onClick();
            }

            return menuItem;
        }
    }

    private static int Px(double dip, double scale) => (int)Math.Round(dip * scale);
}
