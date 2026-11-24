using System.Diagnostics;

namespace BrowserFlip.Platform.Linux;

/// <summary>What a command-line tool printed and how it ended.</summary>
/// <param name="ExitCode">Process exit code; -1 when the process never ran or was killed.</param>
/// <param name="StandardOutput">Everything the tool wrote to stdout.</param>
/// <param name="StandardError">Everything the tool wrote to stderr.</param>
/// <param name="TimedOut">True when the tool was still running when the timeout expired.</param>
/// <param name="Error">Why the tool could not be run at all; <c>null</c> when it ran.</param>
internal sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    string? Error = null)
{
    /// <summary>
    /// True when the tool could not be launched at all, i.e. it is not installed. Distinguished
    /// from an ordinary failure so the caller can report "unsupported" rather than "failed".
    /// </summary>
    internal bool ToolMissing { get; init; }

    /// <summary>True only when the tool ran to completion and reported success.</summary>
    internal bool Succeeded => !TimedOut && Error is null && ExitCode == 0;

    /// <summary>The most useful message to show the user when something went wrong.</summary>
    internal string? FailureMessage
    {
        get
        {
            if (Succeeded)
            {
                return null;
            }

            if (Error is not null)
            {
                return Error;
            }

            if (TimedOut)
            {
                return "The command did not finish in time.";
            }

            var stderr = StandardError.Trim();
            return stderr.Length > 0 ? stderr : $"The command exited with code {ExitCode}.";
        }
    }
}

/// <summary>
/// Runs a command-line tool. An interface rather than a direct <see cref="Process"/> call so the
/// adapter's logic is testable without spawning anything.
/// </summary>
internal interface ICommandRunner
{
    /// <summary>Runs <paramref name="fileName"/> and captures its output. Must never throw.</summary>
    CommandResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout);
}

/// <summary>
/// The real runner: no shell, no window, both streams captured, a hard timeout, and no exception
/// ever propagated. A missing tool or a hung tool must become a message, not a crash.
/// </summary>
internal sealed class ProcessCommandRunner : ICommandRunner
{
    public CommandResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(fileName)
            {
                // No shell: the arguments are passed as an argv list, so nothing a desktop file
                // or an environment variable contains can ever be interpreted as shell syntax.
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return new CommandResult(-1, string.Empty, string.Empty, Error: $"{fileName} could not be started.");
            }

            // Both pipes must be drained while the process runs, or a chatty tool deadlocks
            // against a full pipe buffer while we wait for it to exit.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                Kill(process);
                return new CommandResult(-1, Text(stdout), Text(stderr), TimedOut: true);
            }

            // The pipes are closed by now; give the readers a moment to publish what they hold.
            Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(1));
            return new CommandResult(process.ExitCode, Text(stdout), Text(stderr));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // The tool is not installed (or not executable). That is a missing capability, not
            // a failure of the operation, so it is reported separately.
            return new CommandResult(-1, string.Empty, string.Empty, Error: ex.Message) { ToolMissing = true };
        }
#pragma warning disable CA1031 // Any other launch failure must become a user-visible message, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return new CommandResult(-1, string.Empty, string.Empty, Error: ex.Message);
        }
    }

    private static string Text(Task<string> read)
        => read.IsCompletedSuccessfully ? read.Result : string.Empty;

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
#pragma warning disable CA1031 // The process may have exited between the check and the kill.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Nothing useful can be done; the result already says it timed out.
        }
    }
}

/// <summary>Locating binaries the way a Unix shell would.</summary>
internal static class LinuxExecutables
{
    /// <summary>
    /// Resolves <paramref name="binary"/> to a path that exists: used as-is when it contains a
    /// path separator, otherwise looked up in each <c>PATH</c> entry. Returns <c>null</c> when it
    /// cannot be found, which is what makes a browser's health <c>ExecutableMissing</c>.
    /// </summary>
    /// <remarks>
    /// Existence only, not the execute bit: <c>File.GetUnixFileMode</c> is annotated unsupported
    /// on Windows, and taking that dependency would make this assembly platform-annotated and so
    /// untestable from a non-Linux host. A present-but-not-executable binary is vanishingly rare
    /// compared with the value of keeping this logic testable.
    /// </remarks>
    internal static string? Resolve(string? binary, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(binary))
        {
            return null;
        }

