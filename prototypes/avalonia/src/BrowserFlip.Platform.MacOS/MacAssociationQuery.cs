using System.Runtime.Versioning;
using BrowserFlip.Core.Platform;

namespace BrowserFlip.Platform.MacOS;

/// <summary>
/// macOS implementation of the Core interface <see cref="IAssociationQuery"/> (read-only).
/// Because it satisfies the interface the Windows build already uses, the Core types
/// <c>DefaultBrowserService</c>, <c>DefaultBrowserState</c> and <c>ChangeTracker</c> run on macOS
/// with no change at all.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacAssociationQuery : IAssociationQuery
{
    private readonly MacLaunchServices _launchServices;

    public MacAssociationQuery() : this(new MacLaunchServices())
    {
    }

    internal MacAssociationQuery(MacLaunchServices launchServices) => _launchServices = launchServices;

    public AssociationInfo? QueryProtocol(string protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        var app = _launchServices.GetHandlerForScheme(protocol);
        if (app is null)
        {
            return null;
        }

        // ProgId <- bundle identifier; ExecutablePath <- bundle path.
        return new AssociationInfo(app.BundleIdentifier ?? app.BundlePath, app.DisplayName, app.BundlePath);
    }
}
