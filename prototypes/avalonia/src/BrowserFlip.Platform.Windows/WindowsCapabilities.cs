using System.Globalization;
using System.Runtime.Versioning;
using BrowserFlip.Core.Platform;
using BrowserFlip.Core.Settings;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Windows;

/// <summary>
/// What Windows can do. Unlike Wayland (no client-side window placement, no always-on-top) and
/// GNOME (no tray by default), Windows supports every window feature BrowserFlip needs, so the
/// only capability that can be missing is changing the default browser — and only because Group
/// Policy can hide the Settings page the change has to happen on.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCapabilities : PlatformCapabilitiesBase
{
    private readonly IOsInfo _os;
    private readonly PolicyStatus _policy;

    public WindowsCapabilities() : this(new WindowsRegistryReader())
    {
    }

    /// <summary>
    /// Takes the registry reader, so a host that already has one shares it and tests can supply
    /// an in-memory hive.
    /// </summary>
    public WindowsCapabilities(IRegistryReader registry)
        : this(new EnvironmentOsInfo(registry), new PolicyInspector(registry))
    {
    }

    internal WindowsCapabilities(IOsInfo os, PolicyInspector policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _os = os;
        // Inspected once: the capability properties are read repeatedly (All(), the settings
        // screen, every status refresh) and policy only changes on a policy refresh or sign-in,
        // so re-reading the hive on each property get would cost more than it could ever notice.
        _policy = policy.Inspect();
    }

    /// <summary>
    /// For example "Windows 11 (build 26100.2314)". The build number matters for support
    /// questions: the per-app Settings deep link exists only from certain builds upwards.
    /// </summary>
    public override string Description
        => string.Create(
            CultureInfo.InvariantCulture,
            $"Windows {(_os.BuildNumber >= 22000 ? "11" : "10")} (build {_os.BuildNumber}.{_os.UpdateBuildRevision})");

    public override Capability AlwaysOnTop => Capability.Available;

    public override Capability WindowPositioning => Capability.Available;

    public override Capability TrayIcon => Capability.Available;

    /// <summary>
    /// Available unless policy blocks the page: without the "Default apps" page there is no
    /// user-consented way to change the association, and BrowserFlip will not write one itself.
    /// </summary>
    public override Capability DefaultBrowserSwitch
        => _policy.BlocksNavigation ? Capability.Unavailable(PolicyReason()) : Capability.Available;

    /// <summary>
    /// True: the user confirms the change in the Settings app, so BrowserFlip must not ask for
    /// confirmation a second time.
    /// </summary>
    public override bool OsConfirmsDefaultBrowserChange => true;

    /// <summary>Names the policy that blocks the page, so the message is actionable.</summary>
    private string PolicyReason()
    {
        var cause = _policy.SettingsAppBlocked
            ? "Control Panel and Settings are disabled by policy (NoControlPanel)"
            : "the Default apps page is hidden by policy (SettingsPageVisibility)";
        return $"Group Policy prevents changing the default browser: {cause}.";
    }
}
