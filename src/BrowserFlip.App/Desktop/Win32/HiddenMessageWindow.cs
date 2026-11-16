using System.Windows.Interop;

namespace BrowserFlip.App.Desktop.Win32;

/// <summary>
/// Creates a hidden, zero-size, top-level <see cref="HwndSource"/> (WS_POPUP, never shown).
/// It is deliberately NOT parented to HWND_MESSAGE: message-only windows do not receive
/// broadcasts such as WM_DISPLAYCHANGE, WM_SETTINGCHANGE or "TaskbarCreated".
/// Must be called on a thread with a WPF <see cref="System.Windows.Threading.Dispatcher"/>.
/// </summary>
internal static class HiddenMessageWindow
{
    internal static HwndSource Create(string name, HwndSourceHook hook)
    {
        var parameters = new HwndSourceParameters(name)
        {
            // No WS_VISIBLE: the window is never shown. WS_EX_TOOLWINDOW keeps it out of Alt+Tab even if something shows it.
            WindowStyle = NativeMethods.WsPopup,
            ExtendedWindowStyle = NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate,
            PositionX = 0,
            PositionY = 0,
            Width = 0,
            Height = 0,
            ParentWindow = IntPtr.Zero,
        };

        var source = new HwndSource(parameters);
        source.AddHook(hook);
        return source;
    }
}
