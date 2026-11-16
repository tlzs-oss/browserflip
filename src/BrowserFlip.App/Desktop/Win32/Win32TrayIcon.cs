using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BrowserFlip.App.Desktop.Win32;

/// <summary>
/// <see cref="ITrayIcon"/> using Shell_NotifyIconW (NOTIFYICON_VERSION_4, identified by hWnd + uID, no GUID —
/// a GUID is bound to the executable path and breaks when the portable exe is moved).
/// Create, use and dispose on the UI thread (members other than <see cref="Dispose"/> verify thread access).
/// </summary>
public sealed class Win32TrayIcon : ITrayIcon
{
    internal const int MaxToolTipLength = 127;
    internal const uint IconId = 1;
    internal const int CallbackMessage = NativeMethods.WmApp + 1;

    /// <summary>
    /// True when an activation at <paramref name="now"/> is not a repeat of <paramref name="last"/>
    /// (no previous activation, or at least <see cref="ActivationDedupeMs"/> later).
    /// Written without subtracting a sentinel to avoid overflow (the first click must always fire).
    /// </summary>
    internal static bool ShouldFireActivation(long now, long? last)
        => last is not { } previous || now - previous >= ActivationDedupeMs;

    /// <summary>Keyboard Enter can deliver NIN_KEYSELECT twice; collapse repeats closer than this.</summary>
    private const long ActivationDedupeMs = 150;

    private readonly Func<int, IntPtr>? _iconFactory;
    private readonly HwndSource _window;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _added;
    private bool _version4;
    private bool _tracking;
    private bool _visible;
    private string _toolTip = string.Empty;
    private long? _lastActivation;
    private int _disposed;

    /// <summary>Uses the current executable's first icon at the small-icon size for the current DPI.</summary>
    public Win32TrayIcon()
        : this(null)
    {
    }

    /// <param name="iconFactory">
    /// Given the desired pixel size, returns an HICON that this instance takes ownership of (it calls DestroyIcon),
    /// or <see cref="IntPtr.Zero"/> to fall back to the executable's icon. Null uses the executable's icon.
    /// </param>
    public Win32TrayIcon(Func<int, IntPtr>? iconFactory)
    {
        _iconFactory = iconFactory;
        _window = HiddenMessageWindow.Create("BrowserFlip.TrayHost", WndProc);

        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        if (_taskbarCreatedMessage != 0)
        {
            // Lets the broadcast through UIPI if the process ever runs elevated.
            _ = NativeMethods.ChangeWindowMessageFilterEx(_window.Handle, _taskbarCreatedMessage, NativeMethods.MsgFltAllow, IntPtr.Zero);
        }

        LoadIcon();
    }

    public event EventHandler? Activated;

    /// <inheritdoc />
    public bool LastActivationWasKeyboard { get; private set; }

    public event EventHandler? MenuRequested;

