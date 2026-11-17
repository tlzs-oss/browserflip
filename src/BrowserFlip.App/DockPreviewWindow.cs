using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BrowserFlip.App.Desktop.Win32;

namespace BrowserFlip.App;

/// <summary>
/// Translucent outline shown while the compact bar is dragged near a screen edge: where it will dock
/// and in which orientation. Click-through, never activated, never in the taskbar or Alt+Tab.
/// </summary>
internal sealed class DockPreviewWindow : Window
{
    public DockPreviewWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "BrowserFlip.DockPreview";

        var border = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2) };
        border.SetResourceReference(Border.BorderBrushProperty, "BfAccentBrush");
        var fill = new Border { CornerRadius = new CornerRadius(5), Opacity = 0.22 };
        fill.SetResourceReference(Border.BackgroundProperty, "BfAccentBrush");
        border.Child = fill;
        Content = border;

        SourceInitialized += (_, _) =>
        {
            const int GwlExStyle = -20;
            const int WsExTransparent = 0x00000020;
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongW(hwnd, GwlExStyle);
            _ = NativeMethods.SetWindowLongW(hwnd, GwlExStyle, style | WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate);
        };
    }
}
