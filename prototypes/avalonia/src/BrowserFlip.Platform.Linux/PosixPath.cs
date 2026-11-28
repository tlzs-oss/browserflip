namespace BrowserFlip.Platform.Linux;

/// <summary>
/// Joins POSIX paths with <c>/</c> regardless of the host.
/// </summary>
/// <remarks>
/// The XDG specifications define absolute POSIX paths, so building them with
/// <see cref="Path.Combine(string, string)"/> is wrong: on a Windows host it produces
/// <c>/home/me\.local\share</c>. The adapter only ever runs on Linux, but its tests run
/// everywhere, and a path helper whose output depends on the machine that happens to be
/// executing it is a defect regardless of where it runs.
/// </remarks>
internal static class PosixPath
{
    internal const char Separator = '/';

    /// <summary>Joins segments with <c>/</c>, collapsing separators at the joins.</summary>
    internal static string Join(string first, params string[] rest)
    {
        ArgumentNullException.ThrowIfNull(rest);
        var result = first ?? string.Empty;
        foreach (var segment in rest)
        {
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            result = result.Length == 0
                ? segment
                : result.TrimEnd(Separator) + Separator + segment.TrimStart(Separator);
        }

        return result;
    }
}
