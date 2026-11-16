using System.Globalization;

namespace BrowserFlip.Core.Detection;

/// <summary>Parses command-line and icon-location strings stored in the registry.</summary>
public static class RegistryValueParser
{
    /// <summary>
    /// Extracts the executable path from a <c>shell\open\command</c> value such as
    /// <c>"C:\Program Files\App\app.exe" --flag "%1"</c> or <c>C:\Program Files\App\app.exe -url %1</c>.
    /// </summary>
    public static string? ParseExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var s = command.Trim();
        if (s[0] == '"')
        {
            var end = s.IndexOf('"', 1);
            var quoted = end < 0 ? s[1..] : s[1..end];
            return string.IsNullOrWhiteSpace(quoted) ? null : quoted.Trim();
        }

        // Unquoted: paths may contain spaces, so cut right after ".exe" when present.
        var exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0)
        {
            return s[..(exe + 4)];
        }

        var space = s.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? s : s[..space];
    }

    /// <summary>
    /// Parses a <c>DefaultIcon</c> value such as <c>"C:\app.exe",0</c> or <c>C:\app.exe,-101</c>.
    /// </summary>
    public static (string? Path, int Index) ParseIconLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, 0);
        }

        var s = value.Trim();
        var index = 0;
        var comma = s.LastIndexOf(',');
        if (comma > 0 && int.TryParse(s[(comma + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            index = parsed;
            s = s[..comma].Trim();
        }

        s = s.Trim('"').Trim();
        return (s.Length == 0 ? null : s, index);
    }

    /// <summary>Normalizes a registry path for comparison (case-insensitive, no leading <c>\</c>).</summary>
    public static string NormalizeKeyPath(string path)
        => path.Replace('/', '\\').Trim().TrimStart('\\').TrimEnd('\\');
}
