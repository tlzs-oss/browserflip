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

    // The main window docks only when the cursor is pushed against the edge (or onto the taskbar) when the
    // drag ends: a window that merely ends up near an edge never switches mode (BF-01).
    private const int MainDockThresholdPx = 2;
    private const double MainDockPreviewLengthDip = 240;
    private const double DockMarginDip = 4;
    private const double CursorHysteresisDip = 8;
    private const double TriggerAlongMarginDip = 8;
    private const double TriggerOvershootDip = 4;
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromMilliseconds(10);

    private readonly Application? _app;
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
    private bool _barMenuOpen;      // the bar's own context menu (keyboard use continues inside it)
    private bool _trayOnly;          // every window hidden, only the notification-area icon (main window X / Alt+F4 on a floating bar)
    private (int X, int Y)? _floatingCenter; // keep the floating bar centered here (switched from the main window, BF-03)
    private int _overflowRelayouts;          // BF-05: consecutive re-layouts because the browser list did not fit
    private bool _incompleteMeasure;         // BF-05: the bar was sized before its browser buttons existed
    private PixelRect? _mainRestore;         // main window rectangle before a drag that docked it (undo a Windows snap)
    private bool _closedUnderPointer;        // docked bar retracted by Alt+F4: no edge expansion until the cursor leaves the zone once
    private bool _enterCollapsed;    // next compact layout starts collapsed (docked bar, main window closed)
    private bool _keyboardEngaged;   // the user really typed on the bar (not just an activation)
    private PixelRect _expandedRect;
    private PixelRect _tabRect;
    private PixelRect _triggerZone;
    private DockPreviewWindow? _preview;
    private PixelRect _lastTarget;
    private PixelRect _dragStartRect;
    private int _verifyAttempts;
    private double _barScale = 1;

    public DesktopCoordinator(
        Application? app,
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
        BarHasKeyboardFocus = () => _bar.IsKeyboardFocusWithin;

        _autoHideTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = IdlePollInterval };
        _autoHideTimer.Tick += (_, _) => AutoHideTick();
        _fullScreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fullScreenTimer.Tick += (_, _) => _shell.IsFullScreenAppActive = _monitors.IsFullScreenOrPresentationActive();

        _mainWindow.Closing += OnMainClosing;
        _mainWindow.Topmost = _shell.EffectiveTopmost;
        _bar.Topmost = BarTopmost;
        _bar.Closing += OnBarClosing;
        _bar.DragStarted += (_, _) => OnBarDragStarted();
        _bar.DragCompleted += (_, _) => OnBarDragCompleted();
        _bar.LocationChanged += (_, _) =>
        {
            if (_bar.IsDragging)
            {
                UpdateDragPreview();
            }
        };
        _bar.TabInvoked += (_, _) =>
        {
            // Enter / Space on the strip: the key press already marked keyboard use; keep it through the
            // activation (a mouse click on the strip has cleared it in PreviewMouseDown).
            var keyboard = _keyboardEngaged;
            Expand(activate: true);
            _keyboardEngaged = keyboard;
        };
        _bar.MenuRequested += (_, _) => ShowMenu(fromTray: false);
        _bar.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            // Focus leaving the bar ends keyboard use, except into the bar's own context menu. Focus entering
            // does not expand by itself: any window activation looks like keyboard input to WPF; the strip's
            // button expands with Enter / Space.
            if (e.NewValue is not true && !_barMenuOpen)
            {
                _keyboardEngaged = false;
            }
        };

        // Only real typing on the bar holds it open. WPF reports a window activation (ours, e.g. after a
        // tray click) as keyboard input too, so "most recent input device" alone is not proof of use (T13).
        _bar.PreviewKeyDown += (_, e) =>
        {
            if (CountsAsKeyboardUse(e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key))
            {
                _keyboardEngaged = true;
            }
        };
        _bar.PreviewMouseDown += (_, _) => _keyboardEngaged = false;
        _bar.DpiChanged += (_, _) => ScheduleLayout();
        _bar.BrowserListSizeChanged += (_, _) => OnBrowserListSizeChanged();
        _bar.LayoutUpdated += (_, _) =>
        {
            // BF-05: sized from an incomplete measurement -> size again as soon as the browser buttons exist,
            // whatever path showed the bar (no reliance on one deferred re-layout after Show).
            if (_incompleteMeasure && _bar.IsVisible && !_applying && _bar.BrowserButtonsReady())
            {
                _incompleteMeasure = false;
                IncompleteMeasureRelayouts++;
                ScheduleLayout();
            }
        };

        // In compact mode the bar is the active surface: re-check the default browser when the
        // user comes back (e.g. from Windows Settings), exactly like the main window does.
        _bar.Activated += (_, _) => _main.OnActivated();
        _bar.Deactivated += (_, _) => _main.OnDeactivated();

        _mainWindow.UserMoving += (_, _) => UpdateMainDragPreview();
        _mainWindow.UserMoveCompleted += (_, start) => OnMainDragCompleted(start);
        _shell.EnterCompactRequested += (_, _) => EnterCompactNearMain();
        _shell.PropertyChanged += OnShellChanged;
        _shell.ShowMainRequested += (_, _) => ShowMain();
        _shell.ExitRequested += (_, _) => Exit();
        _monitors.DisplayChanged += (_, _) => OnDisplayChanged();
        _main.PropertyChanged += OnMainChanged;
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        if (_app is not null)
        {
            _app.SessionEnding += (_, _) => Exit(); // logoff / shutdown: persist placement, release the tray icon
        }
    }

    public MainWindow MainWindow => _mainWindow;

    /// <summary>Clock used by auto-hide (replaceable in tests).</summary>
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>True while the docked bar is collapsed to its edge tab.</summary>
    internal bool IsCollapsed => _autoHide.IsCollapsed;

    /// <summary>Whether the bar has keyboard focus (replaceable in tests: activation cannot be done there).</summary>
    internal Func<bool> BarHasKeyboardFocus { get; set; }

    /// <summary>False never activates (focuses) a window (tests on a shared desktop).</summary>
    internal bool AllowActivation { get; set; } = true;

    /// <summary>Current cursor polling interval of auto-hide (tests).</summary>
    internal TimeSpan PollInterval => _autoHideTimer.Interval;

    /// <summary>False keeps the drag preview computed but never shown (tests).</summary>
    internal bool ShowDragPreview { get; set; } = true;

    /// <summary>Edge the bar would dock to if the current drag ended now (null = float).</summary>
    internal DockEdge? PreviewEdge { get; private set; }

    /// <summary>Area that expands the collapsed bar (physical pixels).</summary>
    internal PixelRect TriggerZone => _triggerZone;

    /// <summary>
    /// "Always on top" (the pin) alone decides the window layer, for the bar like for the main window; docking and
    /// auto-hide do not change it (BF-02). A bar that is not on top is raised without activation when it expands.
    /// </summary>
    private bool BarTopmost => _shell.EffectiveTopmost;

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
        _app?.Shutdown();
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
        _keyboardEngaged = false; // a new surface: keyboard use starts again with a real key press on the bar
        if (_shell.IsCompact)
        {
            SaveMainBounds(); // remember the main window position before hiding it
            _mainWindow.Hide();
            _autoHide.SetEnabled(_shell.IsAutoHideActive, Now());
            _autoHide.ForceExpand(Now()); // switching to the bar: show it expanded first
            if (_enterCollapsed)
            {
                _autoHide.CollapseNow(Now()); // main window closed: a docked bar goes straight to its edge strip
            }

            _enterCollapsed = false;
            _bar.CancelCollapseAnimation();
            _shell.IsBarCollapsed = _autoHide.IsCollapsed;
            Layout();
            if (_trayOnly)
            {
                _bar.Hide(); // floating bar + main window closed: notification area only
            }
            else if (!_bar.IsVisible)
            {
                _bar.Show(); // sized again once its browser buttons exist (BF-05, see LayoutUpdated)
            }
        }
        else
        {
            _bar.Hide();
            _trayOnly = false; // the main window is the visible surface now; the bar shows normally next time
            _enterCollapsed = false;
            _floatingCenter = null;
            var restored = RestoreMainAfterDock();

            // Leaving compact mode always brings the main window back (never "tray only").
            ShowMainWindow(activate: false);
            if (restored is { } at)
            {
                // Moving the hidden window to a monitor with another DPI may have shifted it (WM_DPICHANGED): once more.
                _ = _mainWindow.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    var hwnd = new WindowInteropHelper(_mainWindow).Handle;
                    var now = hwnd == IntPtr.Zero ? default : _placement.GetBounds(hwnd);
                    if (hwnd != IntPtr.Zero && (now.Left != at.X || now.Top != at.Y))
                    {
                        _placement.Move(hwnd, at.X, at.Y);
                    }
                });
            }
        }

        UpdateTimers();
    }

    private void ShowMain()
    {
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
        if (activate && AllowActivation)
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

        // X on the main window hides BrowserFlip to the notification area (BF-04): no mode switch, no bar,
        // no exit. Only "Exit" (tray / bar menu), logoff or shutdown end the process.
        e.Cancel = true;
        _mainWindow.Dispatcher.BeginInvoke(HideToTray);
    }

    /// <summary>
    /// Tray only: hides every window and keeps the notification-area icon. The mode, dock state, edge,
    /// monitor, orientation and saved positions are not changed; the tray restores the same surface.
    /// </summary>
    internal void HideToTray()
    {
        if (_exiting)
        {
            return;
        }

        SaveMainBounds();
        _trayOnly = true;
        _mainWindow.Hide();
        _bar.Hide();
        HideDragPreview();
        UpdateTimers();
    }

    /// <summary>Every window is hidden; only the notification-area icon is left.</summary>
    internal bool IsTrayOnly => _trayOnly;

    /// <summary>
    /// Tray click (and a second start): brings back the surface that was in use, where it was: the main window
    /// (also from minimized), the floating bar, or the docked bar (expanded).
    /// </summary>
    internal void RestoreFromTray()
    {
        if (_exiting)
        {
            return;
        }

        _trayOnly = false;
        if (_shell.IsCompact)
        {
            Expand(activate: true);
        }
        else
        {
            ShowMainWindow(activate: true);
        }

        UpdateTimers();
    }

    /// <summary>BrowserFlip was started again while running: bring the current surface forward.</summary>
    internal void ShowRunningInstance()
    {
        RestoreFromTray();
    }

    /// <summary>Tray menu "show compact bar": like the compact button when coming from the main window.</summary>
    internal void ShowToolbar()
    {
        if (_exiting)
        {
            return;
        }

        _trayOnly = false;
        if (!_shell.IsCompact)
        {
            EnterCompactNearMain();
        }

        Expand(activate: true);
    }

    /// <summary>
    /// Compact button (BF-03): the bar appears floating at the center of the main window, in the user's floating
    /// orientation, kept inside that monitor's work area. Never jumps to a corner or another monitor.
    /// </summary>
    internal void EnterCompactNearMain()
    {
        if (_exiting || _shell.IsCompact)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(_mainWindow).Handle;
        var main = hwnd != IntPtr.Zero && _mainWindow.IsVisible && _mainWindow.WindowState == WindowState.Normal
            ? _placement.GetBounds(hwnd)
            : _shell.Settings.MainBounds ?? default;
        _trayOnly = false;
        _shell.DockEnabled = false; // a manual switch floats; docking is done by dragging to an edge
        if (!main.IsEmpty)
        {
            var center = (main.Left + (main.Width / 2), main.Top + (main.Height / 2));
            _floatingCenter = center;
            var size = _shell.Settings.CompactBounds is { } b ? (b.Width, b.Height) : (100, 100);
            _shell.Settings.CompactBounds = PixelRect.FromSize(center.Item1 - (size.Item1 / 2), center.Item2 - (size.Item2 / 2), size.Item1, size.Item2);
        }

        _shell.IsCompact = true; // ApplyMode hides the main window and lays the bar out around the center
    }

    /// <summary>
    /// The user dragged the main window and released it with the cursor pushed against a screen edge (BF-01):
    /// switch to the compact bar docked to that edge of that monitor, already retracted to its strip. The main
    /// window goes back to where it was before the drag (undoing a Windows snap) for when it is shown again.
    /// </summary>
    internal void OnMainDragCompleted(PixelRect before)
    {
        HideDragPreview();
        if (_exiting || _shell.IsCompact || _mainWindow.WindowState != WindowState.Normal)
        {
            return;
        }

        var cursor = _monitors.GetCursorPosition();
        if (cursor is not { } c)
        {
            return;
        }

        var monitor = _monitors.FromPoint(c.X, c.Y);
        if (MainDockEdge(monitor, c) is not { } edge)
        {
            return; // an ordinary move (or the edge is shared with another monitor)
        }

        var hwnd = new WindowInteropHelper(_mainWindow).Handle;
        if (hwnd == IntPtr.Zero || before.IsEmpty || _placement.GetBounds(hwnd) == before)
        {
            return; // the window did not end up moved (e.g. Esc cancelled the drag): never dock
        }

        // The window goes back to where it was before the drag when it is shown again (a Windows snap may
        // still resize it after this point, so the restore is applied when the main window reappears).
        _mainRestore = before;

        _shell.Settings.DockMonitor = monitor.DeviceName;
        _shell.Settings.DockPosition = DockLayout.PositionAlong(PixelRect.FromSize(c.X, c.Y, 1, 1), monitor.WorkArea, edge);
        _floatingCenter = null;
        _shell.DockEdge = edge;
        _shell.DockEnabled = true;
        _enterCollapsed = true; // retracted at once; the cursor at the edge expands it
        _trayOnly = false;
        _shell.IsCompact = true;

        // ApplyMode saved the (possibly snapped) current rectangle: keep the one from before the drag.
        _shell.Settings.MainBounds = before;
        _shell.Settings.MainScale = _monitors.FromPoint(before.Left + (before.Width / 2), before.Top + (before.Height / 2)).Scale;
        _shell.Settings.Save();
    }

    /// <summary>Edge the main window docks to: the cursor pushed against an outer edge (no monitor beyond it).</summary>
    private DockEdge? MainDockEdge(MonitorInfo monitor, (int X, int Y) cursor)
        => DockLayout.PushedEdge(monitor.WorkArea, MainDockThresholdPx, cursor) is { } edge && IsOuterEdge(monitor, edge) ? edge : null;

    /// <summary>Applies the main window geometry from before the drag that docked it (position, width; height follows content).</summary>
    private (int X, int Y)? RestoreMainAfterDock()
    {
        if (_mainRestore is not { } r)
        {
            return null;
        }

        _mainRestore = null;
        var hwnd = new WindowInteropHelper(_mainWindow).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var target = _monitors.FromPoint(r.Left + (r.Width / 2), r.Top + (r.Height / 2));
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.SizeToContent = SizeToContent.Height; // a snap turned it to Manual with the snapped height
        _mainWindow.Width = Math.Max(_mainWindow.MinWidth, Math.Min(r.Width, target.WorkArea.Width) / target.Scale);
        var fixedRect = DockLayout.EnsureVisible(r, _monitors.GetMonitors(), 48);
        _placement.Move(hwnd, fixedRect.Left, fixedRect.Top);
        return (fixedRect.Left, fixedRect.Top);
    }

    /// <summary>Edge the main window would dock to if released now (null = none).</summary>
    internal DockEdge? MainDragPreviewEdge { get; private set; }

    /// <summary>While the main window is dragged: outline the docked bar when the cursor is pushed against an edge.</summary>
    internal void UpdateMainDragPreview()
    {
        if (_exiting || _shell.IsCompact || _monitors.GetCursorPosition() is not { } c)
        {
            return;
        }

        var monitor = _monitors.FromPoint(c.X, c.Y);
        MainDragPreviewEdge = MainDockEdge(monitor, c);
        if (MainDragPreviewEdge is not { } edge)
        {
            HideDragPreview();
            return;
        }

        var vertical = edge is DockEdge.Left or DockEdge.Right;
        var thickness = Px(_shell.BarThicknessFor(vertical), monitor.Scale);
        var length = Px(MainDockPreviewLengthDip, monitor.Scale);
        var position = DockLayout.PositionAlong(PixelRect.FromSize(c.X, c.Y, 1, 1), monitor.WorkArea, edge);
        ShowPreview(monitor, DockLayout.Docked(monitor.WorkArea, edge, vertical ? thickness : length, vertical ? length : thickness,
            position, Px(DockMarginDip, monitor.Scale)));
    }

    /// <summary>
    /// Alt+F4 (WM_CLOSE) on the compact bar never destroys it (BF-04; the bar has no close button, BF-06): a docked bar retracts into its edge
    /// (same edge, monitor and position; it expands again on approach); a floating bar hides to the tray.
    /// </summary>
    private void OnBarClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        e.Cancel = true;
        _bar.Dispatcher.BeginInvoke(CloseBar);
    }

    internal void CloseBar()
    {
        if (_exiting || !_shell.IsCompact)
        {
            return;
        }

        if (_shell.IsAutoHideActive)
        {
            // Explicit close (Alt+F4): retract at once. No fade: the pointer may still be over the bar, and
            // "back on the bar during the fade" would expand it again.
            _keyboardEngaged = false;
            _autoHide.CollapseNow(Now());
            _bar.CancelCollapseAnimation();
            _shell.IsBarCollapsed = true;
            Layout();
            // If the pointer is inside the edge zone, wait until it leaves the zone once before expanding again.
            _closedUnderPointer = _monitors.GetCursorPosition() is { } p && _triggerZone.Contains(p.X, p.Y);
            return;
        }

        HideToTray();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateTrayTooltip();
        ScheduleLayout(); // label widths may change
    }

    /// <summary>
    /// Keys that mean the user is working on the bar with the keyboard. Modifiers alone, volume / media
    /// keys and Print Screen do not (pressing them must not hold the bar open).
    /// </summary>
    internal static bool CountsAsKeyboardUse(System.Windows.Input.Key key) => key is not (
        System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
        or System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
        or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
        or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin
        or System.Windows.Input.Key.VolumeUp or System.Windows.Input.Key.VolumeDown or System.Windows.Input.Key.VolumeMute
        or System.Windows.Input.Key.MediaNextTrack or System.Windows.Input.Key.MediaPreviousTrack
        or System.Windows.Input.Key.MediaPlayPause or System.Windows.Input.Key.MediaStop
        or System.Windows.Input.Key.PrintScreen or System.Windows.Input.Key.None
        or System.Windows.Input.Key.CapsLock or System.Windows.Input.Key.NumLock or System.Windows.Input.Key.Scroll
        or System.Windows.Input.Key.Sleep or System.Windows.Input.Key.SelectMedia
        or System.Windows.Input.Key.LaunchMail or System.Windows.Input.Key.LaunchApplication1 or System.Windows.Input.Key.LaunchApplication2
        or System.Windows.Input.Key.BrowserBack or System.Windows.Input.Key.BrowserForward or System.Windows.Input.Key.BrowserRefresh
        or System.Windows.Input.Key.BrowserStop or System.Windows.Input.Key.BrowserSearch or System.Windows.Input.Key.BrowserFavorites
        or System.Windows.Input.Key.BrowserHome);

    // ---------------------------------------------------------------- shell state

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.EffectiveTopmost):
                _mainWindow.Topmost = _shell.EffectiveTopmost;
                _bar.Topmost = BarTopmost;
                break;
            case nameof(ShellViewModel.IsTopmost):
                UpdateTimers();
                break;
            case nameof(ShellViewModel.IsCompact):
                ApplyMode();
                break;
            case nameof(ShellViewModel.IsAutoHideActive):
                _autoHide.SetEnabled(_shell.IsAutoHideActive, Now());
                _bar.CancelCollapseAnimation();
                _shell.IsBarCollapsed = _autoHide.IsCollapsed;
                _bar.Topmost = BarTopmost;
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
        var autoHide = _shell.IsCompact && _shell.IsAutoHideActive && !_trayOnly && !_exiting;
        if (autoHide)
        {
            _autoHideTimer.Start();
        }
        else
        {
            _autoHideTimer.Stop();
        }

        // Full-screen detection suspends "on top" and keeps a docked bar from being raised over the full-screen app.
        if ((_shell.IsTopmost || (_shell.IsCompact && _shell.DockEnabled)) && !_exiting)
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
            var work = monitor.WorkArea;
            var vertical = _shell.IsVertical;

            var maxLengthDip = ((vertical ? work.Height : work.Width) / monitor.Scale) - (2 * DockMarginDip);
            var maxLength = Math.Max(_shell.BarThickness, maxLengthDip);
            // The bar content is only Hidden while collapsed, so it measures at its real size here too (T7, T13).
            var dip = _bar.MeasureExpanded(maxLength);
            _incompleteMeasure = !_bar.LastMeasureComplete;
            if (_incompleteMeasure)
            {
                IncompleteMeasurements++;
            }

            // Longer than the screen allows: a scroll bar along the bar, with the bar made thicker by the scroll bar so
            // the browser buttons are not covered (browsers never disappear silently).
            var overflow = (vertical ? dip.Height : dip.Width) >= maxLength - 0.5;
            _bar.ShowOverflowScrollBar(overflow);
            if (overflow)
            {
                dip = _bar.MeasureExpanded(maxLength, vertical ? SystemParameters.VerticalScrollBarWidth : SystemParameters.HorizontalScrollBarHeight);
            }

            var width = Px(dip.Width, monitor.Scale);
            var height = Px(dip.Height, monitor.Scale);

            PixelRect target;
            if (_shell.DockEnabled)
            {
                _expandedRect = DockLayout.Docked(work, _shell.DockEdge, width, height, _shell.Settings.DockPosition, Px(DockMarginDip, monitor.Scale));
                // Collapsed: a thin strip (3-5 px) as long as the bar, so it is clear where the bar is.
                var alongEdge = _shell.DockEdge is DockEdge.Left or DockEdge.Right ? _expandedRect.Height : _expandedRect.Width;
                _tabRect = DockLayout.Tab(work, _shell.DockEdge, _expandedRect, DockLayout.HintThickness(monitor.Scale), alongEdge);
                _triggerZone = DockLayout.TriggerZone(work, monitor.Bounds, _shell.DockEdge, _expandedRect,
                    DockLayout.TriggerDepth(monitor.Scale), Px(TriggerAlongMarginDip, monitor.Scale), Px(TriggerOvershootDip, monitor.Scale));

                target = _shell.IsBarCollapsed ? _tabRect : _expandedRect;
            }
            else
            {
                var saved = _shell.Settings.CompactBounds;
                // Default floating position is well outside the snap zone (24 DIP).
                var origin = _floatingCenter is { } center
                    ? PixelRect.FromSize(center.X - (width / 2), center.Y - (height / 2), width, height) // BF-03: around the main window's center
                    : saved ?? PixelRect.FromSize(work.Right - width - Px(64, monitor.Scale), work.Top + Px(64, monitor.Scale), width, height);
                var rect = PixelRect.FromSize(origin.Left, origin.Top, width, height);
                target = DockLayout.EnsureVisible(rect, _monitors.GetMonitors(), Px(32, monitor.Scale));
                target = DockLayout.ClampInto(target, _monitors.FromPoint(target.Left + (target.Width / 2), target.Top + (target.Height / 2)).WorkArea);
                _expandedRect = target;
                _tabRect = default;
                _triggerZone = default;
                _shell.IsBarCollapsed = false;
                if (_floatingCenter is not null)
                {
                    _shell.Settings.CompactBounds = target; // the next restore / restart starts here
                }
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
            _bar.Topmost = BarTopmost;
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

    internal void OnBarDragStarted()
    {
        _floatingCenter = null;
        _autoHide.ForceExpand(Now());
        var hwnd = new WindowInteropHelper(_bar).Handle;
        _dragStartRect = hwnd == IntPtr.Zero ? default : _placement.GetBounds(hwnd);
    }

    internal void OnBarDragCompleted()
    {
        var hwnd = new WindowInteropHelper(_bar).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        HideDragPreview();
        var rect = _placement.GetBounds(hwnd);
        if (rect == _dragStartRect)
        {
            return; // a plain click on the bar frame is not a drag: never re-dock or re-orient
        }

        var (monitor, edge) = DropTarget(rect);
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

        _autoHide.ForceExpand(Now());
    }

    /// <summary>Where a drag ending at <paramref name="rect"/> docks: decided by the cursor first (see <see cref="DockLayout.SnapEdge"/>).</summary>
    private (MonitorInfo Monitor, DockEdge? Edge) DropTarget(PixelRect rect)
    {
        var cursor = _monitors.GetCursorPosition();
        var monitor = cursor is { } c
            ? _monitors.FromPoint(c.X, c.Y)
            : _monitors.FromPoint(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));
        return (monitor, DockLayout.SnapEdge(rect, monitor.WorkArea, Px(SnapThresholdDip, monitor.Scale), cursor, e => IsOuterEdge(monitor, e)));
    }

    /// <summary>True when no other monitor lies beyond <paramref name="edge"/> of <paramref name="monitor"/>.</summary>
    private bool IsOuterEdge(MonitorInfo monitor, DockEdge edge)
    {
        var b = monitor.Bounds;
        var beyond = edge switch
        {
            DockEdge.Left => new PixelRect(b.Left - 1, b.Top, b.Left, b.Bottom),
            DockEdge.Right => new PixelRect(b.Right, b.Top, b.Right + 1, b.Bottom),
            DockEdge.Top => new PixelRect(b.Left, b.Top - 1, b.Right, b.Top),
            _ => new PixelRect(b.Left, b.Bottom, b.Right, b.Bottom + 1),
        };
        return !_monitors.GetMonitors().Any(m => !m.Bounds.Intersect(beyond).IsEmpty);
    }

    /// <summary>While dragging: outline where the bar would dock (and in which orientation) if released now.</summary>
    internal void UpdateDragPreview()
    {
        var hwnd = new WindowInteropHelper(_bar).Handle;
        if (hwnd == IntPtr.Zero || _exiting)
        {
            return;
        }

        var rect = _placement.GetBounds(hwnd);
        var (monitor, edge) = DropTarget(rect);
        PreviewEdge = edge;
        if (edge is not { } e)
        {
            HideDragPreview();
            return;
        }

        if (!ShowDragPreview)
        {
            return;
        }

        // Same size rules as the docked bar: thickness by orientation, length = the bar's long side.
        var vertical = e is DockEdge.Left or DockEdge.Right;
        var thickness = Px(_shell.BarThicknessFor(vertical), monitor.Scale);
        var length = Math.Max(rect.Width, rect.Height);
        var position = DockLayout.PositionAlong(rect, monitor.WorkArea, e);
        var target = DockLayout.Docked(monitor.WorkArea, e, vertical ? thickness : length, vertical ? length : thickness, position, Px(DockMarginDip, monitor.Scale));

        ShowPreview(monitor, target);
    }

    private void ShowPreview(MonitorInfo monitor, PixelRect target)
    {
        if (!ShowDragPreview)
        {
            return;
        }

        _preview ??= new DockPreviewWindow();
        _preview.Width = target.Width / monitor.Scale;
        _preview.Height = target.Height / monitor.Scale;
        var previewHwnd = new WindowInteropHelper(_preview).EnsureHandle();
        _placement.SetBounds(previewHwnd, target);
        if (!_preview.IsVisible)
        {
            _preview.Show();
            _placement.SetBounds(previewHwnd, target); // again after a DPI change caused by showing on another monitor
        }
    }

    private void HideDragPreview()
    {
        PreviewEdge = null;
        MainDragPreviewEdge = null;
        _preview?.Hide();
    }

    private void OnDisplayChanged()
    {
        // Monitors, resolution, scaling, taskbar or resume: re-place everything inside a work area.
        // A collapsed bar is shown again so it can never stay hidden at an unexpected place.
        if (_shell.IsCompact && (_autoHide.IsCollapsed || _bar.IsCollapsing))
        {
            _autoHide.ForceExpand(Now());
            _bar.CancelCollapseAnimation();
            _shell.IsBarCollapsed = false;
        }

        Layout();
        EnsureMainVisible();
        _mainWindow.RefitToWorkArea(); // new work area / resolution: re-cap the height and keep it above the taskbar
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

    internal void AutoHideTick()
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
        // The trigger zone covers the bar's whole length at its edge (never the taskbar).
        var overTab = !_triggerZone.IsEmpty && _triggerZone.Contains(x, y);
        overBar |= overTab; // resting against the edge next to the bar keeps it open

        // Keyboard focus only holds the bar open while the user is actually using the keyboard on it;
        // a mouse click (which also focuses) must not keep it open forever.
        var keyboardFocus = _keyboardEngaged && BarHasKeyboardFocus();
        var suppressed = _bar.IsDragging
                         || _menuOpen
                         || keyboardFocus
                         || (overBar && _monitors.IsMouseButtonDown());

        // The collapse fade is running but the user came back to the bar or pressed a button on it:
        // never hide under the user's hand.
        if (_bar.IsCollapsing && (overBar || suppressed))
        {
            Expand(activate: false);
            return;
        }

        // A held mouse button never expands a hidden bar (dragging a scroll bar, a window to Snap or a
        // file across the edge is not "approaching the bar").
        if (_closedUnderPointer && !overTab)
        {
            _closedUnderPointer = false;
        }

        var hoverTab = overTab && !_monitors.IsMouseButtonDown() && !_closedUnderPointer;

        var wasCollapsed = _autoHide.IsCollapsed;
        _ = _autoHide.Update(new AutoHideInput(Now(), overBar, hoverTab, suppressed));
        if (wasCollapsed != _autoHide.IsCollapsed)
        {
            ApplyCollapsed();
        }

        // Poll every 40 ms, and exactly when a dwell / delay is due (timer granularity would otherwise add
        // up to one interval to the 100 ms dwell and the 450 ms collapse delay).
        var interval = IdlePollInterval;
        if (_autoHide.TimeUntilTransition(Now()) is { } due && due < interval)
        {
            interval = due < MinPollInterval ? MinPollInterval : due;
        }

        if (_autoHideTimer.Interval != interval)
        {
            _autoHideTimer.Interval = interval;
        }
    }

    private void Expand(bool activate)
    {
        if (!_shell.IsCompact)
        {
            ShowMainWindow(activate);
            return;
        }

        _autoHide.ForceExpand(Now());
        if (_shell.IsBarCollapsed || _bar.IsCollapsing)
        {
            ApplyCollapsed();
        }

        if (!_bar.IsVisible)
        {
            _trayOnly = false;
            Layout(); // restore the saved floating position / docked place before showing
            _bar.Show(); // sized again once its browser buttons exist (BF-05, see LayoutUpdated)
        }

        RaiseBar();

        if (activate)
        {
            _keyboardEngaged = false; // our own activation is not keyboard use (T13); the tray handler re-evaluates it
            if (AllowActivation)
            {
                _ = _bar.Activate();
            }
        }
    }

    /// <summary>
    /// BF-05 safety net: the bar window is sized from a measurement of its content (fixed at the source in
    /// <see cref="CompactBarWindow.MeasureExpanded"/>). If the real layout still shows that the browser list does not
    /// fit, size the bar again from a fresh measurement, at most twice in a row (no loop).
    /// </summary>
    private void OnBrowserListSizeChanged()
    {
        if (_applying)
        {
            // Reported during our own layout: check again once it is done (never drop it).
            _ = _bar.Dispatcher.BeginInvoke(DispatcherPriority.Background, OnBrowserListSizeChanged);
            return;
        }

        if (_exiting || !_shell.IsCompact || _shell.IsBarCollapsed || _bar.IsDragging || !_bar.IsVisible)
        {
            return;
        }

        if (!_bar.BrowsersOverflow)
        {
            _overflowRelayouts = 0;
            return;
        }

        // Safety net (the measurement itself is fixed in MeasureExpanded): at most two fresh layouts in a row. A bar
        // longer than the screen gets its scroll bar from Layout.
        if (_overflowRelayouts < 2)
        {
            _overflowRelayouts++;
            OverflowRelayouts++;
            ScheduleLayout();
        }
    }

    /// <summary>How often the bar was sized again because it had been measured before its browser buttons existed (tests).</summary>
    internal int IncompleteMeasureRelayouts { get; private set; }

    /// <summary>How often a layout measured the bar before its browser buttons existed (tests).</summary>
    internal int IncompleteMeasurements { get; private set; }

    /// <summary>How often the bar was laid out again because its browsers did not fit (tests).</summary>
    internal int OverflowRelayouts { get; private set; }

    /// <summary>Raises a bar that is not on top above other windows (no activation), never over a full-screen app.</summary>
    private void RaiseBar()
    {
        if (!_shell.IsFullScreenAppActive)
        {
            BarRaises++;
            _bar.BringToFront();
        }
    }

    /// <summary>How often the bar was raised above other windows (tests).</summary>
    internal int BarRaises { get; private set; }

    /// <summary>True while full-screen / presentation detection runs (tests).</summary>
    internal bool FullScreenPolling => _fullScreenTimer.IsEnabled;

    /// <summary>Applies the controller state: fade out then shrink to the edge strip, or grow then fade in.</summary>
    private void ApplyCollapsed()
    {
        if (_autoHide.IsCollapsed)
        {
            if (_shell.IsBarCollapsed || _bar.IsCollapsing)
            {
                return;
            }

            _bar.PlayCollapseAnimation(() =>
            {
                if (!_autoHide.IsCollapsed || _exiting || !_shell.IsCompact)
                {
                    return; // expanded again (or mode changed) during the fade
                }

                _shell.IsBarCollapsed = true;
                Layout();
            });
            return;
        }

        var wasCollapsed = _shell.IsBarCollapsed;
        _shell.IsBarCollapsed = false;
        if (wasCollapsed)
        {
            Layout();
        }

        RaiseBar(); // visible above other windows even when "always on top" is off

        _bar.PlayExpandAnimation();
    }

    // ---------------------------------------------------------------- tray / menu

    private void UpdateTray()
    {
        // The notification-area icon is always present (fixed behavior): it is the way back to a
        // hidden bar and the place to exit.
        if (_tray is null && !_exiting)
        {
            _tray = _trayFactory();
            _tray.Activated += (_, _) =>
            {
                RestoreFromTray();
                // Win+B, arrows, Enter: the user continues on the bar with the keyboard; a click does not.
                _keyboardEngaged = _tray?.LastActivationWasKeyboard == true;
            };
            _tray.MenuRequested += (_, _) => ShowMenu(fromTray: true);
            UpdateTrayTooltip();
            _tray.Visible = true;
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
        // Set before the menu takes the focus, so leaving the bar for its own menu keeps keyboard use;
        // a keyboard user who dismisses the menu (Esc) is still working on the bar.
        var keyboardBeforeMenu = _keyboardEngaged;
        _menuOpen = true;
        _barMenuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _barMenuOpen = false;

            // The focus returns to the bar after Closed. Keep keyboard use only if it really did (Esc);
            // a command that hid the bar or moved elsewhere ends it.
            _ = _bar.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                _keyboardEngaged = keyboardBeforeMenu && _bar.IsVisible && BarHasKeyboardFocus());
        };
        menu.PlacementTarget = _bar;
        menu.Placement = PlacementMode.MousePoint;
        try
        {
            menu.IsOpen = true;
        }
        catch
        {
            _menuOpen = false;
            _barMenuOpen = false;
            throw;
        }
    }

    /// <summary>One menu definition for both the notification-area menu and the compact bar menu.</summary>
    internal IReadOnlyList<TrayMenuItem> BuildMenuModel() =>
    [
        new(Loc.Instance["Menu_Show"], RestoreFromTray) { IsDefault = true },
        new(Loc.Instance["Menu_ShowToolbar"], ShowToolbar),
        new(Loc.Instance["Menu_Settings"], () => _shell.OpenOptionsCommand.Execute(null)),
        TrayMenuItem.Separator,
        new(Loc.Instance["Menu_Topmost"], () => _shell.ToggleTopmostCommand.Execute(null)) { IsChecked = _shell.IsTopmost },
        TrayMenuItem.Separator,
        new(Loc.Instance["Menu_Exit"], Exit),
    ];

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
