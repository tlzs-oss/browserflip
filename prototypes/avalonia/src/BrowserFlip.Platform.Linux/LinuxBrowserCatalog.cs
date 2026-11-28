using System.Globalization;
using BrowserFlip.Core.Detection;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Linux;

/// <summary>
/// Linux implementation of <see cref="IBrowserCatalog"/>, over the XDG desktop entry database.
/// It produces the very same <see cref="BrowserInfo"/> records the Windows detector and the macOS
/// catalogue produce, so everything downstream (matching, status, change tracking, the whole UI)
/// is shared across all three platforms.
/// </summary>
/// <remarks>
/// <para>
/// Search order follows the Desktop Entry Specification: <c>$XDG_DATA_HOME/applications</c>
/// (default <c>~/.local/share/applications</c>) first, then each <c>$XDG_DATA_DIRS</c> entry
/// (default <c>/usr/local/share:/usr/share</c>). The first directory to define a desktop file id
/// wins, which is how a user overrides a system-wide entry.
/// </para>
/// <para>
/// Mapping from the Windows model to Linux:
/// <list type="bullet">
///   <item><description><c>Id</c> / <c>HttpProgId</c> / <c>HttpsProgId</c> — the desktop file id
///     (<c>firefox.desktop</c>), which plays exactly the ProgID role: a stable handler token, and
///     literally the token <c>xdg-settings set default-web-browser</c> takes.</description></item>
///   <item><description><c>ExecutablePath</c> — the binary parsed out of <c>Exec</c>, resolved
///     against <c>PATH</c> when it is not already absolute.</description></item>
///   <item><description><c>IconPath</c> — the raw <c>Icon</c> value, which is usually a themed
///     icon <em>name</em> rather than a path; <see cref="LinuxAppIconProvider"/> resolves it.</description></item>
///   <item><description><c>Scope</c> — <c>User</c> for entries under the user's home, otherwise
///     <c>Machine</c>.</description></item>
///   <item><description><c>RegisteredApplicationName</c> — the desktop file id; Linux needs no
///     separate registration list, so it is always present and <c>NotInRegisteredApplications</c>
///     never occurs.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class LinuxBrowserCatalog : IBrowserCatalog
{
    /// <summary>A desktop entry is a web browser when it handles either web scheme.</summary>
    private static readonly string[] WebSchemeHandlers = ["x-scheme-handler/https", "x-scheme-handler/http"];

    /// <summary>Sub-directory of each XDG data directory that holds desktop entries.</summary>
    private const string ApplicationsSubdirectory = "applications";

    /// <summary>Guards against walking a pathological directory tree; real ones are 1-2 deep.</summary>
    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 4,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private readonly IReadOnlyList<string> _applicationDirectories;
    private readonly string _homeDirectory;
    private readonly Func<string, string?> _resolveExecutable;

    public LinuxBrowserCatalog()
        : this(
            ApplicationDirectories(Environment.GetEnvironmentVariable, HomeDirectory()),
            HomeDirectory(),
            binary => LinuxExecutables.Resolve(binary, Environment.GetEnvironmentVariable))
    {
    }

    /// <summary>
    /// Test seam: the directories to scan, the home directory that decides
    /// <see cref="RegistrationScope"/>, and how a binary name becomes a real path.
    /// </summary>
    internal LinuxBrowserCatalog(
        IReadOnlyList<string> applicationDirectories,
        string homeDirectory,
        Func<string, string?> resolveExecutable)
    {
        ArgumentNullException.ThrowIfNull(applicationDirectories);
        ArgumentNullException.ThrowIfNull(resolveExecutable);
        _applicationDirectories = applicationDirectories;
        _homeDirectory = homeDirectory ?? string.Empty;
        _resolveExecutable = resolveExecutable;
    }

    public IReadOnlyList<BrowserInfo> GetBrowsers() =>
    [
        .. Discover()
            .Select(found => ToBrowserInfo(found.Id, found.Entry, found.Directory, _homeDirectory, _resolveExecutable))
            .OrderBy(browser => browser.DisplayName, StringComparer.CurrentCultureIgnoreCase)
    ];

    /// <summary>
    /// Every web-browser desktop entry, keyed by desktop file id, with earlier (more
    /// user-specific) directories winning. Never throws: an unreadable file or directory is
    /// skipped, because one broken entry must not empty the browser list.
    /// </summary>
    internal IReadOnlyList<(string Id, DesktopEntry Entry, string Directory)> Discover()
    {
        var found = new List<(string Id, DesktopEntry Entry, string Directory)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var directory in _applicationDirectories)
        {
            foreach (var file in EnumerateDesktopFiles(directory))
            {
                var id = DesktopEntry.DesktopFileId(directory, file);

                // Reserve the id even when the entry turns out not to be a browser: a user's own
                // entry that drops the web schemes must still shadow the system-wide one.
                if (!seen.Add(id))
                {
                    continue;
                }

                var entry = DesktopEntry.Parse(ReadAllText(file));
                if (entry is null || entry.IsSuppressed || !HandlesWebUrls(entry))
                {
                    continue;
                }

                found.Add((id, entry, directory));
            }
        }

        return found;
    }

    /// <summary>
    /// The <c>applications</c> directories to scan, most specific first, following
    /// <c>$XDG_DATA_HOME</c> and <c>$XDG_DATA_DIRS</c> with the spec's defaults. Duplicates are
    /// removed so a distribution that repeats <c>/usr/share</c> cannot shadow itself.
    /// </summary>
    internal static IReadOnlyList<string> ApplicationDirectories(Func<string, string?> getEnvironmentVariable, string homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var dataHome = Trimmed(getEnvironmentVariable("XDG_DATA_HOME"))
                       ?? PosixPath.Join(homeDirectory ?? string.Empty, ".local", "share");
        var dataDirs = Trimmed(getEnvironmentVariable("XDG_DATA_DIRS")) ?? "/usr/local/share:/usr/share";

        var directories = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Add(dataHome);
        foreach (var dataDir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            Add(dataDir);
        }

        return directories;

        void Add(string dataDirectory)
        {
            var trimmed = dataDirectory.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            var applications = PosixPath.Join(trimmed, ApplicationsSubdirectory);
            if (seen.Add(applications))
            {
                directories.Add(applications);
            }
        }

        static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>True when the entry declares itself a handler for <c>http</c> or <c>https</c> URLs.</summary>
    internal static bool HandlesWebUrls(DesktopEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        foreach (var handler in WebSchemeHandlers)
        {
            if (entry.Handles(handler))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Maps one desktop entry onto the platform-neutral <see cref="BrowserInfo"/>.</summary>
    internal static BrowserInfo ToBrowserInfo(
        string desktopFileId,
        DesktopEntry entry,
        string applicationsDirectory,
        string homeDirectory,
        Func<string, string?> resolveExecutable,
        CultureInfo? culture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopFileId);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(resolveExecutable);

        var binary = entry.ExecutableName;
        var resolved = binary is null ? null : resolveExecutable(binary);

        // TryExec exists precisely to answer "is this entry usable?", so it decides health when
        // present; otherwise the binary from Exec does.
        var healthProbe = string.IsNullOrWhiteSpace(entry.TryExec) ? resolved : resolveExecutable(entry.TryExec);

        var perUser = IsUnderHome(applicationsDirectory, homeDirectory);
        var scope = perUser ? RegistrationScope.User : RegistrationScope.Machine;

        return new BrowserInfo
        {
            Id = desktopFileId,
            // Fall back to the id without its extension: a browser with no Name key must still
            // be listed rather than disappear from the UI.
            DisplayName = entry.NameFor(culture ?? CultureInfo.CurrentUICulture) is { Length: > 0 } name
                ? name
                : StripExtension(desktopFileId),
            // The resolved path when we have one, otherwise the raw binary so the UI can still
            // show what the entry asked for.
            ExecutablePath = resolved ?? binary,
            IconPath = entry.Icon,
            IconIndex = 0,
            HttpProgId = desktopFileId,
            HttpsProgId = desktopFileId,
            RegisteredApplicationName = desktopFileId,
            RegisteredApplicationScope = scope,
            Scope = scope,
            Health = healthProbe is null ? BrowserHealth.ExecutableMissing : BrowserHealth.Ok,
        };
    }

    /// <summary>
    /// True when <paramref name="path"/> lives inside the user's home directory. The trailing
    /// separator matters: without it "/home/bobby/..." counts as living inside "/home/bob".
    /// </summary>
    internal static bool IsUnderHome(string path, string homeDirectory)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(homeDirectory))
        {
            return false;
        }

        // Normalise both sides: during a directory walk the path can carry the host's separator
        // while the configured home is POSIX, and the comparison must not depend on which.
        var normalisedPath = path.Replace('\\', PosixPath.Separator);
        var prefix = homeDirectory.Replace('\\', PosixPath.Separator).TrimEnd(PosixPath.Separator) + PosixPath.Separator;
        return normalisedPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string StripExtension(string desktopFileId)
        => desktopFileId.EndsWith(DesktopEntry.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? desktopFileId[..^DesktopEntry.FileExtension.Length]
            : desktopFileId;

    private static string HomeDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static IReadOnlyList<string> EnumerateDesktopFiles(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            // Sorted so the result does not depend on the file system's enumeration order.
            return [.. Directory.EnumerateFiles(directory, "*" + DesktopEntry.FileExtension, Enumeration).Order(StringComparer.Ordinal)];
        }
#pragma warning disable CA1031 // An unreadable directory must not empty the browser list.
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }
    }

    private static string? ReadAllText(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
#pragma warning disable CA1031 // An unreadable entry is skipped, never fatal.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
