using System.Globalization;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Linux;

/// <summary>The display protocol the current session advertises.</summary>
internal enum LinuxSessionType
{
    /// <summary>Neither Wayland nor X11 could be identified (a TTY, a container, or an unset environment).</summary>
    Unknown,

    /// <summary>X11.</summary>
    X11,

    /// <summary>Wayland.</summary>
    Wayland,
}

/// <summary>
/// Which windowing backend Avalonia actually initialised in this process. This is a fact about
/// the running process, not a reading of the environment.
/// </summary>
internal enum LinuxWindowingBackend
{
    /// <summary>No backend has reported itself: a unit test, or a host that is not Linux.</summary>
    Unknown,

    /// <summary><c>Avalonia.Wayland</c> won, so the process is a native Wayland client.</summary>
    Wayland,

    /// <summary><c>Avalonia.X11</c> won, so the process is an X11 client — on XWayland or on a real X server.</summary>
    X11,
}

/// <summary>
/// What the application actually talks to. This, and not <see cref="LinuxSessionType"/>, is what
/// the window-management capabilities depend on.
/// </summary>
internal enum LinuxDisplayBackend
{
    /// <summary>Could not be established; nothing may be claimed.</summary>
    Unknown,

    /// <summary>A Wayland client speaking <c>xdg-shell</c> to the compositor.</summary>
    NativeWayland,

    /// <summary>An X11 client, whose X server is the XWayland one inside a Wayland session.</summary>
    XWayland,

    /// <summary>An X11 client on a real X server, with a real X11 window manager.</summary>
    NativeX11,
}

/// <summary>
/// What this Linux session can actually do. Reported honestly rather than attempted and silently
/// dropped: see <c>docs/linux-feasibility.md</c> section C.
/// </summary>
/// <remarks>
/// <para>
/// The limits below belong to the <b>backend the process is using</b>, not to the session type.
/// An application started inside a GNOME Wayland session with <c>WAYLAND_DISPLAY</c> unset and
/// <c>DISPLAY</c> set runs as an X11 client against XWayland, and the X11 mechanisms are then the
/// ones in play even though <c>XDG_SESSION_TYPE</c> still says <c>wayland</c>. Deciding from
/// <c>XDG_SESSION_TYPE</c> alone made the self-test report "Wayland's xdg-shell protocol has no
/// always-on-top request" for a run that was provably using X11, which is simply false.
/// </para>
/// <para>
/// As a <b>native Wayland client</b> the two hard limits are not bugs and not worked around:
/// <list type="bullet">
///   <item><description><b>Wayland has no always-on-top request.</b> <c>xdg-shell</c> does not
///     define one; only <c>wlr-layer-shell</c> does, and GNOME and KDE do not implement it.</description></item>
///   <item><description><b>Wayland clients cannot place their own windows.</b> Global coordinates
///     are not exposed to clients at all, which is a deliberate design decision upstream. The
///     floating toolbar therefore cannot restore its position there.</description></item>
/// </list>
/// </para>
/// <para>
/// <see cref="OsConfirmsDefaultBrowserChange"/> is always <c>false</c>: <c>xdg-settings</c> applies
/// the change with no prompt whatsoever, so BrowserFlip itself must ask. That is the flag the UI
/// uses to decide to show its own confirmation, and
/// <see cref="LinuxDefaultBrowserSwitcher.UserHasConfirmed"/> is what enforces it.
/// </para>
/// </remarks>
public sealed class LinuxCapabilities : PlatformCapabilitiesBase
{
    /// <summary>Said when the process is a native Wayland client, where the protocol forbids it.</summary>
    private const string WaylandNoAlwaysOnTop =
        "Wayland's xdg-shell protocol has no always-on-top request, and the layer-shell extension "
        + "that could provide one is not implemented by GNOME or KDE.";

    /// <summary>Said when the process is a native Wayland client, where the protocol forbids it.</summary>
    private const string WaylandNoPositioning =
        "Wayland does not expose global screen coordinates to clients and does not let a window "
        + "place itself; the compositor decides where windows go.";

    public LinuxCapabilities()
        : this(Environment.GetEnvironmentVariable, () => XdgSettings.IsAvailable)
    {
    }

