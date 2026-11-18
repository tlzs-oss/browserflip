using System.Windows;
using System.Windows.Controls.Primitives;

namespace BrowserFlip.App.Controls;

/// <summary>
/// Menu bar drop-downs (final UI round, UI-01). A drop-down opens left-aligned under its menu item.
/// <c>Placement="Bottom"</c> follows the Windows handedness setting (<see cref="SystemParameters.MenuDropAlignment"/>):
/// with "right-handed" it right-aligned the menu, so in the narrow Compact window the View menu grew out to the LEFT of
/// the window. The second placement (right-aligned) is only used when the first does not fit on the monitor; WPF keeps
/// the popup on the screen either way. The drop-down has no transparent shadow margin any more (App.xaml): that margin
/// was what screenshots taken with PrintWindow showed as a black band.
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
}
