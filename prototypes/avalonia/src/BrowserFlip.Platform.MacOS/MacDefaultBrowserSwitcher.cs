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
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacDefaultBrowserSwitcher : IDefaultBrowserSwitcher
{
    private static readonly string[] Schemes = ["http", "https"];

    private readonly MacLaunchServices _launchServices;

    public MacDefaultBrowserSwitcher() : this(new MacLaunchServices())
    {
    }

    internal MacDefaultBrowserSwitcher(MacLaunchServices launchServices) => _launchServices = launchServices;

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

        try
        {
            RequestFromOs(browser.ExecutablePath);
        }
#pragma warning disable CA1031 // Any native failure must become a user-visible message, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return new SwitchResult(SwitchOutcome.Failed, ex.Message);
        }

        // The OS prompt is modal and asynchronous. Poll the effective handler briefly so the
        // result reported to the user is what macOS actually did, not merely what we asked for.
        var applied = await WaitForAsync(browser, cancellationToken).ConfigureAwait(true);
        return applied
            ? new SwitchResult(SwitchOutcome.Applied)
            : new SwitchResult(SwitchOutcome.Requested, "macOS did not report the change (the prompt may still be open, or was declined).");
    }

    private static void RequestFromOs(string bundlePath)
    {
        var workspace = ObjC.SharedWorkspace();
        var appUrl = ObjC.FileUrl(bundlePath);
        var selector = ObjC.Sel("setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:");
        foreach (var scheme in Schemes)
        {
            // A nil completion handler is permitted; the outcome is read back below instead.
            ObjC.Send(workspace, selector, appUrl, ObjC.NSString(scheme), ObjC.Nil);
        }
    }

    /// <summary>
    /// Polls for up to ~20 s, because the user has to answer the OS alert first.
    /// <c>ConfigureAwait(true)</c> throughout: AppKit must be called from the thread that owns
    /// the run loop, and a thread-pool thread has no autorelease pool for the returned objects.
    /// </summary>
    private async Task<bool> WaitForAsync(BrowserInfo browser, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(true);
            var current = _launchServices.GetDefaultWebBrowser();
            if (current is not null && Matches(current, browser))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool Matches(MacApp current, BrowserInfo browser)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(browser);
        return string.Equals(current.BundleIdentifier, browser.HttpsProgId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(current.BundlePath.TrimEnd('/'), browser.ExecutablePath?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }
}
