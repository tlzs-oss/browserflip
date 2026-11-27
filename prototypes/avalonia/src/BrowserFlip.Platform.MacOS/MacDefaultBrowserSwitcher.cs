using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using BrowserFlip.Core.Detection;
using BrowserFlip.Platform.MacOS.Interop;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.MacOS;

/// <summary>
/// Asks macOS to make a browser the default, via the public API
/// <c>-[NSWorkspace setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:]</c>
/// (macOS 12+). macOS then shows its own consent alert; the user confirms, exactly as Windows
/// requires confirmation in the Settings app. BrowserFlip never writes the association itself
/// and uses no private API.
/// <para>
/// <b>One request, one prompt.</b> Apple documents the method as setting "the default app to
/// open files of the type specified by the <c>urlScheme</c>", so on paper it is per-scheme.
/// In practice, measured on macOS 27.0 arm64 at 11:49:12 UTC, a single <c>https</c> request
/// updated all four handler entries at once - http, https, public.html and the rest - because
/// the system treats "default web browser" as one decision. Issuing http and https as two
/// requests therefore raised two <c>CoreServicesUIAgent</c> alerts side by side for a single
/// user action. This adapter requests the scheme that is not already satisfied, re-reads what
/// the OS actually did, and only asks again for a scheme the OS did not cover. On this Mac that
/// is exactly one prompt; where an OS really does need one consent per scheme the second prompt
/// still appears, and <see cref="SwitchOutcome.Applied"/> is withheld until both schemes read
/// back correctly.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacDefaultBrowserSwitcher : IDefaultBrowserSwitcher
{
    /// <summary>
    /// https first: it is the scheme the system's own "default web browser" decision is keyed
    /// on, and the one measured to carry http with it.
    /// </summary>
    private static readonly string[] WebSchemes = ["https", "http"];

    private readonly IMacAssociationReader _launchServices;

    public MacDefaultBrowserSwitcher() : this(new MacLaunchServices())
    {
    }

    internal MacDefaultBrowserSwitcher(IMacAssociationReader launchServices) => _launchServices = launchServices;

    /// <summary>Issues one native request and reports the OS's answer through the callback.</summary>
    internal delegate void ConsentRequest(string bundlePath, string scheme, Action<string?> onCompleted);

    /// <summary>
    /// The native call. Replaceable so the orchestration above can be tested: no test may ever
    /// raise a real <c>CoreServicesUIAgent</c> alert or change a developer's own default browser.
    /// </summary>
    internal ConsentRequest Request { get; set; } = SendToOs;

    /// <summary>How many native requests the last <see cref="RequestSwitchAsync"/> issued.</summary>
    internal int LastPromptCount { get; private set; }

    /// <summary>
    /// How long to watch for the change before reporting that it is still pending.
    /// <para>
    /// Deliberately short, and deliberately NOT a deadline on the OS. The consent alert is owned
    /// by <c>CoreServicesUIAgent</c>: it stays up until the user answers, it outlives this
    /// process, and the change lands whenever that happens - measured at 44 s (Firefox) and
    /// 474 s (Chrome) after the request on this Mac. Waiting that long inside a UI action is
    /// not an option, so the wait ends early and says "still pending", which is true, instead of
    /// "failed", which is not.
    /// </para>
    /// <para>
    /// It is a budget, not a guarantee: a backgrounded app is subject to App Nap and timer
    /// coalescing, and a measured run saw one 500 ms poll deferred for minutes. That costs
    /// nothing but latency, because the outcome is read back from the OS either way.
    /// </para>
    /// </summary>
    internal TimeSpan WaitBudget { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>How often the live association is re-read while waiting.</summary>
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public SwitchMechanism Mechanism
        => IsSupportedByOs ? SwitchMechanism.SystemConsentPrompt : SwitchMechanism.Unsupported;

    /// <summary>
    /// True when this macOS version exposes the public setter. Probed with
    /// <c>respondsToSelector:</c> rather than a hard-coded version check.
    /// </summary>
    internal static bool IsSupportedByOs
    {
        get
        {
            if (!MacLaunchServices.IsAvailable)
            {
                return false;
            }

            var workspace = ObjC.SharedWorkspace();
            var selector = ObjC.Sel("setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:");
            return ObjC.RespondsTo(workspace, selector);
        }
    }

    public async Task<SwitchResult> RequestSwitchAsync(BrowserInfo browser, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);

        if (!IsSupportedByOs)
        {
            return new SwitchResult(SwitchOutcome.Unsupported, "macOS 12 or later is required.");
        }

        if (string.IsNullOrWhiteSpace(browser.ExecutablePath) || !Directory.Exists(browser.ExecutablePath))
        {
            return new SwitchResult(SwitchOutcome.BrowserUnavailable, browser.ExecutablePath);
        }

        LastPromptCount = 0;

        // Every decision below is taken from a fresh read of the live association, so neither a
        // false success nor a false failure can be reported.
        var outstanding = SchemesNotYetHandledBy(browser);
        if (outstanding.Count == 0)
        {
            return new SwitchResult(SwitchOutcome.Applied, "already the default for http and https", ObservedDefault());
        }

        var prompts = 0;
        foreach (var scheme in outstanding)
        {
            // Re-checked each time round: the first request normally carries the other scheme
            // with it, and asking again for a scheme the OS has already set would raise a
            // second consent alert for nothing. This is the two-dialog defect, fixed.
            if (HandlerMatches(scheme, browser))
            {
                continue;
            }

            var signal = new ConsentSignal();
            try
            {
                Request(browser.ExecutablePath, scheme, signal.Complete);
                LastPromptCount = ++prompts;
            }
#pragma warning disable CA1031 // Any native failure must become a user-visible message, not a crash.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return new SwitchResult(SwitchOutcome.Failed, ex.Message, ObservedDefault());
            }

            var result = await AwaitOutcomeAsync(browser, scheme, signal, prompts, cancellationToken).ConfigureAwait(true);
            if (result is not null)
            {
                return result;
            }
        }

        // Only now, with every scheme read back from the OS, is "applied" a statement of fact.
        var stillOutstanding = SchemesNotYetHandledBy(browser);
        if (stillOutstanding.Count > 0)
        {
            return new SwitchResult(
                SwitchOutcome.TimedOutPending,
                FormattableString.Invariant($"still not the handler for: {string.Join(", ", stillOutstanding)}"),
                ObservedDefault());
        }

        return new SwitchResult(
            SwitchOutcome.Applied,
            FormattableString.Invariant($"{prompts} OS consent prompt(s) raised for this change"),
            ObservedDefault());
    }

    /// <summary>
    /// Watches one scheme until it lands, the OS answers, or the budget runs out. Returns
    /// <c>null</c> when the scheme is now handled and the caller should carry on; otherwise the
    /// result to report.
    /// </summary>
    private async Task<SwitchResult?> AwaitOutcomeAsync(
        BrowserInfo browser, string scheme, ConsentSignal signal, int prompts, CancellationToken cancellationToken)
    {
        // A caller that asked not to wait gets the honest intermediate state: the request is in,
        // the OS is asking, and nothing has been decided.
        if (WaitBudget <= TimeSpan.Zero)
        {
            return new SwitchResult(
                SwitchOutcome.AwaitingConsent,
                FormattableString.Invariant($"requested {scheme}; the macOS consent alert is open"),
                ObservedDefault());
        }

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < WaitBudget)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(true);

            if (HandlerMatches(scheme, browser))
            {
                return null;
            }

            if (!signal.Completed)
            {
                continue;
            }

            // Apple: "If a change requires user consent, the system asks the for consent
            // asynchronously before invoking the completion handler." The handler firing
            // therefore means the user has answered - but the association is still read back
            // before anything is reported, because the handler says what the OS decided, not
            // what the OS now holds.
            if (HandlerMatches(scheme, browser))
            {
                return null;
            }

            return signal.Error is not null
                ? new SwitchResult(SwitchOutcome.Declined, signal.Error, ObservedDefault())
                : new SwitchResult(
                    SwitchOutcome.TimedOutPending,
                    FormattableString.Invariant(
                        $"macOS reported success for {scheme} but the handler has not changed yet"),
                    ObservedDefault());
        }

        // One last read, so a change that landed between polls is not mis-reported.
        if (HandlerMatches(scheme, browser))
        {
            return null;
        }

        return new SwitchResult(
            SwitchOutcome.TimedOutPending,
            string.Create(
                CultureInfo.InvariantCulture,
                $"no answer to the macOS consent alert within {WaitBudget.TotalSeconds:F0}s ({prompts} prompt(s) raised); the alert outlives this process and the change may still apply"),
            ObservedDefault());
    }

    /// <summary>Which of http / https the OS does not yet route to this browser.</summary>
    private List<string> SchemesNotYetHandledBy(BrowserInfo browser)
        => [.. WebSchemes.Where(scheme => !HandlerMatches(scheme, browser))];

    private bool HandlerMatches(string scheme, BrowserInfo browser)
    {
        var current = _launchServices.GetHandlerForScheme(scheme);
        return current is not null && Matches(current, browser);
    }

    /// <summary>What <c>NSWorkspace.urlForApplication(toOpen:)</c> answers right now, for the record.</summary>
    private string ObservedDefault()
    {
        var current = _launchServices.GetDefaultWebBrowser();
        return current is null
            ? "none"
            : FormattableString.Invariant($"{current.BundleIdentifier ?? "?"} ({current.BundlePath})");
    }

    private static void SendToOs(string bundlePath, string scheme, Action<string?> onCompleted)
    {
        var workspace = ObjC.SharedWorkspace();
        var appUrl = ObjC.FileUrl(bundlePath);
        var selector = ObjC.Sel("setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:");

        // A nil completion handler is permitted, and is what CreateErrorHandler returns when the
        // block runtime symbol cannot be resolved; the association is read back either way.
        var handler = ObjCBlock.CreateErrorHandler(onCompleted);
        ObjC.Send(workspace, selector, appUrl, ObjC.NSString(scheme), handler);
    }

    internal static bool Matches(MacApp current, BrowserInfo browser)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(browser);
        return string.Equals(current.BundleIdentifier, browser.HttpsProgId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(current.BundlePath.TrimEnd('/'), browser.ExecutablePath?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The answer macOS delivers to the completion handler, read from whichever thread AppKit
    /// chooses to call on.
    /// </summary>
    private sealed class ConsentSignal
    {
        private int _completed;
        private string? _error;

        internal bool Completed => Volatile.Read(ref _completed) != 0;

        internal string? Error => Volatile.Read(ref _error);

        internal void Complete(string? error)
        {
            Volatile.Write(ref _error, error);
            Volatile.Write(ref _completed, 1);
        }
    }
}