    internal enum TrayAction
    {
        None,
        Activate,
        Menu,
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            VerifyAccess();
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            if (value)
            {
                Add();
            }
            else
            {
                Delete();
            }
        }
    }

    public string ToolTip
    {
        get => _toolTip;
        set
        {
            VerifyAccess();
            var text = Truncate(value);
            if (string.Equals(text, _toolTip, StringComparison.Ordinal))
            {
                return;
            }

            _toolTip = text;
            if (_added)
            {
                var data = CreateData(NativeMethods.NifTip | NativeMethods.NifShowTip);
                _ = NativeMethods.Shell_NotifyIconW(NativeMethods.NimModify, ref data);
            }
        }
    }

    /// <summary>The hidden owner window (for diagnostics / tests).</summary>
    internal IntPtr Handle => _window.Handle;

    /// <summary>Reloads the icon at the current DPI (call after a DPI / display change).</summary>
    public void UpdateIcon()
    {
        VerifyAccess();
        LoadIcon();
        if (_added)
        {
            var data = CreateData(NativeMethods.NifIcon);
            _ = NativeMethods.Shell_NotifyIconW(NativeMethods.NimModify, ref data);
        }
    }

    /// <summary>
    /// Native popup menu: the standard, reliable way for notification-area menus (a WPF ContextMenu opened
    /// from a hidden window closes immediately or never appears on Windows 11, especially from the
    /// overflow flyout). Runs synchronously and returns the chosen command, so foreground rules are satisfied.
    /// </summary>
    public void ShowMenu(IReadOnlyList<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        VerifyAccess();

        // TrackPopupMenuEx runs a modal loop that still dispatches to this window: ignore a nested request
        // (it would fail with ERROR_POPUP_ALREADY_ACTIVE and confuse the caller's "menu open" state).
        if (_tracking)
        {
            return;
        }

        var actions = new List<Action?>();
        var menus = new List<IntPtr>();
        var root = IntPtr.Zero;
        _tracking = true;
        try
        {
            root = Build(items, actions, menus);
            if (!NativeMethods.GetCursorPos(out var pt))
            {
                return;
            }

            // KB135788: the owner must be foreground, otherwise the menu does not close on outside clicks.
            _ = NativeMethods.SetForegroundWindow(_window.Handle);
            var command = NativeMethods.TrackPopupMenuEx(root,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd | NativeMethods.TpmNoNotify,
                pt.X, pt.Y, _window.Handle, IntPtr.Zero);
            _ = NativeMethods.PostMessageW(_window.Handle, NativeMethods.WmNull, IntPtr.Zero, IntPtr.Zero);

            if (command > 0 && command <= actions.Count && actions[command - 1] is { } action)
            {
                // Run after the menu loop has fully unwound.
                _ = _window.Dispatcher.BeginInvoke(action);
            }
        }
        finally
        {
            // Destroying the root also destroys attached submenus.
            if (root != IntPtr.Zero)
            {
                _ = NativeMethods.DestroyMenu(root);
            }

            _tracking = false;
        }
    }

    /// <summary>Builds an HMENU; command ids are 1-based indexes into <paramref name="actions"/>.</summary>
    internal static IntPtr Build(IReadOnlyList<TrayMenuItem> items, List<Action?> actions, List<IntPtr> menus)
    {
        var menu = NativeMethods.CreatePopupMenu();
        menus.Add(menu);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.IsSeparator)
            {
                _ = NativeMethods.AppendMenuW(menu, NativeMethods.MfSeparator, IntPtr.Zero, null);
                continue;
            }

            var flags = NativeMethods.MfString
                        | (item.IsChecked ? NativeMethods.MfChecked : 0)
                        | (item.IsEnabled ? 0 : NativeMethods.MfGrayed);
            if (item.Children is { Count: > 0 } children)
            {
                var sub = Build(children, actions, menus);
                if (!NativeMethods.AppendMenuW(menu, flags | NativeMethods.MfPopup, sub, item.Text))
                {
                    _ = NativeMethods.DestroyMenu(sub); // not attached: the root would not free it
                }

                continue;
            }

            actions.Add(item.OnClick);
            _ = NativeMethods.AppendMenuW(menu, flags, actions.Count, item.Text);
            if (item.IsDefault)
            {
                _ = NativeMethods.SetMenuDefaultItem(menu, (uint)actions.Count, 0);
            }
        }

        return menu;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var dispatcher = _window.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            DisposeCore();
        }
        else if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.Invoke(DisposeCore);
        }
        else
        {
            // UI thread is gone (its windows with it); still remove the icon and free the HICON.
            DeleteIconAndHandle();
        }
    }

    /// <summary>Maps a NOTIFYICON callback event (LOWORD(lParam)) to an action, without double-firing in v4.</summary>
    internal static TrayAction Classify(int notifyEvent, bool version4) => notifyEvent switch
    {
        // v4: a left click delivers WM_LBUTTONUP *and* NIN_SELECT; a right click WM_RBUTTONUP *and* WM_CONTEXTMENU.
        NativeMethods.NinSelect or NativeMethods.NinKeySelect when version4 => TrayAction.Activate,
        NativeMethods.WmContextMenu when version4 => TrayAction.Menu,
        NativeMethods.WmLButtonUp when !version4 => TrayAction.Activate,
        NativeMethods.WmRButtonUp when !version4 => TrayAction.Menu,
        _ => TrayAction.None,
    };

    internal static string Truncate(string? text)
    {
        text ??= string.Empty;
        if (text.Length <= MaxToolTipLength)
        {
            return text;
        }

        // Do not split a surrogate pair.
        var length = char.IsHighSurrogate(text[MaxToolTipLength - 1]) ? MaxToolTipLength - 1 : MaxToolTipLength;
        return text[..length];
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _window.Dispatcher.VerifyAccess();
    }

    private void DisposeCore()
    {
        DeleteIconAndHandle();
        _window.Dispose();
    }

    private void DeleteIconAndHandle()
    {
        Delete();
        ReleaseIcon();
    }

    private void Add()
    {
        var data = CreateData(NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip | NativeMethods.NifShowTip);
        if (!NativeMethods.Shell_NotifyIconW(NativeMethods.NimAdd, ref data))
        {
            // Explorer not running / not ready: TaskbarCreated will retry.
            _added = false;
            return;
        }

        _added = true;
        data.uVersion = NativeMethods.NotifyIconVersion4;
        _version4 = NativeMethods.Shell_NotifyIconW(NativeMethods.NimSetVersion, ref data);
    }

    private void Delete()
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData(0);
        _ = NativeMethods.Shell_NotifyIconW(NativeMethods.NimDelete, ref data);
        _added = false;
    }

    private NativeMethods.NOTIFYICONDATAW CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _toolTip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void LoadIcon()
    {
        var size = GetSmallIconSize();
        var owns = true;
        var icon = _iconFactory?.Invoke(size) ?? IntPtr.Zero;
        if (icon == IntPtr.Zero)
        {
            icon = ExtractOwnIcon(size);
        }

        if (icon == IntPtr.Zero)
        {
            // Shared system icon: must never be destroyed.
            icon = NativeMethods.LoadIconW(IntPtr.Zero, NativeMethods.IdiApplication);
            owns = false;
        }

        ReleaseIcon();
        _icon = icon;
        _ownsIcon = owns;
    }

    private void ReleaseIcon()
    {
        if (_icon != IntPtr.Zero && _ownsIcon)
        {
            _ = NativeMethods.DestroyIcon(_icon);
        }

        _icon = IntPtr.Zero;
        _ownsIcon = false;
    }

    private int GetSmallIconSize()
    {
        try
        {
            var dpi = NativeMethods.GetDpiForWindow(_window.Handle);
            if (dpi > 0)
            {
                var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSmIcon, dpi);
                if (size > 0)
                {
                    return size;
                }
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-1607; fall through.
        }

        var fallback = NativeMethods.GetSystemMetrics(NativeMethods.SmCxSmIcon);
        return fallback > 0 ? fallback : 16;
    }

    private static IntPtr ExtractOwnIcon(int size)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return IntPtr.Zero;
        }

        // LOWORD = large size, HIWORD = small size; we only keep the "large" one at the requested size.
        var sizes = (uint)size | ((uint)size << 16);
        if (NativeMethods.SHDefExtractIconW(path, 0, 0, out var large, out var small, sizes) != 0)
        {
            return IntPtr.Zero;
        }

        if (small != IntPtr.Zero)
        {
            _ = NativeMethods.DestroyIcon(small);
        }

        return large;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            handled = true;
            if (Volatile.Read(ref _disposed) != 0)
            {
                return IntPtr.Zero;
            }

            // v4: LOWORD(lParam) = event, HIWORD(lParam) = icon id. Legacy: lParam = mouse message, wParam = id.
            var notifyEvent = _version4 ? NativeMethods.LoWord(lParam) : (int)(long)lParam;
            switch (Classify(notifyEvent, _version4))
            {
                case TrayAction.Activate:
                    var now = Environment.TickCount64;
                    if (ShouldFireActivation(now, _lastActivation))
                    {
                        _lastActivation = now;
                        LastActivationWasKeyboard = notifyEvent == NativeMethods.NinKeySelect;
                        Activated?.Invoke(this, EventArgs.Empty);
                    }

                    break;
                case TrayAction.Menu:
                    MenuRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }

            return IntPtr.Zero;
        }

        if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage && Volatile.Read(ref _disposed) == 0)
        {
            // Explorer (re)started: every icon is gone. Reload (taskbar DPI may differ) and re-add if wanted.
            _added = false;
            _version4 = false;
            LoadIcon();
            if (_visible)
            {
                Add();
            }
        }

        return IntPtr.Zero;
    }
}
