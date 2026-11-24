using BrowserFlip.Core.Detection;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Linux;

/// <summary>
/// Asks Linux to make a browser the default, via <c>xdg-settings set default-web-browser</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Linux is the one platform where nothing outside BrowserFlip protects the user.</b> Windows
/// makes the user confirm in the Settings app; macOS shows its own consent alert. By contrast
/// <c>xdg-settings set</c> applies the change <em>immediately and silently</em> — there is no OS
/// prompt, no undo and no notification.
/// </para>
/// <para>
/// BrowserFlip must never change the default browser without the user agreeing, so this class
/// refuses to touch the system unless it is told the user already confirmed:
/// <see cref="UserHasConfirmed"/> is a <c>required</c> property, so no caller can construct this
/// switcher without stating, in code, whether consent was obtained. When it was not, nothing is
/// run at all and the outcome is <see cref="SwitchOutcome.Declined"/>.
/// </para>
/// <para>
/// The confirmation itself belongs to the UI layer: <see cref="SwitchMechanism.CommandLineTool"/>
/// together with <see cref="IPlatformCapabilities.OsConfirmsDefaultBrowserChange"/> being
/// <c>false</c> is how the UI knows it must ask first. See <c>docs/linux-feasibility.md</c>.
/// </para>
/// </remarks>
public sealed class LinuxDefaultBrowserSwitcher : IDefaultBrowserSwitcher
{
    private readonly XdgSettings _settings;

    public LinuxDefaultBrowserSwitcher() : this(new XdgSettings())
    {
    }

    /// <summary>Test seam for the <c>xdg-settings</c> wrapper.</summary>
    internal LinuxDefaultBrowserSwitcher(XdgSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>
    /// Whether the user has already agreed to this change, in BrowserFlip's own confirmation.
    /// <b>Required</b>: on Linux the change is irreversible from the OS's point of view, so the
    /// decision must be explicit at the call site rather than defaulted.
    /// </summary>
    public required bool UserHasConfirmed { get; init; }

    /// <summary>
    /// Always <see cref="SwitchMechanism.CommandLineTool"/>, even when the tool is missing: the
    /// mechanism describes how this platform works, and the outcome reports what actually
    /// happened. A missing tool surfaces as <see cref="SwitchOutcome.Unsupported"/> below and as
    /// an unavailable capability in <see cref="LinuxCapabilities.DefaultBrowserSwitch"/>.
    /// </summary>
    public SwitchMechanism Mechanism => SwitchMechanism.CommandLineTool;

    public async Task<SwitchResult> RequestSwitchAsync(BrowserInfo browser, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);

        // The guard comes first, before any validation that could leak information or any call
        // that could change the system. Without consent this method is a no-op by construction.
        if (!UserHasConfirmed)
        {
            return new SwitchResult(
                SwitchOutcome.Declined,
                "xdg-settings applies the change immediately and the OS asks nothing, so BrowserFlip did not run it: the user has not confirmed.");
        }

        var desktopFileId = browser.HttpsProgId ?? browser.HttpProgId ?? browser.Id;
        if (string.IsNullOrWhiteSpace(desktopFileId))
        {
            return new SwitchResult(SwitchOutcome.BrowserUnavailable, "The browser has no desktop file id.");
        }

        if (browser.Health == BrowserHealth.ExecutableMissing)
        {
            return new SwitchResult(SwitchOutcome.BrowserUnavailable, browser.ExecutablePath);
        }

        // xdg-settings is a shell script that waits on config writes, so it is kept off the UI
        // thread. ConfigureAwait(false): unlike the macOS adapter nothing here is thread-affine.
        return await Task.Run(() => Apply(desktopFileId, browser), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the change and then re-reads the effective default, so what the user is told is what
    /// the system actually did rather than merely what was asked for.
    /// </summary>
    private SwitchResult Apply(string desktopFileId, BrowserInfo browser)
    {
        var result = _settings.SetDefaultWebBrowser(desktopFileId);

        // Probing PATH up front would be a second source of truth; the launch failure itself is
        // the authoritative answer to "is xdg-settings installed?".
        if (result.ToolMissing)
        {
            return new SwitchResult(
                SwitchOutcome.Unsupported,
                $"{XdgSettings.SettingsTool} could not be run; install the xdg-utils package. ({result.Error})");
        }

        if (!result.Succeeded)
        {
            return new SwitchResult(SwitchOutcome.Failed, result.FailureMessage);
        }

        var current = _settings.GetDefaultWebBrowser();
        if (current is not null && Matches(current, browser))
        {
            return new SwitchResult(SwitchOutcome.Applied);
        }

        // The tool reported success but the read-back disagrees: this happens inside a Flatpak
        // sandbox and on desktops that keep a second source of truth. Say so instead of claiming
        // the change took effect.
        return new SwitchResult(
            SwitchOutcome.Requested,
            current is null
                ? $"{XdgSettings.SettingsTool} reported success but the new default could not be read back."
                : $"{XdgSettings.SettingsTool} reported success but the default is still '{current}'.");
    }

    /// <summary>True when the id <c>xdg-settings</c> now reports is this browser's.</summary>
    internal static bool Matches(string currentDesktopFileId, BrowserInfo browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        if (string.IsNullOrWhiteSpace(currentDesktopFileId))
        {
            return false;
        }

        var current = LinuxAssociationQuery.NormaliseDesktopFileId(currentDesktopFileId);
        return string.Equals(current, browser.HttpsProgId, StringComparison.Ordinal)
               || string.Equals(current, browser.HttpProgId, StringComparison.Ordinal)
               || string.Equals(current, browser.Id, StringComparison.Ordinal);
    }
}
