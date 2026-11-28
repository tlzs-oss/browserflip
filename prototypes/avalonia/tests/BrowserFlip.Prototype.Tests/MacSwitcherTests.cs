using System.Runtime.Versioning;
using BrowserFlip.Core.Detection;
using BrowserFlip.Platform.MacOS;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Prototype.Tests;

/// <summary>Scheme handlers the test controls, standing in for live LaunchServices.</summary>
internal sealed class FakeMacAssociations : IMacAssociationReader
{
    private readonly Dictionary<string, MacApp?> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Queried { get; } = [];

    /// <summary>
    /// Runs before each read, so a test can make the association change part-way through the
    /// polling loop without a second thread - the loop stays deterministic.
    /// </summary>
    public Action<int>? BeforeQuery { get; set; }

    public FakeMacAssociations Set(string scheme, MacApp? app)
    {
        _handlers[scheme] = app;
        return this;
    }

    public FakeMacAssociations SetAll(MacApp? app) => Set("http", app).Set("https", app);

    public MacApp? GetHandlerForScheme(string scheme)
    {
        Queried.Add(scheme);
        BeforeQuery?.Invoke(Queried.Count);
        return _handlers.TryGetValue(scheme, out var app) ? app : null;
    }

    public MacApp? GetDefaultWebBrowser() => _handlers.TryGetValue("https", out var app) ? app : null;
}

