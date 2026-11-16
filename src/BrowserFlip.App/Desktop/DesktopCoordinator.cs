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
    private bool _trayOnly;          // floating bar hidden by closing the main window (tray only)
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
        _bar.TabInvoked += (_, _) => Expand(activate: true);
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
    /// A docked bar stays on top (it must be reachable from its edge even over maximized windows),
    /// except while a full-screen app or presentation runs. A floating bar follows the user setting.
    /// </summary>
    private bool BarTopmost => _shell.DockEnabled ? !_shell.IsFullScreenAppActive : _shell.EffectiveTopmost;

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
                _bar.Show();
                ScheduleLayout(); // re-measure once the window is live (a never-shown window measures short)
            }
        }
        else
        {
            _bar.Hide();
            _trayOnly = false; // the main window is the visible surface now; the bar shows normally next time
            _enterCollapsed = false;

            // Leaving compact mode always brings the main window back (never "tray only").
            ShowMainWindow(activate: false);
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

        // Closing the window minimizes BrowserFlip (Owner decision): it keeps running in the notification
        // area. Only "Exit" (tray / bar menu), logoff or shutdown end the process.
        e.Cancel = true;
        SaveMainBounds();
        _mainWindow.Dispatcher.BeginInvoke(MinimizeToTray);
    }

    /// <summary>
    /// Main window closed (X): never changes docking, edge, monitor, orientation or saved positions.
    /// Docked bar: stays at its edge, collapsed to the 3-5 px strip (expands on approach). Floating bar:
    /// hidden with the window (tray only); the tray brings it back where it was.
    /// </summary>
    internal void MinimizeToTray()
    {
        if (_exiting)
        {
            return;
        }

        _trayOnly = !_shell.DockEnabled;
        _enterCollapsed = _shell.DockEnabled;
        if (_shell.IsCompact)
        {
            ApplyMode();
        }
        else
        {
            _shell.IsCompact = true; // ApplyMode applies the flags above
        }
    }

    /// <summary>The bar is hidden with only the notification-area icon left (floating bar, window closed).</summary>
    internal bool IsTrayOnly => _trayOnly;

    /// <summary>BrowserFlip was started again while running: bring the current surface forward.</summary>
    internal void ShowRunningInstance()
    {
        if (!_exiting)
        {
            ShowMain();
        }
    }

    /// <summary>Shows the compact bar (expanded) and hides the main window: tray click / tray menu.</summary>
    internal void ShowToolbar()
    {
        if (_exiting)
        {
            return;
        }

        _trayOnly = false;
        if (!_shell.IsCompact)
        {
            _shell.IsCompact = true; // ApplyMode hides the main window and shows the bar
        }

        Expand(activate: true);
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

        // Full-screen detection suspends "on top": needed for the topmost setting and for a docked bar.
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
                var origin = saved ?? PixelRect.FromSize(work.Right - width - Px(64, monitor.Scale), work.Top + Px(64, monitor.Scale), width, height);
                var rect = PixelRect.FromSize(origin.Left, origin.Top, width, height);
                target = DockLayout.EnsureVisible(rect, _monitors.GetMonitors(), Px(32, monitor.Scale));
                target = DockLayout.ClampInto(target, _monitors.FromPoint(target.Left + (target.Width / 2), target.Top + (target.Height / 2)).WorkArea);
                _expandedRect = target;
                _tabRect = default;
                _triggerZone = default;
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
        var hoverTab = overTab && !_monitors.IsMouseButtonDown();

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
            _bar.Show();
            ScheduleLayout();
        }

        if (activate)
        {
            _keyboardEngaged = false; // our own activation is not keyboard use (T13); the tray handler re-evaluates it
            if (AllowActivation)
            {
                _ = _bar.Activate();
            }
        }
    }

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
                ShowToolbar();
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
        new(Loc.Instance["Menu_ShowToolbar"], ShowToolbar) { IsDefault = true },
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
