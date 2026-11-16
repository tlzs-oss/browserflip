namespace BrowserFlip.App.Desktop;

/// <summary>A rectangle in physical screen pixels (virtual-screen coordinates).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public PixelRect Inflate(int dx, int dy) => new(Left - dx, Top - dy, Right + dx, Bottom + dy);

    public PixelRect Intersect(PixelRect other)
    {
        var r = new PixelRect(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
        return r.IsEmpty ? default : r;
    }

    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public static PixelRect FromSize(int left, int top, int width, int height) => new(left, top, left + width, top + height);
}

/// <summary>A display monitor. All rectangles are physical pixels.</summary>
/// <param name="DeviceName">Stable-ish identifier such as <c>\\.\DISPLAY1</c>.</param>
/// <param name="Bounds">Full monitor rectangle.</param>
/// <param name="WorkArea">Monitor rectangle minus the taskbar / app bars.</param>
/// <param name="Scale">Effective DPI / 96 (1.0, 1.25, 1.5, 2.0 ...).</param>
/// <param name="IsPrimary">True for the primary monitor.</param>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary);

/// <summary>Monitor / cursor / environment queries (Win32). Implemented by <c>Win32MonitorService</c>.</summary>
public interface IMonitorService : IDisposable
{
    /// <summary>All monitors; never empty.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Monitor nearest to the point.</summary>
    MonitorInfo FromPoint(int x, int y);

    /// <summary>Monitor nearest to the window.</summary>
    MonitorInfo FromWindow(IntPtr hwnd);

    /// <summary>Current cursor position in physical pixels, or <c>null</c> if unavailable (e.g. secure desktop).</summary>
    (int X, int Y)? GetCursorPosition();

    /// <summary>True when a full-screen app, presentation mode or "busy" state is active (SHQueryUserNotificationState).</summary>
    bool IsFullScreenOrPresentationActive();

    /// <summary>True when any mouse button is currently pressed (GetAsyncKeyState).</summary>
    bool IsMouseButtonDown();

    /// <summary>
    /// Raised (on the UI thread) when the display layout may have changed: monitors added/removed,
    /// resolution / scaling change, work area / taskbar change, resume from sleep.
    /// </summary>
    event EventHandler? DisplayChanged;
}

/// <summary>Moves windows in physical pixels (avoids WPF DIP ambiguity across monitors with different DPI).</summary>
public interface IWindowPlacement
{
    /// <summary>Current window rectangle in physical pixels.</summary>
    PixelRect GetBounds(IntPtr hwnd);

    /// <summary>Moves/resizes without activating or changing Z-order. Throws <see cref="System.ComponentModel.Win32Exception"/> on failure.</summary>
    void SetBounds(IntPtr hwnd, PixelRect bounds);

    /// <summary>
    /// Moves without resizing, so that when the window lands on a monitor with another DPI,
    /// Windows/WPF rescale it from its DIP size. Throws <see cref="System.ComponentModel.Win32Exception"/> on failure.
    /// </summary>
    void Move(IntPtr hwnd, int left, int top);
}

/// <summary>Notification-area icon (Shell_NotifyIcon). Re-registers itself after Explorer restarts.</summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>Shows or removes the icon.</summary>
    bool Visible { get; set; }

    /// <summary>Tooltip text (max 127 chars).</summary>
    string ToolTip { get; set; }

    /// <summary>Left click / keyboard select (NIN_SELECT / NIN_KEYSELECT / WM_LBUTTONUP).</summary>
    event EventHandler? Activated;

    /// <summary>True when the last <see cref="Activated"/> came from the keyboard (NIN_KEYSELECT).</summary>
    bool LastActivationWasKeyboard { get; }

    /// <summary>Right click / context-menu key. The handler should call <see cref="ShowMenu"/>.</summary>
    event EventHandler? MenuRequested;

    /// <summary>Reloads the icon at the current DPI (call after display / DPI changes).</summary>
    void UpdateIcon();

    /// <summary>
    /// Shows a native popup menu at the cursor (TrackPopupMenuEx with the foreground-window fix) and
    /// runs the chosen item's action after the menu has closed.
    /// </summary>
    void ShowMenu(IReadOnlyList<TrayMenuItem> items);
}

/// <summary>A notification-area menu entry (native menu model; WPF menus cannot reliably open from the tray).</summary>
/// <param name="Text">Label; ignored for separators.</param>
/// <param name="OnClick">Action run after the menu closes; null for separators / submenus.</param>
public sealed record TrayMenuItem(string Text, Action? OnClick = null)
{
    public static TrayMenuItem Separator { get; } = new(string.Empty) { IsSeparator = true };

    public bool IsSeparator { get; init; }

    public bool IsChecked { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Shown in bold (default action, i.e. what a left click does).</summary>
    public bool IsDefault { get; init; }

    public IReadOnlyList<TrayMenuItem>? Children { get; init; }
}
