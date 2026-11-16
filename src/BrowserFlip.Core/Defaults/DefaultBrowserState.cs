using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;

namespace BrowserFlip.Core.Defaults;

/// <summary>Overall default-browser status.</summary>
public enum DefaultStatus
{
    /// <summary>No default handler is set for http or https.</summary>
    None,

    /// <summary>http and https are both handled by the same detected browser.</summary>
    Consistent,

    /// <summary>http and https are handled by different applications (or only one is set).</summary>
    Mismatch,

    /// <summary>Same handler for both protocols, but it is not one of the detected browsers.</summary>
    Unrecognized,
}

/// <summary>How a given browser relates to the current defaults.</summary>
public enum BrowserDefaultRole
{
    NotDefault,

    /// <summary>Default for http only.</summary>
    HttpOnly,

    /// <summary>Default for https only.</summary>
    HttpsOnly,

    /// <summary>Default for both http and https.</summary>
    Both,
}

/// <summary>Snapshot of the http/https default handlers.</summary>
public sealed record DefaultBrowserState(
    AssociationInfo? Http,
    AssociationInfo? Https,
    BrowserInfo? HttpBrowser,
    BrowserInfo? HttpsBrowser)
{
    public static DefaultBrowserState Empty { get; } = new(null, null, null, null);

    public DefaultStatus Status
    {
        get
        {
            if (Http is null && Https is null)
            {
                return DefaultStatus.None;
            }

            if (Http is null || Https is null)
            {
                return DefaultStatus.Mismatch;
            }

            if (HttpBrowser is not null && HttpsBrowser is not null)
            {
                return string.Equals(HttpBrowser.Id, HttpsBrowser.Id, StringComparison.OrdinalIgnoreCase)
                    ? DefaultStatus.Consistent
                    : DefaultStatus.Mismatch;
            }

            if (HttpBrowser is null && HttpsBrowser is null)
            {
                return string.Equals(Http.ProgId, Https.ProgId, StringComparison.OrdinalIgnoreCase)
                    ? DefaultStatus.Unrecognized
                    : DefaultStatus.Mismatch;
            }

            // One protocol maps to a known browser, the other does not.
            return DefaultStatus.Mismatch;
        }
    }

    /// <summary>Returns how <paramref name="browser"/> relates to the current defaults.</summary>
    public BrowserDefaultRole RoleOf(BrowserInfo browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        var http = HttpBrowser is not null && string.Equals(HttpBrowser.Id, browser.Id, StringComparison.OrdinalIgnoreCase);
        var https = HttpsBrowser is not null && string.Equals(HttpsBrowser.Id, browser.Id, StringComparison.OrdinalIgnoreCase);
        return (http, https) switch
        {
            (true, true) => BrowserDefaultRole.Both,
            (true, false) => BrowserDefaultRole.HttpOnly,
            (false, true) => BrowserDefaultRole.HttpsOnly,
            _ => BrowserDefaultRole.NotDefault,
        };
    }

    /// <summary>True when the effective ProgIDs are identical to <paramref name="other"/>'s.</summary>
    public bool SameAssociationsAs(DefaultBrowserState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Http?.ProgId, other.Http?.ProgId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Https?.ProgId, other.Https?.ProgId, StringComparison.OrdinalIgnoreCase);
    }
}
