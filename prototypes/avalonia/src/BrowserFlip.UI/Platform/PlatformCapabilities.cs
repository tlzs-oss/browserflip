using BrowserFlip.UI.Localization;

namespace BrowserFlip.UI.Platform;

/// <summary>Whether a capability can be used here, and why not when it cannot.</summary>
public enum CapabilityState
{
    /// <summary>Usable on this platform and in this session.</summary>
    Available,

    /// <summary>The platform cannot do it at all. The UI must say so rather than fail silently.</summary>
    Unsupported,

    /// <summary>The platform could do it, but this session or configuration cannot.</summary>
    Unavailable,
}

/// <summary>One capability, with a human-readable reason when it is not available.</summary>
/// <param name="State">Whether it can be used.</param>
/// <param name="Reason">Why not; <c>null</c> when available.</param>
public readonly record struct Capability(CapabilityState State, string? Reason = null)
{
    public static Capability Available { get; } = new(CapabilityState.Available);

    public static Capability Unsupported(string reason) => new(CapabilityState.Unsupported, reason);

    public static Capability Unavailable(string reason) => new(CapabilityState.Unavailable, reason);

    public bool IsAvailable => State == CapabilityState.Available;
}

/// <summary>
/// What the current platform and session can actually do.
/// <para>
/// This exists because the three target platforms genuinely differ, and the differences must be
/// shown to the user instead of being worked around unreliably. Wayland, for example, gives a
/// client no way to position its own windows or to ask to stay on top; GNOME ships no system
/// tray by default. Rather than guessing or using fragile hacks, each platform adapter reports
/// what it supports and the UI disables the rest with the reason attached.
/// </para>
/// </summary>
public interface IPlatformCapabilities
{
    /// <summary>Short name of the platform and session, e.g. "macOS 15" or "Linux (GNOME, Wayland)".</summary>
    string Description { get; }

    /// <summary>Keeping a window above other windows.</summary>
    Capability AlwaysOnTop { get; }

    /// <summary>Placing a window at chosen desktop coordinates (the floating toolbar needs this).</summary>
    Capability WindowPositioning { get; }

    /// <summary>A status-area / menu-bar / tray icon.</summary>
    Capability TrayIcon { get; }

    /// <summary>Changing the default browser at all.</summary>
    Capability DefaultBrowserSwitch { get; }

    /// <summary>
    /// Whether the OS asks the user to confirm the change. When it does not, BrowserFlip must
    /// ask instead: the product never changes the default browser without the user agreeing.
    /// </summary>
    bool OsConfirmsDefaultBrowserChange { get; }

    /// <summary>
    /// Enumerates every capability for diagnostics and the settings screen. The names come from
    /// the resources in the current UI language, so the capability table is not English inside
    /// a Japanese or Chinese window.
    /// </summary>
    IReadOnlyList<(string Name, Capability Capability)> All();
}

/// <summary>Base implementation that supplies <see cref="IPlatformCapabilities.All"/>.</summary>
public abstract class PlatformCapabilitiesBase : IPlatformCapabilities
{
    public abstract string Description { get; }

    public abstract Capability AlwaysOnTop { get; }

    public abstract Capability WindowPositioning { get; }

    public abstract Capability TrayIcon { get; }

    public abstract Capability DefaultBrowserSwitch { get; }

    public abstract bool OsConfirmsDefaultBrowserChange { get; }

    /// <summary>
    /// Resolved on every call rather than cached, so switching the UI language re-reads the
    /// names instead of leaving the previous language's text in the table.
    /// </summary>
    public IReadOnlyList<(string Name, Capability Capability)> All() =>
    [
        (Strings.Get("Capability_AlwaysOnTop"), AlwaysOnTop),
        (Strings.Get("Capability_WindowPositioning"), WindowPositioning),
        (Strings.Get("Capability_TrayIcon"), TrayIcon),
        (Strings.Get("Capability_DefaultBrowserSwitch"), DefaultBrowserSwitch),
    ];
}

/// <summary>Everything available and the OS confirms. Used by platforms with no restrictions.</summary>
public sealed class FullCapabilities : PlatformCapabilitiesBase
{
    public FullCapabilities(string description, bool osConfirms)
    {
        Description = description;
        OsConfirmsDefaultBrowserChange = osConfirms;
    }

    public override string Description { get; }

    public override Capability AlwaysOnTop => Capability.Available;

    public override Capability WindowPositioning => Capability.Available;

    public override Capability TrayIcon => Capability.Available;

    public override Capability DefaultBrowserSwitch => Capability.Available;

    public override bool OsConfirmsDefaultBrowserChange { get; }
}