    /// <summary>
    /// Test seam: the environment is read through a delegate, whether <c>xdg-settings</c> is
    /// installed through another, and which windowing backend is live through a third, so the
    /// whole capability matrix is unit-testable off Linux.
    /// </summary>
    internal LinuxCapabilities(
        Func<string, string?> getEnvironmentVariable,
        Func<bool> isSwitchToolAvailable,
        Func<bool>? hasStatusNotifierWatcher = null,
        Func<LinuxWindowingBackend>? getWindowingBackend = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(isSwitchToolAvailable);
        hasStatusNotifierWatcher ??= () => StatusNotifierWatcherIsRunning(getEnvironmentVariable);
        getWindowingBackend ??= DetectWindowingBackend;

        Session = DetectSession(getEnvironmentVariable);
        Backend = getWindowingBackend();
        Display = DetectDisplayBackend(Backend, getEnvironmentVariable);
        Desktop = DetectDesktop(getEnvironmentVariable);
        IsGnome = IsGnomeDesktop(Desktop);

        Description = string.Format(
            CultureInfo.InvariantCulture,
            "Linux ({0}, {1})",
            DesktopName(Desktop),
            DisplayName(Display));

        // XWayland is treated as X11 here, and that is a deliberate, sourced decision rather
        // than an assumption:
        //
        //   * Avalonia's X11 backend implements Topmost with _NET_WM_STATE_ABOVE and moves
        //     windows with XMoveResizeWindow (both atoms/entry points are present in
        //     Avalonia.X11 12.1.4).
        //   * mutter advertises _NET_WM_STATE_ABOVE in _NET_SUPPORTED from the single code path
        //     used for both a native X11 session and the XWayland server it spawns in a Wayland
        //     session (src/x11/meta-x11-display.c set_supported_hint, src/x11/atomnames.h), and
        //     applies it through meta_window_make_above -> META_LAYER_TOP. MetaWindowXwayland
        //     derives from MetaWindowX11 and overrides nothing about layers or _NET_WM_STATE.
        //   * mutter honours ConfigureRequest x/y for X11 clients unless the
        //     org.gnome.desktop.wm.preferences disable-workarounds pref is on (it defaults to
        //     false) or an interactive grab is in progress (src/x11/window-x11.c
        //     meta_window_move_resize_request).
        //   * kwin advertises NET::KeepAbove in _NET_SUPPORTED on the XWayland root window
        //     (src/netinfo.cpp RootInfo::create, wired from Workspace::initializeX11 on
        //     x11ConnectionChanged) and applies it in WinInfo::changeState -> setKeepAbove.
        //
        // UNVERIFIED AT RUNTIME. The above is read off upstream source, not measured on a real
        // desktop. Known restrictions that the source itself states: mutter drops the ABOVE
        // layer while a window is maximized, an X11 window inherits the maximum layer of its WM
        // group, kwin window rules can veto keepAbove, and an XWayland position request is
        // scaled and then clamped by mutter's constraint engine. If a real GNOME/KDE Wayland
        // session turns out not to honour either mechanism for XWayland clients, the honest fix
        // is to downgrade XWayland to Capability.Unavailable with that measurement as the
        // reason - never to leave it claimed and silently broken.
        AlwaysOnTop = Display switch
        {
            LinuxDisplayBackend.NativeX11 or LinuxDisplayBackend.XWayland => Capability.Available,
            LinuxDisplayBackend.NativeWayland => Capability.Unsupported(WaylandNoAlwaysOnTop),
            _ => Capability.Unavailable(
                "Neither the Wayland nor the X11 windowing backend reported itself and the session "
                + "could not be identified, so staying on top cannot be relied on."),
        };

        WindowPositioning = Display switch
        {
            LinuxDisplayBackend.NativeX11 or LinuxDisplayBackend.XWayland => Capability.Available,
            LinuxDisplayBackend.NativeWayland => Capability.Unsupported(WaylandNoPositioning),
            _ => Capability.Unavailable(
                "Neither the Wayland nor the X11 windowing backend reported itself and the session "
                + "could not be identified, so window positions cannot be restored."),
        };

        // A Linux tray icon is a D-Bus StatusNotifierItem. Without a session bus there is
        // nowhere to export it: Avalonia's SetIcons silently does nothing, neither throwing nor
        // logging, so claiming the capability here would make the app report a tray icon that
        // provably does not exist. Measured on linux/arm64: with a session bus the app owns
        // org.kde.StatusNotifierItem-<pid>-0; without one, no such name exists on any bus.
        TrayIcon = DetectTray(getEnvironmentVariable, IsGnome, hasStatusNotifierWatcher);

        DefaultBrowserSwitch = isSwitchToolAvailable()
            ? Capability.Available
            : Capability.Unavailable(
                $"{XdgSettings.SettingsTool} was not found on PATH; install the xdg-utils package to change the default browser.");
    }

