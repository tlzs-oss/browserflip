using System.Globalization;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Linux;

/// <summary>
/// Resolves a desktop entry's <c>Icon=</c> value to PNG bytes, following the XDG Icon Theme
/// Specification's directory layout.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IAppIconProvider"/> parameter is named <c>applicationPath</c> because on Windows
/// and macOS an icon is read out of the application itself. On Linux it is the <c>Icon=</c> value
/// instead, which is either an absolute path or — far more often — a themed icon <em>name</em>
/// such as <c>firefox</c>, looked up under <c>$XDG_DATA_HOME/icons</c>, <c>~/.icons</c>,
/// <c>$XDG_DATA_DIRS/icons</c> and <c>/usr/share/pixmaps</c>.
/// </para>
/// <para>
/// Known limitations, deliberate for this round:
/// <list type="bullet">
///   <item><description><b>SVG icons return <c>null</c>.</b> Several browsers ship only
///     <c>scalable/apps/&lt;name&gt;.svg</c>, and rasterising it would mean taking an SVG renderer
///     dependency. The UI must therefore keep its placeholder icon working on Linux.</description></item>
///   <item><description>The user's configured icon theme and its <c>index.theme</c> inheritance
///     are not parsed; every theme directory is searched and the best-sized PNG wins. That finds
///     an icon reliably, but not necessarily the themed variant.</description></item>
/// </list>
/// </para>
/// <para>Never throws: a missing icon must never break the browser list.</para>
/// </remarks>
public sealed class LinuxAppIconProvider : IAppIconProvider
{
    /// <summary>PNG first, SVG only so that "an icon exists but cannot be decoded" is detectable.</summary>
    private static readonly string[] IconExtensions = [".png", ".svg"];

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Theme layouts nest at most &lt;theme&gt;/&lt;size&gt;/&lt;context&gt;/icon.png.</summary>
    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 5,
        IgnoreInaccessible = true,
    };

    private readonly IReadOnlyList<string> _searchDirectories;

    public LinuxAppIconProvider()
        : this(IconSearchDirectories(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
    {
    }

    /// <summary>Test seam: the icon directories to search, most specific first.</summary>
    internal LinuxAppIconProvider(IReadOnlyList<string> searchDirectories)
    {
        ArgumentNullException.ThrowIfNull(searchDirectories);
        _searchDirectories = searchDirectories;
    }

    public byte[]? GetIconPng(string? applicationPath, int pixelSize = 64)
    {
        if (string.IsNullOrWhiteSpace(applicationPath))
        {
            return null;
        }

        try
        {
            var icon = applicationPath.Trim();

            // An absolute path is used directly, as the spec requires.
            if (Path.IsPathRooted(icon))
            {
                return ReadPng(icon);
            }

            // Icon= should carry no extension, but plenty of real entries include one.
            var name = Path.GetFileNameWithoutExtension(icon);
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var chosen = ChooseIcon(FindCandidates(name), pixelSize);
            return chosen is null ? null : ReadPng(chosen);
        }
#pragma warning disable CA1031 // A missing or unreadable icon must never break the list.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>
    /// The icon directories to search, most specific first:
    /// <c>$XDG_DATA_HOME/icons</c>, <c>~/.icons</c>, each <c>$XDG_DATA_DIRS/icons</c>, and the
    /// legacy flat <c>/usr/share/pixmaps</c> last.
    /// </summary>
    internal static IReadOnlyList<string> IconSearchDirectories(Func<string, string?> getEnvironmentVariable, string homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var home = homeDirectory ?? string.Empty;
        var dataHome = Value(getEnvironmentVariable("XDG_DATA_HOME")) ?? PosixPath.Join(home, ".local", "share");
        var dataDirs = Value(getEnvironmentVariable("XDG_DATA_DIRS")) ?? "/usr/local/share:/usr/share";

        var directories = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Add(PosixPath.Join(dataHome, "icons"));
        Add(PosixPath.Join(home, ".icons"));
        foreach (var dataDir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            Add(PosixPath.Join(dataDir.Trim(), "icons"));
        }

        Add("/usr/share/pixmaps");
        return directories;

        void Add(string directory)
        {
            if (directory.Length > 0 && seen.Add(directory))
            {
                directories.Add(directory);
            }
        }

        static string? Value(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Picks the best candidate: the smallest PNG at least <paramref name="pixelSize"/> across,
    /// else the largest PNG, else an SVG (which <see cref="GetIconPng"/> then declines to decode).
    /// Candidate order breaks ties, so a more specific directory wins.
    /// </summary>
    internal static string? ChooseIcon(IEnumerable<string> candidates, int pixelSize)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        string? smallestBigEnough = null;
        var smallestBigEnoughSize = int.MaxValue;
        string? largest = null;
        var largestSize = -1;
        string? scalable = null;

        foreach (var candidate in candidates)
        {
            if (candidate.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                scalable ??= candidate;
                continue;
            }

            if (!candidate.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var size = SizeFromPath(candidate);
            if (size >= pixelSize && size < smallestBigEnoughSize)
            {
                smallestBigEnoughSize = size;
                smallestBigEnough = candidate;
            }

            if (size > largestSize)
            {
                largestSize = size;
                largest = candidate;
            }
        }

        return smallestBigEnough ?? largest ?? scalable;
    }

    /// <summary>
    /// The icon size encoded in a theme path, e.g. 48 for <c>.../48x48/apps/firefox.png</c>, or 0
    /// when the path says nothing (a flat <c>pixmaps</c> icon, or a <c>scalable</c> directory).
    /// </summary>
    internal static int SizeFromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        foreach (var rawSegment in path.Split('/', '\\'))
        {
            // Some themes write "48x48@2x"; the base size is what matters here.
            var segment = rawSegment.Split('@', 2)[0];
            var separator = segment.IndexOf('x');
            if (separator <= 0 || separator == segment.Length - 1)
            {
                continue;
            }

            if (int.TryParse(segment[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
                && int.TryParse(segment[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
                && width == height)
            {
                return width;
            }
        }

        return 0;
    }

    /// <summary>True when <paramref name="bytes"/> starts with the PNG file signature.</summary>
    internal static bool IsPng(byte[]? bytes)
        => bytes is not null
           && bytes.Length > PngSignature.Length
           && bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature);

    private List<string> FindCandidates(string name)
    {
        var candidates = new List<string>();
        foreach (var directory in _searchDirectories)
        {
            foreach (var extension in IconExtensions)
            {
                candidates.AddRange(Enumerate(directory, name + extension));
            }
        }

        return candidates;
    }

    private static IReadOnlyList<string> Enumerate(string directory, string fileName)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            // Sorted so the chosen icon does not depend on file-system enumeration order.
            return [.. Directory.EnumerateFiles(directory, fileName, Enumeration).Order(StringComparer.Ordinal)];
        }
#pragma warning disable CA1031 // An unreadable icon directory is simply skipped.
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }
    }

    /// <summary>
    /// Reads a file and returns its bytes only when they really are a PNG: the contract is
    /// "PNG bytes or null", so an SVG or an XPM must not be handed to the image decoder.
    /// </summary>
    private static byte[]? ReadPng(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            return IsPng(bytes) ? bytes : null;
        }
#pragma warning disable CA1031 // An unreadable icon file is simply no icon.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
