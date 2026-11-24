using System.Globalization;
using System.Text;

namespace BrowserFlip.Platform.Linux;

/// <summary>
/// The part of a freedesktop.org Desktop Entry that BrowserFlip needs, parsed from the file's
/// text. A <c>.desktop</c> file is the Linux analogue of a Windows <c>StartMenuInternet</c>
/// registration: it names the application, the binary to run and the URL schemes it handles.
/// </summary>
/// <remarks>
/// <para>
/// The parser takes the file <em>content</em>, never a path, so every rule below is unit-testable
/// on any operating system. Only the <c>[Desktop Entry]</c> group is read: the additional groups
/// (<c>[Desktop Action new-window]</c> and friends) carry their own <c>Name</c> and <c>Exec</c>
/// keys, and treating those as the application's own would mislabel browsers.
/// </para>
/// <para>
/// Implemented against the Desktop Entry Specification 1.5. Deliberately incomplete: BrowserFlip
/// only ever reads, and only needs the keys below.
/// </para>
/// </remarks>
internal sealed record DesktopEntry
{
    /// <summary>The only group this parser reads.</summary>
    internal const string GroupName = "Desktop Entry";

    /// <summary>File-name suffix of a desktop entry, which is also part of its id.</summary>
    internal const string FileExtension = ".desktop";

    /// <summary>Field codes the spec defines for <c>Exec</c>; none of them is part of the binary.</summary>
    private const string FieldCodeLetters = "fFuUdDnNickvm";

    /// <summary>Unlocalised <c>Name</c>, or <c>null</c> when the file has none.</summary>
    internal string? Name { get; init; }

