using System.Runtime.Versioning;
using BrowserFlip.Core.Detection;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.MacOS;

/// <summary>
/// macOS implementation of <see cref="IBrowserCatalog"/>. It produces the very same
/// <see cref="BrowserInfo"/> records the Windows detector produces, so everything downstream
/// (matching, status, change tracking, the whole UI) is shared between the two platforms.
/// </summary>
/// <remarks>
/// Mapping from the Windows model to macOS:
/// <list type="bullet">
///   <item><description><c>Id</c> / <c>HttpProgId</c> / <c>HttpsProgId</c> — the bundle identifier
///     (<c>com.apple.Safari</c>), which plays exactly the ProgID role: a stable handler token.</description></item>
///   <item><description><c>ExecutablePath</c> — the <c>.app</c> bundle path.</description></item>
///   <item><description><c>Scope</c> — <c>User</c> for bundles under the user's home, otherwise <c>Machine</c>.</description></item>
///   <item><description><c>RegisteredApplicationName</c> — the bundle identifier; macOS needs no
///     separate registration list, so it is always present and <c>NotInRegisteredApplications</c> never occurs.</description></item>
/// </list>
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacBrowserCatalog : IBrowserCatalog
{
    private readonly MacLaunchServices _launchServices;

    public MacBrowserCatalog() : this(new MacLaunchServices())
    {
    }

    internal MacBrowserCatalog(MacLaunchServices launchServices) => _launchServices = launchServices;

    public IReadOnlyList<BrowserInfo> GetBrowsers()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            .. _launchServices.GetWebBrowsers()
                .Select(app => ToBrowserInfo(app, home))
                .OrderBy(b => b.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    internal static BrowserInfo ToBrowserInfo(MacApp app, string homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(app);
        // The bundle identifier is the handler token; fall back to the bundle path when a bundle
        // has none (rare, but possible for unsigned or malformed bundles).
        var id = app.BundleIdentifier ?? app.BundlePath;
        // The trailing separator matters: without it "/Users/bobby/..." counts as living inside
        // the home directory "/Users/bob".
        var homePrefix = string.IsNullOrEmpty(homeDirectory)
            ? null
            : homeDirectory.TrimEnd('/') + '/';
        var perUser = homePrefix is not null
                      && app.BundlePath.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase);

        return new BrowserInfo
        {
            Id = id,
            DisplayName = app.DisplayName,
            ExecutablePath = app.BundlePath,
            IconPath = app.BundlePath,
            IconIndex = 0,
            HttpProgId = id,
            HttpsProgId = id,
            RegisteredApplicationName = id,
            RegisteredApplicationScope = perUser ? RegistrationScope.User : RegistrationScope.Machine,
            Scope = perUser ? RegistrationScope.User : RegistrationScope.Machine,
            Health = Directory.Exists(app.BundlePath) ? BrowserHealth.Ok : BrowserHealth.ExecutableMissing,
        };
    }
}