        var trimmed = binary.Trim();
        if (trimmed.Contains('/'))
        {
            return File.Exists(trimmed) ? trimmed : null;
        }

        var path = getEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, trimmed);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// Thin wrapper over the <c>xdg-utils</c> command-line tools: <c>xdg-settings</c> for the default
/// web browser and <c>xdg-mime</c> for an individual scheme handler.
/// </summary>
/// <remarks>
/// <para>
/// These tools are the only supported way to read and write the default browser across desktops:
/// they know about <c>mimeapps.list</c>, GNOME's GSettings keys and KDE's own configuration, and
/// writing those files directly would fight whichever desktop is running.
/// </para>
/// <para>
/// Reading is harmless. <b>Writing is not</b>: see <see cref="LinuxDefaultBrowserSwitcher"/>.
/// </para>
/// </remarks>
internal sealed class XdgSettings
{
    /// <summary>The settings tool, expected on <c>PATH</c>.</summary>
    internal const string SettingsTool = "xdg-settings";

    /// <summary>The MIME tool, used as a cross-check for individual schemes.</summary>
    internal const string MimeTool = "xdg-mime";

    /// <summary>The property <c>xdg-settings</c> uses for the default browser.</summary>
    internal const string DefaultWebBrowser = "default-web-browser";

    /// <summary>These tools are shell scripts that do a handful of config reads; 5 s is generous.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly ICommandRunner _runner;

    internal XdgSettings() : this(new ProcessCommandRunner())
    {
    }

    internal XdgSettings(ICommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <summary>True when <c>xdg-settings</c> is on <c>PATH</c> at all.</summary>
    internal static bool IsAvailable
        => LinuxExecutables.Resolve(SettingsTool, Environment.GetEnvironmentVariable) is not null;

    /// <summary>
    /// <c>xdg-settings get default-web-browser</c>: the desktop file id of the current default,
    /// or <c>null</c> when none is set or the tool failed.
    /// </summary>
    internal string? GetDefaultWebBrowser()
        => ParseSingleValue(_runner.Run(SettingsTool, ["get", DefaultWebBrowser], Timeout));

    /// <summary>
    /// <c>xdg-mime query default &lt;mimeType&gt;</c>: the handler for one MIME type, used for
    /// schemes <c>xdg-settings</c> has no property for.
    /// </summary>
    internal string? QueryMimeHandler(string mimeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        return ParseSingleValue(_runner.Run(MimeTool, ["query", "default", mimeType], Timeout));
    }

    /// <summary>
    /// <c>xdg-settings set default-web-browser &lt;id&gt;</c>.
    /// <para>
    /// This applies the change immediately and silently. Only
    /// <see cref="LinuxDefaultBrowserSwitcher"/> may call it, and only after the user has agreed.
    /// </para>
    /// </summary>
    internal CommandResult SetDefaultWebBrowser(string desktopFileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopFileId);
        return _runner.Run(SettingsTool, ["set", DefaultWebBrowser, desktopFileId], Timeout);
    }

    /// <summary>
    /// The single value these tools print, or <c>null</c> when they failed or printed nothing.
    /// <c>xdg-settings</c> also prints diagnostics on stdout on some distributions, so only the
    /// first non-empty line is taken and only when the exit code says the run succeeded.
    /// </summary>
    internal static string? ParseSingleValue(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            return null;
        }

        foreach (var rawLine in result.StandardOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length > 0)
            {
                return line;
            }
        }

        return null;
    }
}
