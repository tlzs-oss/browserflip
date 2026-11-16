using System.Runtime.InteropServices;
using BrowserFlip.App.Desktop.Win32;
using static BrowserFlip.App.Desktop.Win32.Win32TrayIcon;

namespace BrowserFlip.Core.Tests.Desktop;

public sealed class Win32TrayIconTests
{
    [Fact]
    public void NOTIFYICONDATAW_has_the_full_vista_plus_layout()
    {
        var size = Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>();
        if (IntPtr.Size == 8)
        {
            Assert.Equal(976, size);
            Assert.Equal(8, (int)Marshal.OffsetOf<NativeMethods.NOTIFYICONDATAW>(nameof(NativeMethods.NOTIFYICONDATAW.hWnd)));
            Assert.Equal(32, (int)Marshal.OffsetOf<NativeMethods.NOTIFYICONDATAW>(nameof(NativeMethods.NOTIFYICONDATAW.hIcon)));
            Assert.Equal(40, (int)Marshal.OffsetOf<NativeMethods.NOTIFYICONDATAW>(nameof(NativeMethods.NOTIFYICONDATAW.szTip)));
            Assert.Equal(816, (int)Marshal.OffsetOf<NativeMethods.NOTIFYICONDATAW>(nameof(NativeMethods.NOTIFYICONDATAW.uVersion)));
            Assert.Equal(952, (int)Marshal.OffsetOf<NativeMethods.NOTIFYICONDATAW>(nameof(NativeMethods.NOTIFYICONDATAW.guidItem)));
            Assert.Equal(968, (int)Marshal.OffsetOf<NativeMethods.NOTIFYICONDATAW>(nameof(NativeMethods.NOTIFYICONDATAW.hBalloonIcon)));
        }
        else
        {
            Assert.Equal(956, size);
        }
    }

    [Theory]
    [InlineData(NativeMethods.NinSelect, true, "Activate")]
    [InlineData(NativeMethods.NinKeySelect, true, "Activate")]
    [InlineData(NativeMethods.WmLButtonUp, true, "None")] // v4 also sends NIN_SELECT: fire once.
    [InlineData(NativeMethods.WmContextMenu, true, "Menu")]
    [InlineData(NativeMethods.WmRButtonUp, true, "None")] // v4 also sends WM_CONTEXTMENU.
    [InlineData(NativeMethods.WmLButtonUp, false, "Activate")]
    [InlineData(NativeMethods.WmRButtonUp, false, "Menu")]
    [InlineData(NativeMethods.NinSelect, false, "None")]
    [InlineData(0x0200 /* WM_MOUSEMOVE */, true, "None")]
    public void Classify_maps_callback_events_without_double_firing(int notifyEvent, bool version4, string expected)
        => Assert.Equal(Enum.Parse<TrayAction>(expected), Classify(notifyEvent, version4));

    [Fact]
    public void Truncate_limits_tooltip_to_127_chars_without_splitting_surrogates()
    {
        Assert.Equal(string.Empty, Truncate(null));
        Assert.Equal("BrowserFlip", Truncate("BrowserFlip"));
        Assert.Equal(127, Truncate(new string('x', 500)).Length);

        var withPair = new string('a', 126) + "\U0001F600" + "tail";
        var truncated = Truncate(withPair);
        Assert.Equal(126, truncated.Length);
        Assert.False(char.IsHighSurrogate(truncated[^1]));
    }

    [Fact]
    public void Hidden_icon_can_be_configured_and_disposed_twice() => Sta.Run(() =>
    {
        // Never set Visible = true here: tests must not put an icon in the notification area.
        var tray = new Win32TrayIcon();
        Assert.NotEqual(IntPtr.Zero, tray.Handle);
        Assert.False(tray.Visible);

        tray.ToolTip = new string('t', 300);
        Assert.Equal(127, tray.ToolTip.Length);
        tray.UpdateIcon();
        tray.Visible = false;

        tray.Dispose();
        tray.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tray.ToolTip = "x");
    });

    [Fact]
    public void Custom_icon_factory_receives_a_positive_size_and_zero_falls_back() => Sta.Run(() =>
    {
        var sizes = new List<int>();
        using var tray = new Win32TrayIcon(size =>
        {
            sizes.Add(size);
            return IntPtr.Zero; // fall back to the exe / system icon
        });

        tray.UpdateIcon();

        Assert.Equal(2, sizes.Count);
        Assert.All(sizes, s => Assert.True(s >= 16));
    });

    [Fact]
    public void Members_require_the_creating_thread() => Sta.Run(() =>
    {
        using var tray = new Win32TrayIcon();

        var error = Task.Run(() => Record.Exception(() => tray.ToolTip = "other thread")).Result;

        Assert.IsType<InvalidOperationException>(error);
    });
}
