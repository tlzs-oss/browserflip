using System.Runtime.Versioning;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using BrowserFlip.Core.Settings;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Windows;

/// <summary>
/// Asks Windows to make a browser the default by opening the official Settings "Default apps"
/// page — the browser's own page when the OS build supports the deep link, the top-level page
/// otherwise. BrowserFlip never writes <c>UserChoice</c>: the user confirms in Settings, as
/// Windows requires.
/// </summary>
/// <remarks>
/// All of that behaviour (deep-link support matrix, policy check, fallback, error capture) lives
/// in <see cref="SettingsLauncher"/> and is reused verbatim; this type only translates Core's
/// <see cref="LaunchKind"/> into the UI's <see cref="SwitchOutcome"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsDefaultBrowserSwitcher : IDefaultBrowserSwitcher
{
    /// <summary>Policy blocks the Settings page, so nothing was launched and nothing can be.</summary>
    internal const string PolicyBlockedMessage =
        "Group Policy prevents opening the Windows Settings \"Default apps\" page "
        + "(NoControlPanel or SettingsPageVisibility), so the default browser cannot be changed here.";

    /// <summary>The executable recorded in the registry is gone; Settings would show a stale entry.</summary>
    internal const string BrowserMissingMessage =
        "The browser's executable is missing, so Windows cannot make it the default.";

    private const string AppPageMessage =
        "Windows Settings opened this browser's \"Default apps\" page. The change takes effect once you confirm it there.";

    private const string TopPageMessage =
        "Windows Settings opened the \"Default apps\" page. Select this browser there and confirm the change.";

    private const string UnknownFailureMessage = "Windows Settings could not be opened.";

    private readonly SettingsLauncher _settings;

    public WindowsDefaultBrowserSwitcher() : this(CreateLauncher())
    {
    }

    /// <summary>
    /// Injects the Core launcher, so the mapping can be tested without launching Settings.
    /// </summary>
    internal WindowsDefaultBrowserSwitcher(SettingsLauncher settings) => _settings = settings;

    /// <summary>
    /// Always <see cref="SwitchMechanism.OpensSystemSettings"/>. Even where the per-app deep link
    /// is unsupported the mechanism is the same page, only less precisely targeted, so the UI
    /// wording stays correct on Windows 10 as well.
    /// </summary>
    public SwitchMechanism Mechanism => SwitchMechanism.OpensSystemSettings;

    public Task<SwitchResult> RequestSwitchAsync(BrowserInfo browser, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<SwitchResult>(cancellationToken);
        }

        // Synchronous by nature: ShellExecute returns as soon as Settings has been asked to open.
        // Unlike macOS there is nothing to poll — Windows never reports back what the user chose
        // in Settings, which is exactly why the outcome below is Requested and never Applied.
        return Task.FromResult(ToSwitchResult(_settings.Open(browser)));
    }

    /// <summary>
    /// Composition identical to the shipping WPF product's root, so both Windows front-ends
    /// resolve build numbers and policy from the same registry reader.
    /// </summary>
    private static SettingsLauncher CreateLauncher()
    {
        var registry = new WindowsRegistryReader();
        return new SettingsLauncher(new ShellUriLauncher(), new EnvironmentOsInfo(registry), new PolicyInspector(registry));
    }

    /// <summary>Translates Core's launch result into the UI's switch result.</summary>
    internal static SwitchResult ToSwitchResult(LaunchResult launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        return launch.Kind switch
        {
            // Requested, never Applied: Windows only shows the page. The user has to pick the
            // browser and confirm, and the OS tells us nothing about what they did.
            LaunchKind.OpenedAppPage => new SwitchResult(SwitchOutcome.Requested, AppPageMessage),
            LaunchKind.OpenedTopPage => new SwitchResult(SwitchOutcome.Requested, TopPageMessage),
            LaunchKind.BrowserUnavailable => new SwitchResult(SwitchOutcome.BrowserUnavailable, BrowserMissingMessage),
            // A managed machine is not an unsupported platform: it is a failure with a reason the
            // user (or their administrator) can act on, so it must be reported, not swallowed.
            LaunchKind.BlockedByPolicy => new SwitchResult(SwitchOutcome.Failed, PolicyBlockedMessage),
            LaunchKind.Failed => new SwitchResult(SwitchOutcome.Failed, Describe(launch)),
            _ => new SwitchResult(SwitchOutcome.Failed, Describe(launch)),
        };
    }

    /// <summary>Keeps the shell's own error text, and the URI that failed, in the message.</summary>
    private static string Describe(LaunchResult launch)
    {
        var reason = string.IsNullOrWhiteSpace(launch.Error) ? UnknownFailureMessage : launch.Error;
        return launch.Uri is null ? reason : $"{reason} ({launch.Uri.OriginalString})";
    }
}
