using BrowserFlip.Core.Platform;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Linux;

/// <summary>
/// Linux implementation of the Core interface <see cref="IAssociationQuery"/> (read-only).
/// Because it satisfies the interface the Windows build already uses, the Core types
/// <c>DefaultBrowserService</c>, <c>DefaultBrowserState</c> and <c>ChangeTracker</c> run on Linux
/// with no change at all.
/// </summary>
/// <remarks>
/// <c>http</c> and <c>https</c> are answered by <c>xdg-settings get default-web-browser</c>,
/// which is what every desktop agrees on for "the default browser". Any other scheme falls back
/// to <c>xdg-mime query default x-scheme-handler/&lt;scheme&gt;</c>. The desktop file id plays the
/// ProgID role, so <c>AssociationInfo.ProgId</c> is e.g. <c>firefox.desktop</c> and compares
/// directly against <see cref="BrowserFlip.Core.Detection.BrowserInfo.HttpsProgId"/>.
/// </remarks>
public sealed class LinuxAssociationQuery : IAssociationQuery
{
    private readonly XdgSettings _settings;
    private readonly IBrowserCatalog _catalog;

    public LinuxAssociationQuery() : this(new XdgSettings(), new LinuxBrowserCatalog())
    {
    }

    /// <summary>
    /// Test seam. The catalogue is used only to put a friendly name and a binary next to the id:
    /// those live in the desktop entry, not in anything <c>xdg-settings</c> prints.
    /// </summary>
    internal LinuxAssociationQuery(XdgSettings settings, IBrowserCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);
        _settings = settings;
        _catalog = catalog;
    }

    public AssociationInfo? QueryProtocol(string protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        var id = IsWebProtocol(protocol)
            ? _settings.GetDefaultWebBrowser()
            : null;

        // Either the protocol is not a web scheme, or xdg-settings had no answer (it is missing,
        // or the desktop keeps only the MIME association). xdg-mime is the broader fallback.
        id ??= _settings.QueryMimeHandler("x-scheme-handler/" + protocol);

        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var desktopFileId = NormaliseDesktopFileId(id);
        var browser = FindBrowser(desktopFileId);
        return new AssociationInfo(desktopFileId, browser?.DisplayName, browser?.ExecutablePath);
    }

    /// <summary>True for the two schemes <c>xdg-settings</c> has a dedicated property for.</summary>
    internal static bool IsWebProtocol(string protocol)
        => string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase)
           || string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The tools normally print a full desktop file id. Some distributions' wrappers print the
    /// bare name, so the extension is added when missing to keep the id comparable with
    /// <see cref="BrowserFlip.Core.Detection.BrowserInfo.Id"/>.
    /// </summary>
    internal static string NormaliseDesktopFileId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        return trimmed.EndsWith(DesktopEntry.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + DesktopEntry.FileExtension;
    }

    private BrowserFlip.Core.Detection.BrowserInfo? FindBrowser(string desktopFileId)
    {
        foreach (var browser in _catalog.GetBrowsers())
        {
            // Desktop file ids are file names on a case-sensitive file system: compare exactly.
            if (string.Equals(browser.Id, desktopFileId, StringComparison.Ordinal))
            {
                return browser;
            }
        }

        return null;
    }
}
