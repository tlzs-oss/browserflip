using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using BrowserFlip.App.Desktop.Win32;

namespace BrowserFlip.App.Controls;

/// <summary>
/// Menu bar drop-downs (final UI round, UI-01).
/// <para>
/// Placement: a drop-down opens left-aligned under its menu item. <c>Placement="Bottom"</c> follows the Windows
/// handedness setting (<see cref="SystemParameters.MenuDropAlignment"/>): with "right-handed" it right-aligns the menu,
/// so in the narrow Compact window the View menu grew out to the LEFT of the window. The second placement (right-aligned)
/// is only used when the first does not fit on the monitor; WPF keeps the popup on the screen either way.
/// </para>
/// <para>
/// Background: the drop-down is an opaque (non-layered) window with no transparent shadow margin, so no capture method
/// or remote session can show a black band around it; on Windows 11 DWM rounds its corners (small radius, like the
/// system menus) and draws its border.
/// </para>
/// </summary>
internal static class MenuPopup
{
    /// <summary>Gap between the menu bar item and its drop-down (DIP).</summary>
    private const double Gap = 2;

    public static readonly CustomPopupPlacementCallback LeftAligned = (popupSize, targetSize, _) =>
    [
        new CustomPopupPlacement(new Point(0, targetSize.Height + Gap), PopupPrimaryAxis.Horizontal),
        new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height + Gap), PopupPrimaryAxis.Horizontal),
    ];

    /// <summary>Popup.Opened: ask DWM for the small rounded corners of Windows 11 menus (no-op elsewhere).</summary>
    internal static void RoundCorners(object? sender, EventArgs e)
    {
        if (sender is Popup { Child: { } child } && PresentationSource.FromVisual(child) is HwndSource source)
        {
            var preference = 3; // DWMWCP_ROUNDSMALL
            _ = NativeMethods.DwmSetWindowAttribute(source.Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
        }
    }
}
