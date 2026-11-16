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
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
            {
                LimitHeightToWorkArea(); // a minimized window sits at -32000; measure only when restored
            }
        };
    }

    private bool _inSizeMove;

    /// <summary>
    /// A content-sized utility window has nothing to gain from maximizing (and the height cap would stop
    /// it short of the taskbar). Removing WS_MAXIMIZEBOX disables the button, caption double-click,
    /// Win+Up and snap-to-maximize together; width resizing stays available.
    /// </summary>
    private void RemoveMaximizeBox()
    {
        const int GwlStyle = -16;
        const long WsMaximizeBox = 0x00010000;
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtrW(hwnd, GwlStyle).ToInt64();
        _ = NativeMethods.SetWindowLongPtrW(hwnd, GwlStyle, new IntPtr(style & ~WsMaximizeBox));
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
        if (msg == WmEnterSizeMove)
        {
            _inSizeMove = true;
        }
        else if (msg == WmExitSizeMove)
        {
            _inSizeMove = false;

            // A move or resize may have ended under the taskbar (or grown during the drag): fix it once now.
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, KeepBottomInWorkArea);
        }

        if (msg == WmExitSizeMove && SizeToContent == SizeToContent.Manual)
        {
            SizeToContent = SizeToContent.Height;
        }

        return IntPtr.Zero;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => ApplyLanguage();

    /// <summary>xml:lang + per-language font stack so CJK text uses the correct regional glyphs.</summary>
    private void ApplyLanguage()
    {
        Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag);
        FontFamily = new FontFamily(Loc.Instance.FontFamilyName);
    }
}
