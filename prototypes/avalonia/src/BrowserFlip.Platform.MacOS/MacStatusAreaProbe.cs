using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using BrowserFlip.Platform.MacOS.Interop;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.MacOS;

/// <summary>
/// Reads BrowserFlip's own <c>NSStatusItem</c> back out of the live menu bar, and delivers a real
/// left click to it.
/// <para>
/// Everything here is documented AppKit: <c>-[NSApplication windows]</c>, <c>-[NSView subviews]</c>,
/// the public <c>NSStatusBarButton</c> class, <c>+[NSEvent mouseEventWithType:…]</c> and
/// <c>-[NSApplication postEvent:atStart:]</c>. No private selector, no Accessibility grant, no
/// Screen Recording grant, and no synthetic <c>CGEvent</c>: the event is posted into this
/// process's own queue, which is why it needs no permission at all.
/// </para>
/// <para>
/// Why it exists: the menu-bar item was reported as PASS because BrowserFlip had called
/// <c>TrayIcon.SetIcons</c>. That is a statement about BrowserFlip, not about macOS, and it
/// survived a defect the Owner found by hand (MAC-D6, the menu not opening on click). This type
/// replaces that inference with a measurement.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacStatusAreaProbe : IStatusAreaProbe
{
    private const string Libobjc = "/usr/lib/libobjc.A.dylib";

    /// <summary>NSEventTypeLeftMouseDown.</summary>
    private const ulong LeftMouseDown = 1;

    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    private static extern IntPtr MouseEvent(
        IntPtr receiver, IntPtr selector, ulong type, double locationX, double locationY,
        ulong modifierFlags, double timestamp, long windowNumber, IntPtr context,
        long eventNumber, long clickCount, float pressure);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    private static extern ObjC.NSRect SendRect(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector);

    /// <inheritdoc />
    public StatusAreaFacts Inspect(string expectedToolTip)
    {
        ArgumentNullException.ThrowIfNull(expectedToolTip);
        if (!ObjC.EnsureAppKit())
        {
            return StatusAreaFacts.NotProbed("AppKit could not be loaded");
        }

        var detail = new StringBuilder();
        var buttons = FindStatusButtons(detail);
        var mine = buttons.Find(b =>
            string.Equals(ObjC.ToManaged(ObjC.Send(b, ObjC.Sel("toolTip"))), expectedToolTip, StringComparison.Ordinal));

        if (mine == ObjC.Nil)
        {
            return StatusAreaFacts.NotProbed(
                $"no NSStatusBarButton with tooltip \"{expectedToolTip}\"; {buttons.Count} status button(s) in this process; {detail}");
        }

        var window = ObjC.Send(mine, ObjC.Sel("window"));
        var hidden = SendBool(mine, ObjC.Sel("isHidden"));
        var enabled = SendBool(mine, ObjC.Sel("isEnabled"));
        var windowVisible = SendBool(window, ObjC.Sel("isVisible"));
        var image = ObjC.Send(mine, ObjC.Sel("image"));
        var bounds = SendRect(mine, ObjC.Sel("bounds"));

        return new StatusAreaFacts(
            ItemFound: true,
            ItemVisible: windowVisible && !hidden,
            HasImage: image != ObjC.Nil,
            ToolTip: expectedToolTip,
            Detail: string.Create(CultureInfo.InvariantCulture,
                $"NSStatusBarButton in the live menu bar: window visible={windowVisible}, hidden={hidden}, "
                + $"enabled={enabled}, image={(image != ObjC.Nil ? "present" : "MISSING")}, "
                + $"size={bounds.Width:F0}x{bounds.Height:F0}"));
    }

    /// <inheritdoc />
    public bool TryClick(string expectedToolTip, out string detail)
    {
        ArgumentNullException.ThrowIfNull(expectedToolTip);
        if (!ObjC.EnsureAppKit())
        {
            detail = "AppKit could not be loaded";
            return false;
        }

        var ignored = new StringBuilder();
        var button = FindStatusButtons(ignored).Find(b =>
            string.Equals(ObjC.ToManaged(ObjC.Send(b, ObjC.Sel("toolTip"))), expectedToolTip, StringComparison.Ordinal));
        if (button == ObjC.Nil)
        {
            detail = $"no NSStatusBarButton with tooltip \"{expectedToolTip}\"";
            return false;
        }

        var window = ObjC.Send(button, ObjC.Sel("window"));
        var windowNumber = ObjC.SendLong(window, ObjC.Sel("windowNumber"));
        var bounds = SendRect(button, ObjC.Sel("bounds"));
        var frame = SendRect(button, ObjC.Sel("frame"));

        var mouseDown = MouseEvent(
            ObjC.GetClass("NSEvent"),
            ObjC.Sel("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
            LeftMouseDown,
            frame.X + (bounds.Width / 2),
            frame.Y + (bounds.Height / 2),
            0UL, 0.0, windowNumber, ObjC.Nil, 0, 1, 1.0f);

        if (mouseDown == ObjC.Nil)
        {
            detail = "NSEvent could not be built";
            return false;
        }

        // Posted, not sent: it goes to the back of this application's own event queue and is
        // then dispatched by -[NSApplication sendEvent:] exactly as the window server's own
        // click would be, so the whole routing path is under test and not just the button.
        var app = ObjC.Send(ObjC.GetClass("NSApplication"), ObjC.Sel("sharedApplication"));
        ObjC.Send(app, ObjC.Sel("postEvent:atStart:"), mouseDown, IntPtr.Zero);

        detail = string.Create(CultureInfo.InvariantCulture,
            $"posted a real NSEventTypeLeftMouseDown at ({frame.X + (bounds.Width / 2):F0},{frame.Y + (bounds.Height / 2):F0}) "
            + $"to window {windowNumber} through -[NSApplication postEvent:atStart:]");
        return true;
    }

    /// <summary>Every <c>NSStatusBarButton</c> this process owns, found through public API.</summary>
    private static List<IntPtr> FindStatusButtons(StringBuilder detail)
    {
        var app = ObjC.Send(ObjC.GetClass("NSApplication"), ObjC.Sel("sharedApplication"));
        var statusButtonClass = ObjC.GetClass("NSStatusBarButton");
        var found = new List<IntPtr>();
        var windows = ObjC.ArrayItems(ObjC.Send(app, ObjC.Sel("windows")));
        detail.Append(CultureInfo.InvariantCulture, $"scanned {windows.Count} NSApp window(s)");

        foreach (var window in windows)
        {
            Collect(ObjC.Send(window, ObjC.Sel("contentView")), statusButtonClass, found);
        }

        return found;
    }

    /// <summary>Depth-first walk of a view tree, collecting views of <paramref name="cls"/>.</summary>
    private static void Collect(IntPtr view, IntPtr cls, List<IntPtr> into)
    {
        if (view == ObjC.Nil)
        {
            return;
        }

        if (ObjC.IsKindOf(view, cls))
        {
            into.Add(view);
        }

        foreach (var child in ObjC.ArrayItems(ObjC.Send(view, ObjC.Sel("subviews"))))
        {
            Collect(child, cls, into);
        }
    }
}
