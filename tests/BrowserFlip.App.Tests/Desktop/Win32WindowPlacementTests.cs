using System.ComponentModel;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Desktop.Win32;

namespace BrowserFlip.Core.Tests.Desktop;

public sealed class Win32WindowPlacementTests
{
    [Fact]
    public void SetBounds_then_GetBounds_round_trips_on_a_hidden_window() => Sta.Run(() =>
    {
        // Hidden WS_POPUP window: SetWindowPos without SWP_SHOWWINDOW never shows it.
        using var window = HiddenMessageWindow.Create("BrowserFlip.Tests.Placement", static (IntPtr _, int _, IntPtr _, IntPtr _, ref bool _) => IntPtr.Zero);
        var placement = new Win32WindowPlacement();
        var target = PixelRect.FromSize(40, 50, 320, 240);

        placement.SetBounds(window.Handle, target);

        Assert.Equal(target, placement.GetBounds(window.Handle));
        Assert.False(NativeMethods.IsWindowVisible(window.Handle));
    });

    [Fact]
    public void Zero_handle_is_rejected()
    {
        var placement = new Win32WindowPlacement();

        Assert.Throws<ArgumentException>(() => placement.GetBounds(IntPtr.Zero));
        Assert.Throws<ArgumentException>(() => placement.SetBounds(IntPtr.Zero, PixelRect.FromSize(0, 0, 1, 1)));
    }

    [Fact]
    public void Destroyed_window_throws_Win32Exception() => Sta.Run(() =>
    {
        var window = HiddenMessageWindow.Create("BrowserFlip.Tests.Destroyed", static (IntPtr _, int _, IntPtr _, IntPtr _, ref bool _) => IntPtr.Zero);
        var handle = window.Handle;
        window.Dispose();

        Assert.Throws<Win32Exception>(() => new Win32WindowPlacement().GetBounds(handle));
    });
}
