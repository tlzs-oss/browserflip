using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using static BrowserFlip.Core.Tests.RegistryFixtures;

namespace BrowserFlip.Core.Tests;

public class DefaultBrowserTests
{
    private static readonly BrowserInfo Chrome = Browser("Google Chrome", "ChromeHTML", "ChromeHTML", @"C:\Chrome\chrome.exe");
    private static readonly BrowserInfo Edge = Browser("Microsoft Edge", "MSEdgeHTM", "MSEdgeHTM", @"C:\Edge\msedge.exe");
    private static readonly BrowserInfo Split = Browser("Split", "SplitHttp", "SplitHttps", @"C:\Split\split.exe");
    private static readonly IReadOnlyList<BrowserInfo> All = [Chrome, Edge, Split];

    private static DefaultBrowserState State(string? http, string? https, string? httpExe = null)
        => new DefaultBrowserService(new FakeAssociations().With("http", http, httpExe).With("https", https)).GetState(All);

    [Fact]
    public void Consistent_when_both_protocols_map_to_same_browser()
    {
        var s = State("ChromeHTML", "ChromeHTML");
        Assert.Equal(DefaultStatus.Consistent, s.Status);
        Assert.Same(Chrome, s.HttpBrowser);
        Assert.Equal(BrowserDefaultRole.Both, s.RoleOf(Chrome));
        Assert.Equal(BrowserDefaultRole.NotDefault, s.RoleOf(Edge));
    }

    [Fact]
    public void Consistent_for_browser_with_distinct_progids_per_protocol()
        => Assert.Equal(DefaultStatus.Consistent, State("SplitHttp", "SplitHttps").Status);

    [Fact]
    public void Mismatch_when_protocols_map_to_different_browsers()
    {
        var s = State("ChromeHTML", "MSEdgeHTM");
        Assert.Equal(DefaultStatus.Mismatch, s.Status);
        Assert.Equal(BrowserDefaultRole.HttpOnly, s.RoleOf(Chrome));
        Assert.Equal(BrowserDefaultRole.HttpsOnly, s.RoleOf(Edge));
    }

    [Fact]
    public void Mismatch_when_only_one_protocol_is_set()
        => Assert.Equal(DefaultStatus.Mismatch, State("ChromeHTML", null).Status);

    [Fact]
    public void Mismatch_when_one_side_is_unknown_app()
        => Assert.Equal(DefaultStatus.Mismatch, State("ChromeHTML", "SomeOtherApp").Status);

    [Fact]
    public void Mismatch_when_two_different_unknown_apps()
        => Assert.Equal(DefaultStatus.Mismatch, State("AppA", "AppB").Status);

    [Fact]
    public void Unrecognized_when_same_unknown_handler()
    {
        var s = State("UnknownURL", "UnknownURL");
        Assert.Equal(DefaultStatus.Unrecognized, s.Status);
        Assert.Null(s.HttpBrowser);
    }

    [Fact]
    public void None_when_nothing_is_set()
        => Assert.Equal(DefaultStatus.None, State(null, null).Status);

    [Fact]
    public void Progid_matching_is_case_insensitive()
        => Assert.Same(Chrome, State("chromehtml", "CHROMEHTML").HttpsBrowser);

    [Fact]
    public void Falls_back_to_executable_path_match()
    {
        var s = State("ChromeHTML.RENAMED", "ChromeHTML", httpExe: @"C:\Chrome\chrome.exe");
        Assert.Same(Chrome, s.HttpBrowser);
        Assert.Equal(DefaultStatus.Consistent, s.Status);
    }

    [Fact]
    public void Invalid_executable_path_does_not_throw()
    {
        var s = State("Nope", "Nope", httpExe: "C:\\bad\0path.exe");
        Assert.Null(s.HttpBrowser);
    }

    [Fact]
    public void SameAssociationsAs_compares_progids()
    {
        Assert.True(State("ChromeHTML", "ChromeHTML").SameAssociationsAs(State("chromehtml", "ChromeHTML")));
        Assert.False(State("ChromeHTML", "ChromeHTML").SameAssociationsAs(State("ChromeHTML", "MSEdgeHTM")));
        Assert.True(DefaultBrowserState.Empty.SameAssociationsAs(State(null, null)));
    }

