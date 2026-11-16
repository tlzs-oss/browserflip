using System.ComponentModel;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
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
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Activated += (_, _) => (DataContext as MainViewModel)?.OnActivated();
        Deactivated += (_, _) => (DataContext as MainViewModel)?.OnDeactivated();
        DpiChanged += (_, _) => LimitHeightToWorkArea();
        LocationChanged += (_, _) => LimitHeightToWorkArea();
        Loaded += (_, _) => LimitHeightToWorkArea();
    }

    /// <summary>
    /// The window sizes itself to its content (SizeToContent=Height). Cap it at the work area of the
    /// monitor it is on; beyond that the content scrolls instead of running off-screen.
    /// </summary>
    internal void LimitHeightToWorkArea()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return;
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var info = new Desktop.Win32.NativeMethods.MONITORINFOEXW
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Desktop.Win32.NativeMethods.MONITORINFOEXW>(),
        };
        var monitor = Desktop.Win32.NativeMethods.MonitorFromWindow(hwnd, Desktop.Win32.NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero || !Desktop.Win32.NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            return;
        }

        var workHeight = info.rcWork.Bottom - info.rcWork.Top;
        var scaleY = source.CompositionTarget.TransformToDevice.M22;
        var max = Math.Max(MinHeight, (workHeight / scaleY) - 16);
        if (Math.Abs(MaxHeight - max) > 0.5)
        {
            MaxHeight = max;
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => ApplyLanguage();

    /// <summary>xml:lang + per-language font stack so CJK text uses the correct regional glyphs.</summary>
    private void ApplyLanguage()
    {
        Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag);
        FontFamily = new FontFamily(Loc.Instance.FontFamilyName);
    }
}
