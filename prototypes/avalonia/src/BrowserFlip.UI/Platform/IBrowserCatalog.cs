using BrowserFlip.Core.Detection;

namespace BrowserFlip.UI.Platform;

/// <summary>
/// Enumerates the browsers installed on this machine, as the platform-neutral
/// <see cref="BrowserInfo"/> model that <c>BrowserFlip.Core</c> already defines.
/// </summary>
/// <remarks>
/// Windows implements this over the registry (<c>BrowserFlip.Core.Detection.BrowserDetector</c>);
/// macOS over LaunchServices; Linux over the XDG desktop database.
/// </remarks>
public interface IBrowserCatalog
{
    /// <summary>Returns the installed browsers, sorted by display name. Never throws.</summary>
    IReadOnlyList<BrowserInfo> GetBrowsers();
}
