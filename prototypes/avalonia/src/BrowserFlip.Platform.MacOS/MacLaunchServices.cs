using System.Runtime.Versioning;
using BrowserFlip.Platform.MacOS.Interop;

namespace BrowserFlip.Platform.MacOS;

/// <summary>An application bundle that can handle web URLs.</summary>
/// <param name="BundlePath">Absolute path of the <c>.app</c> bundle.</param>
/// <param name="BundleIdentifier">e.g. <c>com.apple.Safari</c>. The macOS analogue of a Windows ProgID.</param>
/// <param name="DisplayName">Localised application name.</param>
internal sealed record MacApp(string BundlePath, string? BundleIdentifier, string DisplayName);

/// <summary>
/// Reads the live URL-scheme associations. Separated from <see cref="MacLaunchServices"/> so
/// the switcher's own logic - how many consent prompts to raise, and what the outcome is - can
/// be tested without any test being able to raise a real OS consent alert.
/// </summary>
internal interface IMacAssociationReader
{
    /// <summary>The application currently handling <paramref name="scheme"/>, or <c>null</c>.</summary>
    MacApp? GetHandlerForScheme(string scheme);

    /// <summary>The application currently handling <c>https</c>, or <c>null</c> when none is set.</summary>
    MacApp? GetDefaultWebBrowser();
}

/// <summary>
/// Thin wrapper over the public LaunchServices surface of <c>NSWorkspace</c>.
/// Read operations only; changing the default browser lives in
/// <see cref="MacDefaultBrowserSwitcher"/> because it shows an OS consent prompt.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacLaunchServices : IMacAssociationReader
{
    private const string ProbeUrl = "https://example.invalid/";

    /// <summary>True when AppKit could be loaded and NSWorkspace is reachable.</summary>
    internal static bool IsAvailable => ObjC.EnsureAppKit();

    /// <summary>Every application registered as able to open <c>https</c> URLs.</summary>
    internal IReadOnlyList<MacApp> GetWebBrowsers()
    {
        if (!IsAvailable)
        {
            return [];
        }

        var workspace = ObjC.SharedWorkspace();
        var urls = ObjC.Send(workspace, ObjC.Sel("URLsForApplicationsToOpenURL:"), ObjC.Url(ProbeUrl));
        var result = new List<MacApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in ObjC.ArrayItems(urls))
        {
            var app = Describe(url);
            if (app is not null && seen.Add(app.BundlePath))
            {
                result.Add(app);
            }
        }

        return result;
    }

    /// <summary>The application currently handling <c>https</c>, or <c>null</c> when none is set.</summary>
    public MacApp? GetDefaultWebBrowser()
    {
        if (!IsAvailable)
        {
            return null;
        }

        var workspace = ObjC.SharedWorkspace();
        var url = ObjC.Send(workspace, ObjC.Sel("URLForApplicationToOpenURL:"), ObjC.Url(ProbeUrl));
        return Describe(url);
    }

    /// <summary>The application handling <paramref name="scheme"/> (<c>http</c> / <c>https</c>).</summary>
    public MacApp? GetHandlerForScheme(string scheme)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(scheme))
        {
            return null;
        }

        var workspace = ObjC.SharedWorkspace();
        var url = ObjC.Send(workspace, ObjC.Sel("URLForApplicationToOpenURL:"), ObjC.Url($"{scheme}://example.invalid/"));
        return Describe(url);
    }

    /// <summary>Reads bundle identifier and display name for an application NSURL.</summary>
    private static MacApp? Describe(IntPtr appUrl)
    {
        var path = ObjC.UrlPath(appUrl);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string? bundleId = null;
        string? name = null;
        var bundle = ObjC.Send(ObjC.GetClass("NSBundle"), ObjC.Sel("bundleWithURL:"), appUrl);
        if (bundle != ObjC.Nil)
        {
            bundleId = ObjC.ToManaged(ObjC.Send(bundle, ObjC.Sel("bundleIdentifier")));
            name = InfoString(bundle, "CFBundleDisplayName") ?? InfoString(bundle, "CFBundleName");
        }

        name = string.IsNullOrWhiteSpace(name) ? FileNameOf(path) : name.Trim();
        return new MacApp(path, string.IsNullOrWhiteSpace(bundleId) ? null : bundleId, name);
    }

    private static string? InfoString(IntPtr bundle, string key)
        => ObjC.ToManaged(ObjC.Send(bundle, ObjC.Sel("objectForInfoDictionaryKey:"), ObjC.NSString(key)));

    /// <summary>"/Applications/Google Chrome.app" -> "Google Chrome".</summary>
    internal static string FileNameOf(string bundlePath)
    {
        var leaf = Path.GetFileName(bundlePath.TrimEnd('/'));
        return leaf.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? leaf[..^4] : leaf;
    }
}