    [Fact]
    public void Empty_state_is_none()
        => Assert.Equal(DefaultStatus.None, DefaultBrowserState.Empty.Status);

    [Fact]
    public void Service_rejects_null_list()
        => Assert.Throws<ArgumentNullException>(() => new DefaultBrowserService(new FakeAssociations()).GetState(null!));

    [Fact]
    public void Real_association_query_returns_non_empty_progid_or_null()
    {
        // Smoke test against the real shell API: must never throw, regardless of machine configuration.
        var info = new ShellAssociationQuery().QueryProtocol("https");
        Assert.True(info is null || !string.IsNullOrWhiteSpace(info.ProgId));
        if (info is not null)
        {
            // Regression: the executable must be resolvable (needs the "open" verb for URL protocols).
            Assert.False(string.IsNullOrWhiteSpace(info.ExecutablePath));
            Assert.True(File.Exists(info.ExecutablePath), info.ExecutablePath);
        }

        Assert.Null(new ShellAssociationQuery().QueryProtocol("browserflip-nonexistent-protocol"));
    }
}

public class ChangeTrackerTests
{
    private static readonly BrowserInfo Chrome = Browser("Google Chrome", "ChromeHTML", "ChromeHTML");
    private static readonly BrowserInfo Edge = Browser("Microsoft Edge", "MSEdgeHTM", "MSEdgeHTM");
    private static readonly IReadOnlyList<BrowserInfo> All = [Chrome, Edge];

    private static DefaultBrowserState State(string? http, string? https)
        => new DefaultBrowserService(new FakeAssociations().With("http", http).With("https", https)).GetState(All);

    private static readonly DefaultBrowserState EdgeDefault = State("MSEdgeHTM", "MSEdgeHTM");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Applied_when_target_owns_both(bool returned)
        => Assert.Equal(ChangeOutcome.AppliedToTarget, new ChangeTracker(Chrome, EdgeDefault).Evaluate(State("ChromeHTML", "ChromeHTML"), returned));

    [Fact]
    public void Partial_is_pending_until_user_returns()
    {
        var t = new ChangeTracker(Chrome, EdgeDefault);
        var partial = State("ChromeHTML", "MSEdgeHTM");
        Assert.Equal(ChangeOutcome.Pending, t.Evaluate(partial, false));
        Assert.Equal(ChangeOutcome.PartiallyApplied, t.Evaluate(partial, true));
    }

    [Fact]
    public void Unchanged_is_pending_then_no_change()
    {
        var t = new ChangeTracker(Chrome, EdgeDefault);
        Assert.Equal(ChangeOutcome.Pending, t.Evaluate(EdgeDefault, false));
        Assert.Equal(ChangeOutcome.NoChange, t.Evaluate(EdgeDefault, true));
    }

    [Fact]
    public void Already_partial_target_unchanged_reports_no_change()
    {
        var before = State("ChromeHTML", "MSEdgeHTM");
        var t = new ChangeTracker(Chrome, before);
        Assert.Equal(ChangeOutcome.Pending, t.Evaluate(before, false));
        Assert.Equal(ChangeOutcome.NoChange, t.Evaluate(before, true));
        Assert.Equal(ChangeOutcome.AppliedToTarget, t.Evaluate(State("ChromeHTML", "ChromeHTML"), false));
    }

    [Fact]
    public void Changed_to_other_app()
    {
        var t = new ChangeTracker(Chrome, EdgeDefault);
        var other = State("OtherURL", "OtherURL");
        Assert.Equal(ChangeOutcome.Pending, t.Evaluate(other, false));
        Assert.Equal(ChangeOutcome.ChangedToOther, t.Evaluate(other, true));
    }

    [Fact]
    public void Constructor_validates_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ChangeTracker(null!, EdgeDefault));
        Assert.Throws<ArgumentNullException>(() => new ChangeTracker(Chrome, null!));
        Assert.Throws<ArgumentNullException>(() => new ChangeTracker(Chrome, EdgeDefault).Evaluate(null!, true));
    }
}
