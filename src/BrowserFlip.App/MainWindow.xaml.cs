using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using BrowserFlip.App.Desktop.Win32;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;

namespace BrowserFlip.App;

public partial class MainWindow : Window
{
    /// <summary>Desktop-experience state (always on top, compact mode, options).</summary>
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(MainWindow), new PropertyMetadata(null));

    public ShellViewModel? Shell
    {
        get => (ShellViewModel?)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        ThemeManager.UseFluent(this); // Windows 11 control styles (ComboBox, CheckBox, Expander) in light / dark
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.Instance.PropertyChanged -= OnLanguageChanged;
        SourceInitialized += (_, _) =>
        {
            ThemeManager.ApplyTitleBar(this);
            RemoveMaximizeBox();
            LimitHeightToWorkArea(); // before the first frame: never start taller than the work area
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
        };
        Activated += (_, _) => (DataContext as MainViewModel)?.OnActivated();
        Deactivated += (_, _) => (DataContext as MainViewModel)?.OnDeactivated();
        DpiChanged += (_, _) => LimitHeightToWorkArea();
        LocationChanged += (_, _) => LimitHeightToWorkArea();
        BaseMinWidth = MinWidth;
        Loaded += (_, _) =>
        {
            LimitHeightToWorkArea();
            KeepBottomInWorkArea();
        };
        SizeChanged += (_, e) =>
        {
            // Options expanded / message shown: grow upwards if needed. Deferred until the resize (and the
            // expander's animation step) has been applied to the HWND. Not while the user drags a border.
            if (e.HeightChanged && e.NewSize.Height > e.PreviousSize.Height && !_inSizeMove)
            {
                _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, KeepBottomInWorkArea);
            }
        };
        IsVisibleChanged += (_, _) => FollowLanguageWidthIfStale();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Maximized)
            {
                // Maximize is disabled in the UI, but a shortcut set to "Maximized" (SW_MAXIMIZE) can still
                // request it: the content-sized window stays normal.
                _ = Dispatcher.BeginInvoke(() => WindowState = WindowState.Normal);
            }
            else if (WindowState == WindowState.Normal)
            {
                LimitHeightToWorkArea(); // a minimized window sits at -32000; measure only when restored
                FollowLanguageWidthIfStale();
            }
        };
    }

    private bool _inSizeMove;
    private bool _resizedInSizeMove;
    private int _sizeMoveStartWidth;     // physical pixels, at WM_ENTERSIZEMOVE
    private double? _widthBeforeCap;     // width wanted before a narrower work area capped it (BF-07)
    private bool _languageWidthStale;    // the language changed while the window was hidden / minimized

    /// <summary>
    /// The app changed the width to follow the language (BF-07). The coordinator keeps the right edge (and with it
    /// the header tools) inside the work area.
    /// </summary>
    internal event EventHandler? AutomaticWidthChanged;

    /// <summary>
    /// The width the window wants: the current width, or the wider one it had before a narrower work area capped it.
    /// Saved instead of the capped width so a user's width survives a stay on a small screen (BF-07).
    /// </summary>
    internal double WantedWidth => _widthBeforeCap ?? Width;

    /// <summary>Default width from MainWindow.xaml (DIP).</summary>
    internal const double DefaultWidth = 520;

    /// <summary>
    /// Reserve added to the measured header width (DIP): text in Display formatting mode is hinted per device pixel
    /// size, so the same text can be a few DIP wider at another scale than where it was measured.
    /// </summary>
    private const double HeaderReserve = 8;

    /// <summary>
    /// Work-area width (DIP) of the monitor the window is on. Set by the desktop coordinator from its monitor service
    /// (the same Win32 monitors in the app; fakes in tests); null = ask Win32 directly.
    /// </summary>
    internal Func<double?>? WorkAreaWidthProvider { get; set; }

    /// <summary>MinWidth from XAML; lowered only when a work area is narrower (BF-07).</summary>
    internal double BaseMinWidth { get; }

    /// <summary>
    /// True while the width is chosen by the app (first run, upgraded old setting): it follows the header when the
    /// language changes. False once the user resized the window: their width is kept (BF-07).
    /// </summary>
    internal bool WidthIsAutomatic { get; set; } = true;

    /// <summary>
    /// BF-07: the width at which the header shows icon, name, tagline and every tool on one line in the current
    /// language and font, measured (never a fixed per-language number); at least the XAML default width.
    /// </summary>
    internal double PreferredWidth()
    {
        var content = (FrameworkElement)RootScroller.Content;
        if (!IsLoaded)
        {
            // Never laid out yet (start-up, before the first show): the language box has not picked up its selected
            // item, so it would measure ~40 DIP too narrow. One layout pass of the content resolves it (no dispatcher
            // pump: nothing else runs re-entrantly). UpdateLayout runs this thread's shared layout queue, so other
            // windows with pending layout (the compact bar during start-up) may be laid out here too: layout only,
            // and the bar keeps its own guard for a measurement taken before its browser buttons exist (BF-05).
            RootScroller.Measure(new Size(Math.Max(MinWidth, Width - FrameWidth()), double.PositiveInfinity));
            RootScroller.Arrange(new Rect(RootScroller.DesiredSize));
            RootScroller.UpdateLayout();
        }

        Header.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var needed = Header.SingleLineWidth + content.Margin.Left + content.Margin.Right + FrameWidth() + HeaderReserve;
        Header.InvalidateMeasure(); // the next layout pass measures it again at the real width (and picks its mode)
        return Math.Ceiling(Math.Max(DefaultWidth, needed));
    }

    /// <summary>Sets the app-chosen width: the preferred width, never wider than <paramref name="maxWidth"/> (work area, DIP).</summary>
    internal void ApplyPreferredWidth(double maxWidth)
    {
        _widthBeforeCap = null;
        MinWidth = Math.Min(BaseMinWidth, maxWidth);
        Width = Math.Max(MinWidth, Math.Min(PreferredWidth(), maxWidth));
    }

    /// <summary>Width of the window frame (window width minus client width, incl. invisible resize borders), DIP.</summary>
    private double FrameWidth()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = PresentationSource.FromVisual(this);
        if (hwnd != IntPtr.Zero && source?.CompositionTarget is not null
            && NativeMethods.GetWindowRect(hwnd, out var window) && NativeMethods.GetClientRect(hwnd, out var client))
        {
            var pixels = (window.Right - window.Left) - (client.Right - client.Left);
            if (pixels is > 0 and < 200)
            {
                return pixels / source.CompositionTarget.TransformToDevice.M11;
            }
        }

        return 16;
    }
    private readonly Desktop.MainWindowMoveTracker _moveTracker = new();

    /// <summary>The user is dragging the window (WM_MOVING inside a move loop).</summary>
    internal event EventHandler? UserMoving;

    /// <summary>
    /// The user finished dragging the window (moved, not resized). The argument is the window rectangle
    /// before the drag (physical pixels), so a snap or dock can be undone.
    /// </summary>
    internal event EventHandler<Desktop.PixelRect>? UserMoveCompleted;

    /// <summary>
    /// A content-sized utility window has nothing to gain from maximizing (and the height cap would stop
    /// it short of the taskbar). Removing WS_MAXIMIZEBOX disables the button, caption double-click,
    /// Win+Up and snap-to-maximize together; width resizing stays available.
    /// </summary>
    private void RemoveMaximizeBox()
    {
        const int GwlStyle = -16;
        const int WsMaximizeBox = 0x00010000;
        var hwnd = new WindowInteropHelper(this).Handle;

        // Called at SourceInitialized (before the first show), so no SWP_FRAMECHANGED is needed.
        var style = NativeMethods.GetWindowLongW(hwnd, GwlStyle);
        _ = NativeMethods.SetWindowLongW(hwnd, GwlStyle, style & ~WsMaximizeBox);
    }

    /// <summary>Re-applies the height cap and keeps the bottom above the taskbar (display / work-area change).</summary>
    public void RefitToWorkArea()
    {
        LimitHeightToWorkArea();
        KeepBottomInWorkArea();
    }

    /// <summary>
    /// The window sizes itself to its content (SizeToContent=Height). Cap it at the work area of the
    /// monitor it is on; beyond that the content scrolls instead of running off-screen.
    /// </summary>
    internal void LimitHeightToWorkArea()
    {
        var source = PresentationSource.FromVisual(this);
        if (WindowState != WindowState.Normal || source?.CompositionTarget is null || !TryGetWorkArea(out var work))
        {
            return;
        }

        var scaleY = source.CompositionTarget.TransformToDevice.M22;
        var max = Math.Max(MinHeight, ((work.Bottom - work.Top) / scaleY) - 16);
        if (Math.Abs(MaxHeight - max) > 0.5)
        {
            MaxHeight = max;
        }

        // BF-07: never wider than the work area (narrow screen / high DPI); MinWidth gives way and the header
        // degrades (wraps, then two rows) instead. Not inside a drag (no resizing inside the move loop): once at
        // its end. The wanted width comes back when the window is on a wide enough work area again.
        if (_inSizeMove || !TryGetWorkAreaWidth(out var maxWidth))
        {
            return;
        }

        var minWidth = Math.Min(BaseMinWidth, maxWidth);
        if (Math.Abs(MinWidth - minWidth) > 0.5)
        {
            MinWidth = minWidth;
        }

        if (Width > maxWidth + 0.5)
        {
            _widthBeforeCap ??= Width;
            Width = maxWidth;
        }
        else if (_widthBeforeCap is { } wanted && Width + 0.5 < Math.Min(wanted, maxWidth))
        {
            Width = Math.Min(wanted, maxWidth);
            if (wanted <= maxWidth + 0.5)
            {
                _widthBeforeCap = null;
            }

            AutomaticWidthChanged?.Invoke(this, EventArgs.Empty); // grew back: keep the tools on screen
        }
    }

    /// <summary>The width was set from settings or a saved rectangle: no earlier cap applies any more.</summary>
    internal void ForgetCappedWidth() => _widthBeforeCap = null;

    /// <summary>Work-area width of the window's monitor in DIP, when the window has a handle.</summary>
    private bool TryGetWorkAreaWidth(out double width)
    {
        width = 0;
        if (WorkAreaWidthProvider is not null)
        {
            width = WorkAreaWidthProvider() ?? 0;
            return width > 0;
        }

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null || !TryGetWorkArea(out var work))
        {
            return false;
        }

        width = (work.Right - work.Left) / source.CompositionTarget.TransformToDevice.M11;
        return true;
    }

    /// <summary>When the window grows past the bottom of the work area (taskbar), move it up just enough.</summary>
    private void KeepBottomInWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || WindowState != WindowState.Normal || !TryGetWorkArea(out var work)
            || !NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Bottom <= work.Bottom)
        {
            return;
        }

        var top = Math.Max(work.Top, work.Bottom - (rect.Bottom - rect.Top));
        _ = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.Left, top, 0, 0,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | 0x0001 /* SWP_NOSIZE */);
    }

    private bool TryGetWorkArea(out NativeMethods.RECT work)
    {
        work = default;
        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new NativeMethods.MONITORINFOEXW { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEXW>() };
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        // Use the monitor under the title bar (not the one with the largest overlap): a window that grows
        // downwards across stacked monitors must still stay above *its* monitor's taskbar.
        var titleBar = new NativeMethods.POINT { X = rect.Left + ((rect.Right - rect.Left) / 2), Y = rect.Top + 8 };
        var monitor = NativeMethods.MonitorFromPoint(titleBar, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        work = info.rcWork;
        return true;
    }

    /// <summary>
    /// A manual resize turns SizeToContent off (WPF behaviour). When the user finishes resizing, keep
    /// their width but let the height follow the content again (WM_EXITSIZEMOVE).
    /// </summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmEnterSizeMove = 0x0231;
        const int WmExitSizeMove = 0x0232;
        const int WmMoving = 0x0216;
        const int WmSizing = 0x0214;
        if (msg == WmEnterSizeMove)
        {
            _inSizeMove = true;
            _resizedInSizeMove = false;
            var hasStart = NativeMethods.GetWindowRect(hwnd, out var start);
            _sizeMoveStartWidth = hasStart ? start.Right - start.Left : 0;
            _moveTracker.Enter(hasStart ? start.ToPixelRect() : default);
        }
        else if (msg == WmMoving)
        {
            if (_moveTracker.Moving())
            {
                UserMoving?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (msg == WmSizing)
        {
            _moveTracker.Sizing();
            _resizedInSizeMove = true;
        }
        else if (msg == WmExitSizeMove)
        {
            _inSizeMove = false;
            if (_resizedInSizeMove && NativeMethods.GetWindowRect(hwnd, out var end) && end.Right - end.Left != _sizeMoveStartWidth)
            {
                // The user chose a width (not just dragged the top / bottom border, whose height is not kept):
                // keep it from now on (BF-07).
                WidthIsAutomatic = false;
                _widthBeforeCap = null;
            }

            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, LimitHeightToWorkArea);

            if (_moveTracker.Exit() is { } before)
            {
                // After Windows has finished the move (and any snap): docking may replace the window (BF-01).
                _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    () => UserMoveCompleted?.Invoke(this, before));
            }

            // A move or resize may have ended under the taskbar (or grown during the drag): fix it once now.
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, KeepBottomInWorkArea);
        }

        if (msg == WmExitSizeMove && SizeToContent == SizeToContent.Manual)
        {
            SizeToContent = SizeToContent.Height;
        }

        return IntPtr.Zero;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyLanguage();

        // BF-07: an app-chosen width follows the header of the new language (the header itself re-lays out on
        // its own either way: it wraps or goes to two rows rather than overlap). Hidden / minimized: when shown.
        _languageWidthStale = WidthIsAutomatic;
        FollowLanguageWidthIfStale();
    }

    private void FollowLanguageWidthIfStale()
    {
        // Not while snapped (Win+arrow: SizeToContent is Manual with the snapped size): changing only the width would
        // leave an odd shape. Applied once the window is back to its normal content-sized state.
        if (!_languageWidthStale || !WidthIsAutomatic || !IsVisible || WindowState != WindowState.Normal
            || SizeToContent == SizeToContent.Manual)
        {
            return;
        }

        _languageWidthStale = false;

        // After the bindings have picked up the new language.
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (WidthIsAutomatic && WindowState == WindowState.Normal && TryGetWorkAreaWidth(out var max))
            {
                ApplyPreferredWidth(max);
                AutomaticWidthChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    /// <summary>xml:lang + per-language font stack so CJK text uses the correct regional glyphs.</summary>
    private void ApplyLanguage()
    {
        Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag);
        FontFamily = new FontFamily(Loc.Instance.FontFamilyName);
    }
}