    /// <summary>The display protocol the session advertises. Descriptive only; see <see cref="Display"/>.</summary>
    internal LinuxSessionType Session { get; }

    /// <summary>The windowing backend Avalonia actually initialised.</summary>
    internal LinuxWindowingBackend Backend { get; }

    /// <summary>What the process actually talks to, which is what the capabilities are derived from.</summary>
    internal LinuxDisplayBackend Display { get; }

    /// <summary>The raw <c>XDG_CURRENT_DESKTOP</c> value, or <c>null</c> when unset.</summary>
    internal string? Desktop { get; }

    /// <summary>True when the session is a GNOME Shell variant, which has no tray.</summary>
    internal bool IsGnome { get; }

    /// <inheritdoc />
    public override string Description { get; }

    /// <inheritdoc />
    public override Capability AlwaysOnTop { get; }

    /// <inheritdoc />
    public override Capability WindowPositioning { get; }

    /// <inheritdoc />
    public override Capability TrayIcon { get; }

    /// <inheritdoc />
    public override Capability DefaultBrowserSwitch { get; }

    /// <summary>
    /// Always <c>false</c> on Linux: <c>xdg-settings set default-web-browser</c> changes the
    /// default immediately, with no OS prompt and no undo. BrowserFlip must confirm itself.
    /// </summary>
    public override bool OsConfirmsDefaultBrowserChange => false;

    /// <summary>
    /// Asks Avalonia which backend it selected instead of re-deriving it from the environment.
    /// <para>
    /// <c>UseWaylandWithFallback()</c> registers one windowing-subsystem initialiser that calls
    /// <c>WaylandPlatform.TryInitialize</c> and, when that fails, invokes the initialiser already
    /// configured by <c>UsePlatformDetect()</c> — the X11 one. Exactly one of the two therefore
    /// runs, and the window objects in this process are created by whichever did.
    /// <see cref="WindowingBackend.ImplementationAssemblyName()"/> reads that off the live main
    /// window, so the answer is a fact about the process rather than an inference from
    /// <c>XDG_SESSION_TYPE</c>.
    /// </para>
    /// </summary>
    internal static LinuxWindowingBackend DetectWindowingBackend()
        => BackendOfAssembly(WindowingBackend.ImplementationAssemblyName());

    /// <summary>
    /// Maps the assembly implementing the app's windows onto a backend. The assembly names are
    /// the NuGet package names the backends live in; an unrecognised one (headless, Win32,
    /// macOS, or no window yet) is honestly reported as unknown rather than guessed at.
    /// </summary>
    internal static LinuxWindowingBackend BackendOfAssembly(string? windowingAssemblyName) => windowingAssemblyName switch
    {
        "Avalonia.Wayland" => LinuxWindowingBackend.Wayland,
        "Avalonia.X11" => LinuxWindowingBackend.X11,
        _ => LinuxWindowingBackend.Unknown,
    };

