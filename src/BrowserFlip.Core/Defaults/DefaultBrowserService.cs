using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;

namespace BrowserFlip.Core.Defaults;

/// <summary>Reads the current http/https defaults and maps them to detected browsers (read-only).</summary>
public sealed class DefaultBrowserService
{
    private readonly IAssociationQuery _query;

    public DefaultBrowserService(IAssociationQuery query) => _query = query;

    public DefaultBrowserState GetState(IReadOnlyList<BrowserInfo> browsers)
    {
        ArgumentNullException.ThrowIfNull(browsers);
        var http = _query.QueryProtocol("http");
        var https = _query.QueryProtocol("https");
        return new DefaultBrowserState(http, https, Match(browsers, http, "http"), Match(browsers, https, "https"));
    }

    internal static BrowserInfo? Match(IReadOnlyList<BrowserInfo> browsers, AssociationInfo? assoc, string protocol)
    {
        if (assoc is null)
        {
            return null;
        }

        // Prefer an exact protocol match, then any ProgID the browser owns.
        var exact = browsers.FirstOrDefault(b => string.Equals(
            protocol == "http" ? b.HttpProgId : b.HttpsProgId, assoc.ProgId, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var owned = browsers.FirstOrDefault(b => b.OwnsProgId(assoc.ProgId));
        if (owned is not null)
        {
            return owned;
        }

        // Last resort: same executable path (e.g. ProgID renamed by an update).
        if (!string.IsNullOrEmpty(assoc.ExecutablePath))
        {
            var target = SafeFullPath(assoc.ExecutablePath);
            return target is null
                ? null
                : browsers.FirstOrDefault(b => string.Equals(SafeFullPath(b.ExecutablePath), target, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static string? SafeFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
