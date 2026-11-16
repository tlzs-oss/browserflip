using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;

namespace BrowserFlip.Core.Settings;

/// <summary>What happened when BrowserFlip tried to open Windows Settings.</summary>
public enum LaunchKind
{
    /// <summary>Opened the target browser's own Default apps page.</summary>
    OpenedAppPage,

    /// <summary>Opened the top-level Default apps page (deep link unsupported or not possible).</summary>
    OpenedTopPage,

    /// <summary>A policy prevents reaching the Default apps page; nothing was launched.</summary>
    BlockedByPolicy,

    /// <summary>The browser's executable is missing; nothing was launched.</summary>
    BrowserUnavailable,

    /// <summary>Windows Settings could not be opened.</summary>
    Failed,
}

/// <summary>Result of <see cref="SettingsLauncher.Open"/>.</summary>
public sealed record LaunchResult(LaunchKind Kind, Uri? Uri, string? Error = null)
{
    public bool Launched => Kind is LaunchKind.OpenedAppPage or LaunchKind.OpenedTopPage;
}

/// <summary>
/// Opens the official Windows Settings "Default apps" page. BrowserFlip never changes associations
/// itself: the user confirms the change in Settings, as Windows requires.
/// </summary>
public sealed class SettingsLauncher
{
    /// <summary>
    /// Whether Windows supports the per-app deep link
    /// (<c>ms-settings:defaultapps?registeredAppUser|registeredAppMachine=</c>).
    /// Per Microsoft Learn ("Launch the Default Apps settings page"): Windows 11 21H2 with the
    /// 2023-04 cumulative update (10.0.22000.1817), 22H2 with the 2023-04 update (10.0.22621.1555),
    /// and 23H2 or later. Windows 10 only supports the top-level page.
    /// </summary>
    public static bool SupportsAppDeepLink(int build, int ubr) => build switch
    {
        < 22000 => false,
        22000 => ubr >= 1817,
        < 22621 => true, // Insider-only builds between 21H2 and 22H2.
        22621 => ubr >= 1555,
        _ => true,
    };

    public static readonly Uri DefaultAppsUri = new("ms-settings:defaultapps");

    private readonly IUriLauncher _launcher;
    private readonly IOsInfo _os;
    private readonly PolicyInspector _policy;

    public SettingsLauncher(IUriLauncher launcher, IOsInfo os, PolicyInspector policy)
    {
        _launcher = launcher;
        _os = os;
        _policy = policy;
    }

    /// <summary>Builds the deep-link URI for <paramref name="browser"/>, or <c>null</c> when none applies.</summary>
    public Uri? BuildAppPageUri(BrowserInfo browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        if (!SupportsAppDeepLink(_os.BuildNumber, _os.UpdateBuildRevision) || string.IsNullOrWhiteSpace(browser.RegisteredApplicationName))
        {
            return null;
        }

        var parameter = browser.RegisteredApplicationScope == RegistrationScope.User ? "registeredAppUser" : "registeredAppMachine";
        return new Uri($"ms-settings:defaultapps?{parameter}={Uri.EscapeDataString(browser.RegisteredApplicationName)}");
    }

    public LaunchResult Open(BrowserInfo browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        if (browser.Health == BrowserHealth.ExecutableMissing)
        {
            return new LaunchResult(LaunchKind.BrowserUnavailable, null);
        }

        if (_policy.Inspect().BlocksNavigation)
        {
            return new LaunchResult(LaunchKind.BlockedByPolicy, null);
        }

        var deepLink = BuildAppPageUri(browser);
        if (deepLink is not null && TryLaunch(deepLink, out _))
        {
            return new LaunchResult(LaunchKind.OpenedAppPage, deepLink);
        }

        return TryLaunch(DefaultAppsUri, out var error)
            ? new LaunchResult(LaunchKind.OpenedTopPage, DefaultAppsUri)
            : new LaunchResult(LaunchKind.Failed, DefaultAppsUri, error);
    }

    /// <summary>Opens the top-level Default apps page (no specific browser).</summary>
    public LaunchResult OpenTopPage()
    {
        if (_policy.Inspect().BlocksNavigation)
        {
            return new LaunchResult(LaunchKind.BlockedByPolicy, null);
        }

        return TryLaunch(DefaultAppsUri, out var error)
            ? new LaunchResult(LaunchKind.OpenedTopPage, DefaultAppsUri)
            : new LaunchResult(LaunchKind.Failed, DefaultAppsUri, error);
    }

    private bool TryLaunch(Uri uri, out string? error)
    {
        try
        {
            _launcher.Launch(uri);
            error = null;
            return true;
        }
#pragma warning disable CA1031 // Any shell failure must degrade to a user-visible error, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            error = ex.Message;
            return false;
        }
    }
}