    /// <summary>
    /// Combines the live backend with whether a Wayland compositor exists at all, which is the
    /// only remaining question once the backend is known: an X11 client inside a Wayland session
    /// is talking to XWayland, and an X11 client with no compositor anywhere is on a real X
    /// server.
    /// <para>
    /// When no backend has initialised — unit tests, or the capability object being built before
    /// <c>AppBuilder.Setup</c> — the environment is the only thing left, and it is used only to
    /// the extent that it is unambiguous.
    /// </para>
    /// </summary>
    internal static LinuxDisplayBackend DetectDisplayBackend(
        LinuxWindowingBackend backend, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var compositor = WaylandCompositorIsPresent(getEnvironmentVariable);

        switch (backend)
        {
            case LinuxWindowingBackend.Wayland:
                return LinuxDisplayBackend.NativeWayland;
            case LinuxWindowingBackend.X11:
                return compositor ? LinuxDisplayBackend.XWayland : LinuxDisplayBackend.NativeX11;
            default:
                break;
        }

        var session = DetectSession(getEnvironmentVariable);
        if (!compositor)
        {
            return session == LinuxSessionType.X11 ? LinuxDisplayBackend.NativeX11 : LinuxDisplayBackend.Unknown;
        }

        // A compositor exists. An X11 display with no Wayland display is the XWayland case that
        // this whole class exists to get right; anything else would have taken the Wayland path.
        var waylandDisplay = !string.IsNullOrWhiteSpace(getEnvironmentVariable("WAYLAND_DISPLAY"));
        var x11Display = !string.IsNullOrWhiteSpace(getEnvironmentVariable("DISPLAY"));
        return x11Display && !waylandDisplay ? LinuxDisplayBackend.XWayland : LinuxDisplayBackend.NativeWayland;
    }

