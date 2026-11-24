using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// The point of the unified architecture: the Core logic written for Windows produces correct
/// answers when it is fed macOS-shaped data (bundle identifiers in place of ProgIDs), with no
/// change to Core at all. If these fail, the "shared Core" claim is not true.
/// </summary>
public class CoreReuseTests
{
    private static readonly BrowserInfo Safari = Browsers.Mac("com.apple.Safari", "Safari");
    private static readonly BrowserInfo Firefox = Browsers.Mac("org.mozilla.firefox", "Firefox");
    private static readonly BrowserInfo Chrome = Browsers.Mac("com.google.Chrome", "Google Chrome");
    private static readonly IReadOnlyList<BrowserInfo> All = [Safari, Firefox, Chrome];

    private static DefaultBrowserState State(string? http, string? https)
        => new DefaultBrowserService(new FakeAssociations().With("http", http).With("https", https)).GetState(All);

    [Fact]
    public void Core_matches_a_macos_bundle_identifier_like_a_windows_progid()
    {
        var state = State("org.mozilla.firefox", "org.mozilla.firefox");
        Assert.Equal(DefaultStatus.Consistent, state.Status);
        Assert.Same(Firefox, state.HttpsBrowser);
        Assert.Equal(BrowserDefaultRole.Both, state.RoleOf(Firefox));
        Assert.Equal(BrowserDefaultRole.NotDefault, state.RoleOf(Safari));
    }

    [Fact]
    public void Core_reports_a_mismatch_when_http_and_https_differ_on_macos()
    {
        var state = State("com.apple.Safari", "org.mozilla.firefox");
        Assert.Equal(DefaultStatus.Mismatch, state.Status);
        Assert.Equal(BrowserDefaultRole.HttpOnly, state.RoleOf(Safari));
        Assert.Equal(BrowserDefaultRole.HttpsOnly, state.RoleOf(Firefox));
    }

    [Fact]
    public void Core_reports_unrecognized_for_a_bundle_that_is_not_in_the_catalogue()
    {
        var state = State("com.operasoftware.Opera", "com.operasoftware.Opera");
        Assert.Equal(DefaultStatus.Unrecognized, state.Status);
        Assert.Null(state.HttpsBrowser);
    }

    [Fact]
    public void Core_reports_none_when_macos_has_no_handler()
        => Assert.Equal(DefaultStatus.None, State(null, null).Status);

    [Fact]
    public void Bundle_identifier_matching_is_case_insensitive()
        => Assert.Same(Chrome, State("COM.GOOGLE.CHROME", "com.google.chrome").HttpsBrowser);

    [Fact]
    public void Core_change_tracker_detects_a_macos_switch()
    {
        var before = State("com.apple.Safari", "com.apple.Safari");
        var tracker = new ChangeTracker(Firefox, before);

        Assert.Equal(ChangeOutcome.Pending, tracker.Evaluate(before, userReturned: false));
        Assert.Equal(ChangeOutcome.NoChange, tracker.Evaluate(before, userReturned: true));

        var after = State("org.mozilla.firefox", "org.mozilla.firefox");
        Assert.Equal(ChangeOutcome.AppliedToTarget, tracker.Evaluate(after, userReturned: true));
    }

    [Fact]
    public void Core_change_tracker_detects_a_partial_macos_switch()
    {
        var before = State("com.apple.Safari", "com.apple.Safari");
        var tracker = new ChangeTracker(Firefox, before);
        var partial = State("org.mozilla.firefox", "com.apple.Safari");
        Assert.Equal(ChangeOutcome.PartiallyApplied, tracker.Evaluate(partial, userReturned: true));
    }

    [Fact]
    public void Core_change_tracker_detects_a_switch_to_a_third_browser()
    {
        var before = State("com.apple.Safari", "com.apple.Safari");
        var tracker = new ChangeTracker(Firefox, before);
        var other = State("com.google.Chrome", "com.google.Chrome");
        Assert.Equal(ChangeOutcome.ChangedToOther, tracker.Evaluate(other, userReturned: true));
    }
}