    /// <summary>Localised names keyed by the locale in <c>Name[..]</c>, e.g. <c>ja</c> or <c>pt_BR</c>.</summary>
    internal IReadOnlyDictionary<string, string> LocalisedNames { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raw <c>Exec</c> value, including quoting and field codes.</summary>
    internal string? Exec { get; init; }

    /// <summary>Raw <c>Icon</c> value: either an absolute path or a themed icon name.</summary>
    internal string? Icon { get; init; }

    /// <summary>
    /// <c>TryExec</c>: a binary whose presence tells the desktop whether the entry is usable.
    /// Preferred over <see cref="Exec"/> for health checks, which is exactly what it exists for.
    /// </summary>
    internal string? TryExec { get; init; }

    /// <summary>MIME types from <c>MimeType</c>, e.g. <c>x-scheme-handler/https</c>.</summary>
    internal IReadOnlyList<string> MimeTypes { get; init; } = [];

    /// <summary><c>NoDisplay=true</c>: the entry exists but must not be shown to the user.</summary>
    internal bool NoDisplay { get; init; }

    /// <summary><c>Hidden=true</c>: the entry is deleted as far as the desktop is concerned.</summary>
    internal bool Hidden { get; init; }

    /// <summary>True when the user must never see this entry in a browser list.</summary>
    internal bool IsSuppressed => NoDisplay || Hidden;

    /// <summary>
    /// The binary from <see cref="Exec"/>, with quoting resolved and field codes removed, or
    /// <c>null</c> when <c>Exec</c> is missing or holds nothing but field codes.
    /// </summary>
    internal string? ExecutableName => ParseExecutable(Exec);

    /// <summary>True when the entry declares <paramref name="mimeType"/> in <c>MimeType</c>.</summary>
    internal bool Handles(string mimeType)
    {
        foreach (var declared in MimeTypes)
        {
            // MIME types are defined to be case-insensitive, and real files are inconsistent.
            if (string.Equals(declared, mimeType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The name to show for <paramref name="culture"/>: the most specific matching
    /// <c>Name[..]</c>, then the language-only variant, then the unlocalised <c>Name</c>.
    /// </summary>
    internal string? NameFor(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        foreach (var candidate in LocaleCandidates(culture))
        {
            if (LocalisedNames.TryGetValue(candidate, out var localised) && !string.IsNullOrWhiteSpace(localised))
            {
                return localised;
            }
        }

        return Name;
    }

    /// <summary>
    /// Locale keys to try, most specific first. Desktop entries spell locales the POSIX way
    /// (<c>pt_BR</c>, <c>sr@latin</c>) while .NET spells them <c>pt-BR</c>, so both are tried.
    /// </summary>
    private static IEnumerable<string> LocaleCandidates(CultureInfo culture)
    {
        var name = culture.Name;
        if (name.Length == 0)
        {
            yield break;
        }

        yield return name.Replace('-', '_');
        yield return name;

        var language = culture.TwoLetterISOLanguageName;
        if (!string.Equals(language, name, StringComparison.OrdinalIgnoreCase))
        {
            yield return language;
        }
    }

    /// <summary>
    /// Parses desktop-entry text. Returns <c>null</c> when the content has no
    /// <c>[Desktop Entry]</c> group, which is the spec's definition of "not a desktop entry".
    /// Never throws: a malformed file is data, not an error.
    /// </summary>
    internal static DesktopEntry? Parse(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        var seenGroup = false;
        var inGroup = false;
        string? name = null;
        string? exec = null;
        string? icon = null;
        string? tryExec = null;
        var localisedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> mimeTypes = [];
        var noDisplay = false;
        var hidden = false;
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        // Split on '\n' and trim '\r' so a CRLF file (they exist, written by cross-platform
        // installers) parses identically to an LF one.
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim('\r').Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                var end = line.IndexOf(']');
                var group = end > 0 ? line[1..end] : line[1..];
                inGroup = string.Equals(group, GroupName, StringComparison.Ordinal);
                seenGroup |= inGroup;
                continue;
            }

            if (!inGroup)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            // The spec allows space on either side of '=' and requires it to be ignored. Trailing
            // space is dropped too: a value whose whitespace matters has to escape it as "\s".
            var rawKey = line[..separator].TrimEnd();
            var value = line[(separator + 1)..].Trim();
            var (key, locale) = SplitKey(rawKey);

            // Duplicate keys are invalid. Keeping the first occurrence makes the outcome
            // deterministic instead of depending on how far the file was read.
            if (!seenKeys.Add(rawKey))
            {
                continue;
            }

            switch (key)
            {
                case "Name" when locale is null:
                    name = Unescape(value);
                    break;
                case "Name":
                    localisedNames[locale] = Unescape(value);
                    break;
                case "Exec" when locale is null:
                    exec = Unescape(value);
                    break;
                case "Icon" when locale is null:
                    icon = Unescape(value);
                    break;
                case "TryExec" when locale is null:
                    tryExec = Unescape(value);
                    break;
                case "MimeType" when locale is null:
                    mimeTypes = SplitList(value);
                    break;
                case "NoDisplay" when locale is null:
                    noDisplay = ParseBoolean(value);
                    break;
                case "Hidden" when locale is null:
                    hidden = ParseBoolean(value);
                    break;
                default:
                    break;
            }
        }

        return seenGroup
            ? new DesktopEntry
            {
                Name = name,
                LocalisedNames = localisedNames,
                Exec = exec,
                Icon = icon,
                TryExec = tryExec,
                MimeTypes = mimeTypes,
                NoDisplay = noDisplay,
                Hidden = hidden,
            }
            : null;
    }

    /// <summary>
    /// The desktop file id of <paramref name="filePath"/> relative to the <c>applications</c>
    /// directory it was found in: the file name, with sub-directories folded in using '-'
    /// (<c>applications/kde4/konqbrowser.desktop</c> becomes <c>kde4-konqbrowser.desktop</c>),
    /// exactly as the spec defines the id that <c>xdg-settings</c> expects.
    /// </summary>
    internal static string DesktopFileId(string applicationsDirectory, string filePath)
    {
        ArgumentNullException.ThrowIfNull(applicationsDirectory);
        ArgumentNullException.ThrowIfNull(filePath);

        var root = applicationsDirectory.TrimEnd('/', '\\');
        // Accept either separator: the path may have come from the host's file APIs during a
        // directory walk, while the XDG root itself is always POSIX.
        var normalisedPath = filePath.Replace('\\', PosixPath.Separator);
        var normalisedRoot = root.Replace('\\', PosixPath.Separator).TrimEnd(PosixPath.Separator);
        var relative = normalisedPath.StartsWith(normalisedRoot + PosixPath.Separator, StringComparison.Ordinal)
            ? normalisedPath[(normalisedRoot.Length + 1)..]
            : normalisedPath[(normalisedPath.LastIndexOf(PosixPath.Separator) + 1)..];

        return relative.Replace(PosixPath.Separator, '-');
    }

    /// <summary>Splits <c>Name[ja_JP]</c> into its key and locale; locale is <c>null</c> when absent.</summary>
    internal static (string Key, string? Locale) SplitKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var open = key.IndexOf('[');
        if (open > 0 && key.EndsWith(']') && key.Length - open > 2)
        {
            return (key[..open].TrimEnd(), key[(open + 1)..^1]);
        }

        return (key, null);
    }

    /// <summary>
    /// The first real argument of an <c>Exec</c> value: quotes resolved, field codes dropped, and
    /// an <c>env VAR=value</c> prefix skipped (common on entries that force a display backend).
    /// </summary>
    internal static string? ParseExecutable(string? exec)
    {
        if (string.IsNullOrWhiteSpace(exec))
        {
            return null;
        }

        var skippingEnvironmentPrefix = false;
        foreach (var token in TokenizeExec(exec))
        {
            var candidate = RemoveFieldCodes(token).Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            if (string.Equals(candidate, "env", StringComparison.Ordinal))
            {
                // "env" runs the *next* non-assignment token, so it is not the browser binary.
                skippingEnvironmentPrefix = true;
                continue;
            }

            // VAR=value assignments only appear as arguments to env; anything else is the binary.
            if (skippingEnvironmentPrefix && candidate.Contains('=') && !candidate.Contains('/'))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Splits an <c>Exec</c> value into arguments the way the spec requires: double quotes group
    /// an argument, and inside quotes a backslash escapes <c>"</c>, <c>\</c>, <c>$</c> and <c>`</c>.
    /// </summary>
    internal static IReadOnlyList<string> TokenizeExec(string exec)
    {
        ArgumentNullException.ThrowIfNull(exec);

        var tokens = new List<string>();
        var current = new StringBuilder();
        var started = false;
        var inQuotes = false;

        for (var i = 0; i < exec.Length; i++)
        {
            var c = exec[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                started = true;
                continue;
            }

            if (inQuotes && c == '\\' && i + 1 < exec.Length && exec[i + 1] is '"' or '\\' or '$' or '`')
            {
                current.Append(exec[i + 1]);
                i++;
                started = true;
                continue;
            }

            if (!inQuotes && (c == ' ' || c == '\t'))
            {
                if (started)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                continue;
            }

            current.Append(c);
            started = true;
        }

        if (started)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Removes <c>%u</c>, <c>%U</c>, <c>%f</c>, <c>%F</c> and the other field codes, turning the
    /// literal <c>%%</c> back into a single '%'.
    /// </summary>
    internal static string RemoveFieldCodes(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!token.Contains('%'))
        {
            return token;
        }

        var result = new StringBuilder(token.Length);
        for (var i = 0; i < token.Length; i++)
        {
            if (token[i] != '%' || i + 1 >= token.Length)
            {
                result.Append(token[i]);
                continue;
            }

            var next = token[i + 1];
            if (next == '%')
            {
                result.Append('%');
                i++;
                continue;
            }

            if (FieldCodeLetters.Contains(next, StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            result.Append(token[i]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Splits a semicolon-separated desktop-entry list, honouring the <c>\;</c> escape and
    /// discarding the empty element the spec's trailing semicolon produces.
    /// </summary>
    internal static IReadOnlyList<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var items = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] == ';')
            {
                current.Append(';');
                i++;
                continue;
            }

            if (value[i] == ';')
            {
                Flush(items, current);
                continue;
            }

            current.Append(value[i]);
        }

        Flush(items, current);
        return items;

        static void Flush(List<string> into, StringBuilder buffer)
        {
            var item = Unescape(buffer.ToString()).Trim();
            buffer.Clear();
            if (item.Length > 0)
            {
                into.Add(item);
            }
        }
    }

    /// <summary>Only the literal strings <c>true</c> and <c>false</c> are valid; anything else is false.</summary>
    internal static bool ParseBoolean(string? value)
        => string.Equals(value?.Trim(), "true", StringComparison.Ordinal);

    /// <summary>Resolves the escape sequences the spec defines for string values.</summary>
    internal static string Unescape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Contains('\\'))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            var next = value[i + 1];
            var replacement = next switch
            {
                's' => " ",
                'n' => "\n",
                't' => "\t",
                'r' => "\r",
                '\\' => "\\",
                ';' => ";",
                _ => null,
            };

            if (replacement is null)
            {
                result.Append(value[i]);
                continue;
            }

            result.Append(replacement);
            i++;
        }

        return result.ToString();
    }
}
