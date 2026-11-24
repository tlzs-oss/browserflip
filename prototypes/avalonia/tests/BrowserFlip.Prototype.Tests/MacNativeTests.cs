using System.Runtime.CompilerServices;
using BrowserFlip.Platform.MacOS;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> reported as SKIPPED instead of failing when the host is not macOS,
/// so a Windows or Linux run never executes AppKit calls.
/// </summary>
public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "macOS-only: exercises real AppKit / LaunchServices APIs (UNSUPPORTED on this OS).";
        }
    }
}

/// <summary>The <see cref="TheoryAttribute"/> counterpart of <see cref="MacFactAttribute"/>.</summary>
public sealed class MacTheoryAttribute : TheoryAttribute
{
    public MacTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "macOS-only: exercises the macOS adapter (UNSUPPORTED on this OS).";
        }
    }
}

/// <summary>
/// Smoke tests against the real macOS APIs. They are read-only: nothing here changes the default
/// browser, because a real system change must be triggered by the Owner.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public class MacNativeTests
{
    [MacFact]
    public void Launch_services_is_reachable()
        => Assert.True(MacLaunchServices.IsAvailable, "AppKit could not be loaded.");

    [MacFact]
    public void At_least_one_web_browser_is_registered()
    {
        var browsers = new MacBrowserCatalog().GetBrowsers();
        Assert.NotEmpty(browsers);
        // Safari ships with macOS, so some browser must always be present and well formed.
        Assert.All(browsers, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(b.Id));
            Assert.EndsWith(".app", b.ExecutablePath!.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        });
    }

    [MacFact]
    public void The_default_handler_for_https_is_readable()
    {
        var association = new MacAssociationQuery().QueryProtocol("https");
        Assert.NotNull(association);
        Assert.False(string.IsNullOrWhiteSpace(association.ProgId));
        Assert.True(Directory.Exists(association.ExecutablePath), association.ExecutablePath);
    }

    [MacFact]
    public void An_unknown_scheme_returns_null_rather_than_throwing()
        => Assert.Null(new MacAssociationQuery().QueryProtocol("browserflip-nonexistent-scheme"));

    [MacFact]
    public void Icons_are_produced_as_decodable_png()
    {
        var browser = new MacBrowserCatalog().GetBrowsers()[0];
        var png = new MacAppIconProvider().GetIconPng(browser.IconPath);
        Assert.NotNull(png);
        // PNG magic number.
        Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47], [png[0], png[1], png[2], png[3]]);
    }

    [MacFact]
    public void A_missing_path_yields_no_icon_instead_of_throwing()
        => Assert.Null(new MacAppIconProvider().GetIconPng(null));

    [MacFact]
    public void The_public_switch_api_is_present_on_this_macos()
    {
        // Probes respondsToSelector: only. It does NOT change anything.
        Assert.True(MacDefaultBrowserSwitcher.IsSupportedByOs,
            "NSWorkspace setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler: is missing (macOS 12+ required).");
        Assert.Equal(BrowserFlip.UI.Platform.SwitchMechanism.SystemConsentPrompt, new MacDefaultBrowserSwitcher().Mechanism);
    }

    [MacFact]
    public async Task A_missing_bundle_is_refused_without_touching_the_system()
    {
        var ghost = Browsers.Mac("com.example.ghost", "Ghost", "/Applications/Ghost-does-not-exist.app");
        var result = await new MacDefaultBrowserSwitcher().RequestSwitchAsync(ghost, TestContext.Current.CancellationToken);
        Assert.Equal(BrowserFlip.UI.Platform.SwitchOutcome.BrowserUnavailable, result.Outcome);
    }
}
