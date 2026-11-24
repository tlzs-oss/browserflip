using System.Globalization;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Linux;

/// <summary>The display protocol the current session uses.</summary>
internal enum LinuxSessionType
{
    /// <summary>Neither Wayland nor X11 could be identified (a TTY, a container, or an unset environment).</summary>
    Unknown,

    /// <summary>X11, where a client may position its own windows and ask to stay on top.</summary>
    X11,

    /// <summary>Wayland, where by design a client may do neither.</summary>
    Wayland,
}

/// <summary>
/// What this Linux session can actually do. Reported honestly rather than attempted and silently
/// dropped: see <c>docs/linux-feasibility.md</c> section C.
/// </summary>
/// <remarks>
/// <para>
/// The two hard limits are not bugs and not worked around:
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
    public LinuxCapabilities()
        : this(Environment.GetEnvironmentVariable, () => XdgSettings.IsAvailable)
    {
    }

    /// <summary>
    /// Test seam: the environment is read through a delegate, and whether <c>xdg-settings</c> is
    /// installed through another, so the whole capability matrix is unit-testable off Linux.
    /// </summary>
    internal LinuxCapabilities(
        Func<string, string?> getEnvironmentVariable,
        Func<bool> isSwitchToolAvailable,
        Func<bool>? hasStatusNotifierWatcher = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(isSwitchToolAvailable);
        hasStatusNotifierWatcher ??= () => StatusNotifierWatcherIsRunning(getEnvironmentVariable);

        Session = DetectSession(getEnvironmentVariable);
        Desktop = DetectDesktop(getEnvironmentVariable);
        IsGnome = IsGnomeDesktop(Desktop);

        Description = string.Format(
            CultureInfo.InvariantCulture,
            "Linux ({0}, {1})",
            DesktopName(Desktop),
            SessionName(Session));

        AlwaysOnTop = Session switch
        {
            LinuxSessionType.X11 => Capability.Available,
            LinuxSessionType.Wayland => Capability.Unsupported(
                "Wayland's xdg-shell protocol has no always-on-top request, and the layer-shell extension that could provide one is not implemented by GNOME or KDE."),
            _ => Capability.Unavailable(
                "The display protocol could not be identified (XDG_SESSION_TYPE is not set), so staying on top cannot be relied on."),
        };

        WindowPositioning = Session switch
        {
            LinuxSessionType.X11 => Capability.Available,
            LinuxSessionType.Wayland => Capability.Unsupported(
                "Wayland does not expose global screen coordinates to clients and does not let a window place itself; the compositor decides where windows go."),
            _ => Capability.Unavailable(
                "The display protocol could not be identified (XDG_SESSION_TYPE is not set), so window positions cannot be restored."),
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

    /// <summary>The detected display protocol.</summary>
    internal LinuxSessionType Session { get; }

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
    /// Reads <c>XDG_SESSION_TYPE</c>, falling back to the presence of <c>WAYLAND_DISPLAY</c> or
    /// <c>DISPLAY</c>, because some login managers and most containers leave the former unset.
    /// </summary>
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

    private static string SessionName(LinuxSessionType session) => session switch
    {
        LinuxSessionType.Wayland => "Wayland",
        LinuxSessionType.X11 => "X11",
        _ => "unknown session",
    };
}
