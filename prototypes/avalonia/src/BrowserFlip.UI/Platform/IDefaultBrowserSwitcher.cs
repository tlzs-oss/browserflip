using BrowserFlip.Core.Detection;

namespace BrowserFlip.UI.Platform;

/// <summary>How a platform lets the user change the default browser.</summary>
public enum SwitchMechanism
{
    /// <summary>The OS opens its own settings UI and the user confirms there (Windows).</summary>
    OpensSystemSettings,

    /// <summary>The OS shows a consent prompt owned by the OS, triggered by an API call (macOS).</summary>
    SystemConsentPrompt,

    /// <summary>A command-line tool applies the change directly (Linux / xdg-settings).</summary>
    CommandLineTool,

    /// <summary>No supported mechanism on this platform.</summary>
    Unsupported,
}

/// <summary>What happened when a switch was requested.</summary>
public enum SwitchOutcome
{
    /// <summary>The OS accepted the request. On macOS the user may still have declined the prompt.</summary>
    Requested,

    /// <summary>The OS confirmed the browser is now the default.</summary>
    Applied,

    /// <summary>The user declined the OS prompt, or the OS refused.</summary>
    Declined,

    /// <summary>The target browser is not usable (missing executable / bundle).</summary>
    BrowserUnavailable,

    /// <summary>Not supported on this platform or OS version.</summary>
    Unsupported,

    /// <summary>The attempt failed; see <see cref="SwitchResult.Error"/>.</summary>
    Failed,
}

/// <summary>Result of <see cref="IDefaultBrowserSwitcher.RequestSwitchAsync"/>.</summary>
public sealed record SwitchResult(SwitchOutcome Outcome, string? Error = null)
{
    public bool Accepted => Outcome is SwitchOutcome.Requested or SwitchOutcome.Applied;
}

/// <summary>
/// Requests that the OS make a browser the default. BrowserFlip never writes the association
/// itself on any platform: every supported mechanism keeps the user in control.
/// </summary>
public interface IDefaultBrowserSwitcher
{
    /// <summary>How this platform performs the change (for wording the UI honestly).</summary>
    SwitchMechanism Mechanism { get; }

    /// <summary>
    /// Asks the OS to make <paramref name="browser"/> the default for http and https.
    /// </summary>
    Task<SwitchResult> RequestSwitchAsync(BrowserInfo browser, CancellationToken cancellationToken = default);
}
