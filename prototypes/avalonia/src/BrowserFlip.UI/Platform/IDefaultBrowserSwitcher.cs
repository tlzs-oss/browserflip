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

/// <summary>
/// What happened when a switch was requested.
/// <para>
/// "Not applied yet" and "not applied" are different facts and must never collapse into one
/// value. Measured on macOS 27: the consent alert belongs to <c>CoreServicesUIAgent</c>, not to
/// the requesting process; it stays on screen until the user answers it (observed for 7.5
/// minutes), it survives the requesting process exiting, and the change then lands - Firefox
/// 44 s and Chrome 474 s after the request. An implementation that stops waiting and calls that
/// a failure reports the opposite of what the OS is about to do.
/// </para>
/// </summary>
public enum SwitchOutcome
{
    /// <summary>
    /// The OS accepted the request and nothing further is known yet. Used by mechanisms that
    /// hand the user off elsewhere (Windows Settings).
    /// </summary>
    Requested,

    /// <summary>
    /// The OS accepted the request and is asking the user to confirm. The consent dialog is
    /// known to be open and unanswered; nothing has changed, and nothing has been refused.
    /// </summary>
    AwaitingConsent,

    /// <summary>
    /// Read back from the OS after the request: the browser IS the default now. Only ever set
    /// from a fresh query of the live association, never inferred from the call succeeding.
    /// </summary>
    Applied,

    /// <summary>The user declined the OS prompt, or the OS refused.</summary>
    Declined,

    /// <summary>
    /// The caller stopped waiting while the request was still outstanding. The state is
    /// UNKNOWN, not failed: the dialog may still be open and the change may still land.
    /// </summary>
    TimedOutPending,

    /// <summary>The target browser is not usable (missing executable / bundle).</summary>
    BrowserUnavailable,

    /// <summary>Not supported on this platform or OS version.</summary>
    Unsupported,

    /// <summary>The attempt failed; see <see cref="SwitchResult.Error"/>.</summary>
    Failed,
}

/// <summary>Result of <see cref="IDefaultBrowserSwitcher.RequestSwitchAsync"/>.</summary>
/// <param name="Outcome">What is known to have happened.</param>
/// <param name="Error">
/// Technical detail from the platform, in whatever language the platform produced it. It is a
/// diagnostic, never the user-facing sentence: the UI builds that from localised resources and
/// attaches this as a labelled detail.
/// </param>
/// <param name="ObservedDefault">
/// What the OS answered when the current default was last read back, so a report can show the
/// evidence behind <paramref name="Outcome"/> rather than only the verdict.
/// </param>
public sealed record SwitchResult(SwitchOutcome Outcome, string? Error = null, string? ObservedDefault = null)
{
    /// <summary>The request reached the OS and was not refused. It may or may not have applied.</summary>
    public bool Accepted => Outcome
        is SwitchOutcome.Requested or SwitchOutcome.AwaitingConsent
        or SwitchOutcome.Applied or SwitchOutcome.TimedOutPending;

    /// <summary>
    /// True while the real outcome is unknown and could still become <see cref="SwitchOutcome.Applied"/>.
    /// Callers must not report these as failure.
    /// </summary>
    public bool StillPending => Outcome
        is SwitchOutcome.Requested or SwitchOutcome.AwaitingConsent or SwitchOutcome.TimedOutPending;
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