    /// <summary>
    /// Whether a Wayland compositor is reachable at all. <c>WAYLAND_DISPLAY</c> alone is not
    /// enough — the measured failure ran with it deliberately unset — so the session type and
    /// the compositor's own socket in <c>XDG_RUNTIME_DIR</c> are checked too. The socket is a
    /// filesystem fact rather than an environment claim.
    /// </summary>
    internal static bool WaylandCompositorIsPresent(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(getEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return true;
        }

        if (string.Equals(
                getEnvironmentVariable("XDG_SESSION_TYPE")?.Trim(), "wayland", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var runtimeDirectory = getEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrWhiteSpace(runtimeDirectory) && HasWaylandSocket(runtimeDirectory);
    }

    /// <summary>
    /// Whether <c>XDG_RUNTIME_DIR</c> holds a compositor socket. <c>wayland-0.lock</c> is
    /// skipped: a lock file can outlive the compositor that wrote it.
    /// </summary>
    private static bool HasWaylandSocket(string runtimeDirectory)
    {
        try
        {
            return Directory.Exists(runtimeDirectory)
                   && Directory.EnumerateFileSystemEntries(runtimeDirectory, "wayland-*")
                       .Any(entry => !entry.EndsWith(".lock", StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a D-Bus session bus is reachable. Checks the advertised address first, then the
    /// conventional per-user socket, because a session can have the socket without the variable.
    /// </summary>
    /// <summary>
    /// Decides the tray capability. A watcher actually owning
    /// <c>org.kde.StatusNotifierWatcher</c> settles it: GNOME Shell itself ships no tray host,
    /// but <c>gnome-shell-extension-appindicator</c> is a dependency of <c>ubuntu-desktop</c>,
    /// so refusing on the desktop name alone misfires for most Ubuntu GNOME users.
    /// </summary>
    /// <summary>
    /// Whether anything owns <c>org.kde.StatusNotifierWatcher</c>. Asked over the session bus
    /// with <c>busctl</c> / <c>dbus-send</c>; when neither tool is present the answer is "no",
    /// which only falls back to the desktop-name heuristic and never over-claims.
    /// </summary>
    internal static bool StatusNotifierWatcherIsRunning(Func<string, string?> getEnvironmentVariable)
    {
        const string WatcherName = "org.kde.StatusNotifierWatcher";
        var runner = new ProcessCommandRunner();
        var timeout = TimeSpan.FromSeconds(3);

        if (LinuxExecutables.Resolve("busctl", getEnvironmentVariable) is not null)
        {
            var result = runner.Run("busctl", ["--user", "--list", "--no-legend", "--no-pager"], timeout);
            if (result.Succeeded && result.StandardOutput.Contains(WatcherName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (LinuxExecutables.Resolve("dbus-send", getEnvironmentVariable) is not null)
        {
            var result = runner.Run(
                "dbus-send",
                [
                    "--session", "--print-reply", "--dest=org.freedesktop.DBus",
                    "/org/freedesktop/DBus", "org.freedesktop.DBus.ListNames",
                ],
                timeout);
            if (result.Succeeded && result.StandardOutput.Contains(WatcherName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static Capability DetectTray(
        Func<string, string?> getEnvironmentVariable, bool isGnome, Func<bool> hasStatusNotifierWatcher)
    {
        if (!HasSessionBus(getEnvironmentVariable))
        {
            return Capability.Unavailable(
                "There is no D-Bus session bus (DBUS_SESSION_BUS_ADDRESS is unset and no bus socket exists), "
                + "so a StatusNotifierItem cannot be exported and no tray icon can appear.");
        }

        if (hasStatusNotifierWatcher())
        {
            return Capability.Available;
        }

        return isGnome
            ? Capability.Unavailable(
                "GNOME Shell ships no system tray and nothing on the session bus is acting as one; "
                + "install and enable the AppIndicator / KStatusNotifierItem extension.")
            : Capability.Available;
    }

    internal static bool HasSessionBus(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(getEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
        {
            return true;
        }

        var runtimeDir = getEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDir) && File.Exists(PosixPath.Join(runtimeDir, "bus")))
        {
            return true;
        }

        var uid = getEnvironmentVariable("UID");
        return !string.IsNullOrWhiteSpace(uid) && File.Exists($"/run/user/{uid}/bus");
    }

    /// <summary>
    /// Reads <c>XDG_SESSION_TYPE</c>, falling back to the presence of <c>WAYLAND_DISPLAY</c> or
    /// <c>DISPLAY</c>, because some login managers and most containers leave the former unset.
    /// This describes the <i>session</i>, which is not the same question as which backend the
    /// process ended up using: see <see cref="DetectDisplayBackend"/>.
    /// </summary>
    internal static LinuxSessionType DetectSession(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var sessionType = getEnvironmentVariable("XDG_SESSION_TYPE")?.Trim();
        if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxSessionType.Wayland;
        }

        if (string.Equals(sessionType, "x11", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxSessionType.X11;
        }

        if (!string.IsNullOrWhiteSpace(getEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return LinuxSessionType.Wayland;
        }

        if (!string.IsNullOrWhiteSpace(getEnvironmentVariable("DISPLAY")))
        {
            return LinuxSessionType.X11;
        }

        return LinuxSessionType.Unknown;
    }

    /// <summary>
    /// The raw <c>XDG_CURRENT_DESKTOP</c> value, which is a colon-separated list such as
    /// <c>ubuntu:GNOME</c>. It is kept whole on purpose: picking one entry out of it would throw
    /// away the part that identifies the shell.
    /// </summary>
    internal static string? DetectDesktop(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        var value = getEnvironmentVariable("XDG_CURRENT_DESKTOP");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// True for GNOME and its variants (<c>ubuntu:GNOME</c>, <c>GNOME-Classic</c>,
    /// <c>GNOME-Flashback</c>), all of which run GNOME Shell and so have no tray without an
    /// extension. The whole list is searched, because on Ubuntu the generic name is not first.
    /// </summary>
    internal static bool IsGnomeDesktop(string? desktop)
        => desktop is not null && desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase);

    /// <summary>The desktop list as shown to the user: "ubuntu:GNOME" reads better as "ubuntu/GNOME".</summary>
    private static string DesktopName(string? desktop)
        => string.IsNullOrEmpty(desktop) ? "unknown desktop" : desktop.Replace(':', '/');

    /// <summary>
    /// What the diagnostics call the backend. "XWayland" is spelled out so a report from a real
    /// desktop says which of the two X11 cases produced it.
    /// </summary>
    private static string DisplayName(LinuxDisplayBackend display) => display switch
    {
        LinuxDisplayBackend.NativeWayland => "Wayland",
        LinuxDisplayBackend.XWayland => "XWayland",
        LinuxDisplayBackend.NativeX11 => "X11",
        _ => "unknown session",
    };
}