/// <summary>
/// The macOS switcher's own decisions: how many OS consent alerts a single user action raises,
/// and what outcome is reported for each thing the OS can do. The native call is replaced
/// throughout, so no test can raise a real <c>CoreServicesUIAgent</c> alert or change the
/// machine's default browser; the orchestration around it is the real production code.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacSwitcherTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "browserflip-tests-" + Guid.NewGuid().ToString("N"));

    private readonly MacApp _other = new("/Applications/Safari.app", "com.apple.Safari", "Safari");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A real directory, because the switcher refuses a bundle that is not on disk.</summary>
    private (BrowserInfo Browser, MacApp App) Bundle()
    {
        var path = Path.Combine(_root, "Test Browser.app");
        Directory.CreateDirectory(path);
        return (Browsers.Mac("org.example.testbrowser", "Test Browser", path),
                new MacApp(path, "org.example.testbrowser", "Test Browser"));
    }

    private static MacDefaultBrowserSwitcher Switcher(
        FakeMacAssociations associations, MacDefaultBrowserSwitcher.ConsentRequest request)
        => new(associations)
        {
            Request = request,
            // The polling loop is real; only its clock is shortened.
            WaitBudget = TimeSpan.FromMilliseconds(400),
            PollInterval = TimeSpan.FromMilliseconds(20),
        };

    [MacFact]
    public async Task One_user_action_raises_one_consent_prompt_when_the_os_updates_both_schemes()
    {
        // The measured macOS 27 behaviour: a single https request moved http, https and
        // public.html together at 11:49:12 UTC. Looping over both schemes therefore produced
        // two simultaneous CoreServicesUIAgent alerts for one click. One is correct.
        var (browser, app) = Bundle();
        var associations = new FakeMacAssociations().SetAll(_other);
        var asked = new List<string>();
        var switcher = Switcher(associations, (_, scheme, done) =>
        {
            asked.Add(scheme);
            associations.SetAll(app);
            done(null);
        });

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Applied, result.Outcome);
        Assert.Equal(["https"], asked);
        Assert.Equal(1, switcher.LastPromptCount);
    }

    [MacFact]
    public async Task A_second_prompt_is_raised_only_when_the_os_did_not_carry_the_other_scheme()
    {
        // If an OS really does need one consent per scheme, the change still has to be
        // completed - correctness first, and the extra prompt is the OS's, not an artefact.
        var (browser, app) = Bundle();
        var associations = new FakeMacAssociations().SetAll(_other);
        var asked = new List<string>();
        var switcher = Switcher(associations, (_, scheme, done) =>
        {
            asked.Add(scheme);
            associations.Set(scheme, app);
            done(null);
        });

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Applied, result.Outcome);
        Assert.Equal(["https", "http"], asked);
        Assert.Equal(2, switcher.LastPromptCount);
    }

    [MacFact]
    public async Task A_browser_that_is_already_the_default_raises_no_prompt_at_all()
    {
        var (browser, app) = Bundle();
        var associations = new FakeMacAssociations().SetAll(app);
        var asked = 0;
        var switcher = Switcher(associations, (_, _, done) => { asked++; done(null); });

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Applied, result.Outcome);
        Assert.Equal(0, asked);
        Assert.Equal(0, switcher.LastPromptCount);
    }

    [MacFact]
    public async Task An_unanswered_prompt_is_pending_and_is_never_reported_as_failure()
    {
        // The defect this exists for: the OS alert outlives the wait (44 s and 474 s measured),
        // and the old code called that "macOS did not report the change" and FAILed.
        var (browser, _) = Bundle();
        var associations = new FakeMacAssociations().SetAll(_other);
        var switcher = Switcher(associations, (_, _, _) => { /* nobody has answered the alert */ });

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.TimedOutPending, result.Outcome);
        Assert.True(result.StillPending);
        Assert.True(result.Accepted);
        Assert.NotEqual(SwitchOutcome.Failed, result.Outcome);
        Assert.NotEqual(SwitchOutcome.Declined, result.Outcome);
        // The evidence is what the OS answered, not what BrowserFlip assumed.
        Assert.NotNull(result.ObservedDefault);
        Assert.Contains("com.apple.Safari", result.ObservedDefault, StringComparison.Ordinal);
    }

    [MacFact]
    public async Task A_decline_reported_by_the_completion_handler_is_reported_as_declined()
    {
        var (browser, _) = Bundle();
        var associations = new FakeMacAssociations().SetAll(_other);
        var switcher = Switcher(associations, (_, _, done) => done("The operation couldn't be completed."));

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Declined, result.Outcome);
        Assert.False(result.StillPending);
        Assert.Equal("The operation couldn't be completed.", result.Error);
    }

    [MacFact]
    public async Task Applied_is_never_inferred_from_the_call_succeeding()
    {
        // A completion handler with no error says what the OS decided, not what it now holds.
        // Without a read-back that agrees, the honest answer is "unknown", never "applied".
        var (browser, _) = Bundle();
        var associations = new FakeMacAssociations().SetAll(_other);
        var switcher = Switcher(associations, (_, _, done) => done(null));

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.NotEqual(SwitchOutcome.Applied, result.Outcome);
        Assert.Equal(SwitchOutcome.TimedOutPending, result.Outcome);
    }

    [MacFact]
    public async Task A_change_that_lands_despite_an_error_is_read_back_as_applied()
    {
        // The live association outranks the completion handler in both directions.
        var (browser, app) = Bundle();
        var associations = new FakeMacAssociations().SetAll(_other);
        var switcher = Switcher(associations, (_, _, done) =>
        {
            associations.SetAll(app);
            done("some NSError the system produced anyway");
        });

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Applied, result.Outcome);
    }

    [MacFact]
    public async Task A_change_that_lands_several_polls_later_is_still_reported_as_applied()
    {
        // The user answered the alert a moment after the request, which is the normal case and
        // the one the old 20 s / "FAIL" path could not express.
        var (browser, app) = Bundle();
        var associations = new FakeMacAssociations().SetAll(_other);
        // Reads 1-2 decide what is outstanding, 3 re-checks before the request; the sixth read
        // is the third poll of the wait loop.
        associations.BeforeQuery = n =>
        {
            if (n == 6)
            {
                associations.SetAll(app);
            }
        };
        var switcher = Switcher(associations, (_, _, _) => { });

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Applied, result.Outcome);
        Assert.Equal(1, switcher.LastPromptCount);
    }

    [MacFact]
    public async Task A_caller_that_chooses_not_to_wait_is_told_the_os_is_still_asking()
    {
        var (browser, _) = Bundle();
        var associations = new FakeMacAssociations().SetAll(_other);
        var switcher = Switcher(associations, (_, _, _) => { });
        switcher.WaitBudget = TimeSpan.Zero;

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.AwaitingConsent, result.Outcome);
        Assert.True(result.StillPending);
        Assert.Equal(1, switcher.LastPromptCount);
    }

    [MacFact]
    public async Task A_native_failure_becomes_a_failed_result_rather_than_a_crash()
    {
        var (browser, _) = Bundle();
        var associations = new FakeMacAssociations().SetAll(_other);
        var switcher = Switcher(associations, (_, _, _) => throw new InvalidOperationException("objc_msgSend blew up"));

        var result = await switcher.RequestSwitchAsync(browser, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Failed, result.Outcome);
        Assert.Equal("objc_msgSend blew up", result.Error);
        Assert.False(result.Accepted);
    }

    [MacFact]
    public async Task A_missing_bundle_is_refused_before_any_request_reaches_the_os()
    {
        var associations = new FakeMacAssociations().SetAll(_other);
        var asked = 0;
        var switcher = Switcher(associations, (_, _, _) => asked++);
        var ghost = Browsers.Mac("com.example.ghost", "Ghost", Path.Combine(_root, "Nope.app"));

        var result = await switcher.RequestSwitchAsync(ghost, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.BrowserUnavailable, result.Outcome);
        Assert.Equal(0, asked);
    }
}
