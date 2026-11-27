using BrowserFlip.Core.Defaults;
using BrowserFlip.UI.Localization;
using BrowserFlip.UI.Platform;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// The shared view model, driven entirely by in-memory adapters: no OS call is made, so these
/// tests are identical on macOS, Windows and Linux.
/// </summary>
public class MainViewModelTests
{
    /// <summary>A platform whose OS confirms the change, so the view model must not ask again.</summary>
    private static readonly IPlatformCapabilities Caps = new FullCapabilities("test platform", osConfirms: true);

    private static (MainViewModel Model, FakeSwitcher Switcher, InMemorySettingsStore Store) Build(
        string? http = "com.apple.Safari", string? https = "com.apple.Safari")
    {
        var associations = new FakeAssociations().With("http", http).With("https", https);
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"))
            .Add(Browsers.Mac("com.google.Chrome", "Google Chrome"));
        var switcher = new FakeSwitcher(associations);
        var store = new InMemorySettingsStore();
        var model = new MainViewModel(
            catalog, _ => switcher, new FakeIcons(), store,
            new DefaultBrowserService(associations), Caps);
        model.Initialize();
        return (model, switcher, store);
    }

    [Fact]
    public void Initialize_loads_the_browsers_and_marks_the_default()
    {
        var (model, _, _) = Build();
        Assert.Equal(3, model.Browsers.Count);
        Assert.Equal(DefaultStatus.Consistent, model.Status);
        Assert.Single(model.Browsers, b => b.IsDefault);
        Assert.Equal("Safari", model.Browsers.Single(b => b.IsDefault).DisplayName);
    }

    [Fact]
    public void The_default_browser_cannot_be_set_again_but_the_others_can()
    {
        var (model, _, _) = Build();
        Assert.False(model.Browsers.Single(b => b.DisplayName == "Safari").CanSetDefault);
        Assert.True(model.Browsers.Single(b => b.DisplayName == "Firefox").CanSetDefault);
    }

    [Fact]
    public async Task Setting_a_default_asks_the_platform_and_re_reads_the_result()
    {
        var (model, switcher, _) = Build();
        var firefox = model.Browsers.Single(b => b.DisplayName == "Firefox");

        await model.SetDefaultAsync(firefox, TestContext.Current.CancellationToken);

        Assert.Equal(["org.mozilla.firefox"], switcher.Requested);
        Assert.True(model.Browsers.Single(b => b.DisplayName == "Firefox").IsDefault);
        Assert.False(model.Browsers.Single(b => b.DisplayName == "Safari").IsDefault);
        Assert.Contains("Firefox", model.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declined_switch_reports_that_nothing_changed()
    {
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var model = new MainViewModel(
            catalog, _ => new FakeSwitcher(associations, SwitchOutcome.Requested), new FakeIcons(),
            new InMemorySettingsStore(), new DefaultBrowserService(associations), Caps);
        model.Initialize();

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"), TestContext.Current.CancellationToken);

        Assert.True(model.Browsers.Single(b => b.DisplayName == "Safari").IsDefault);
        Assert.False(string.IsNullOrWhiteSpace(model.StatusMessage));
    }

    [Fact]
    public async Task An_unsupported_platform_reports_instead_of_throwing()
    {
        var associations = new FakeAssociations();
        var catalog = new FakeCatalog().Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var switcher = new FakeSwitcher(associations, SwitchOutcome.Unsupported) { Mechanism = SwitchMechanism.Unsupported };
        var model = new MainViewModel(
            catalog, _ => switcher, new FakeIcons(), new InMemorySettingsStore(),
            new DefaultBrowserService(associations), Caps);
        model.Initialize();

        await model.SetDefaultAsync(model.Browsers[0], TestContext.Current.CancellationToken);
        Assert.Equal(SwitchMechanism.Unsupported, model.Mechanism);
        Assert.False(string.IsNullOrWhiteSpace(model.StatusMessage));
    }

    /// <summary>Builds a view model on a platform whose OS does not confirm (the Linux case).</summary>
    private static (MainViewModel Model, List<bool> ConsentSeen, FakeConfirmation Prompt) BuildNoOsConfirm(bool userAnswers)
    {
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var consentSeen = new List<bool>();
        var prompt = new FakeConfirmation(userAnswers);
        var model = new MainViewModel(
            catalog,
            confirmed =>
            {
                consentSeen.Add(confirmed);
                return new FakeSwitcher(associations);
            },
            new FakeIcons(),
            new InMemorySettingsStore(),
            new DefaultBrowserService(associations),
            new NoOsConfirmCapabilities(),
            prompt);
        model.Initialize();
        return (model, consentSeen, prompt);
    }

    [Fact]
    public async Task Where_the_os_does_not_confirm_the_user_is_asked_before_anything_happens()
    {
        var (model, consentSeen, prompt) = BuildNoOsConfirm(userAnswers: true);
        consentSeen.Clear(); // discard the metadata probe

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, prompt.AskedCount);
        Assert.Contains("Firefox", prompt.LastMessage, StringComparison.Ordinal);
        // Exactly one switcher was built with consent stated as true. Unconsented instances may
        // also exist - the view model builds one to read the platform's mechanism for the status
        // line - and those are harmless by construction, because they refuse to apply anything.
        Assert.Equal(1, consentSeen.Count(c => c));
        Assert.True(model.Browsers.Single(b => b.DisplayName == "Firefox").IsDefault);
    }

    [Fact]
    public async Task Declining_the_prompt_changes_nothing_and_never_builds_a_consented_switcher()
    {
        var (model, consentSeen, prompt) = BuildNoOsConfirm(userAnswers: false);
        consentSeen.Clear();

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, prompt.AskedCount);
        // The invariant that matters: no switcher was ever told the user consented.
        Assert.DoesNotContain(true, consentSeen);
        Assert.True(model.Browsers.Single(b => b.DisplayName == "Safari").IsDefault);
        Assert.False(model.Browsers.Single(b => b.DisplayName == "Firefox").IsDefault);
    }

    [Fact]
    public async Task With_no_prompt_attached_a_platform_needing_one_can_never_proceed()
    {
        // Fail-closed: an unattended run (self-test, CI) must not change a real system setting.
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var consentedBuilds = 0;
        var model = new MainViewModel(
            catalog,
            confirmed =>
            {
                if (confirmed)
                {
                    consentedBuilds++;
                }

                return new FakeSwitcher(associations);
            },
            new FakeIcons(), new InMemorySettingsStore(), new DefaultBrowserService(associations),
            new NoOsConfirmCapabilities());
        model.Initialize();
        consentedBuilds = 0;

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, consentedBuilds);
        Assert.True(model.Browsers.Single(b => b.DisplayName == "Safari").IsDefault);
    }

    [Fact]
    public async Task The_owner_triggered_switch_completes_with_no_interactive_dialog()
    {
        // Measured on Ubuntu 26.04.1 GNOME Wayland: `--selftest <dir> --switch-default <id>`
        // opened a modal dialog over the main window that nothing could ever answer, and the
        // run hung indefinitely and wrote no report at all. The flags themselves are the
        // consent for that one path, so the switch must now run to completion unattended.
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var consentSeen = new List<bool>();
        var model = new MainViewModel(
            catalog,
            confirmed =>
            {
                consentSeen.Add(confirmed);
                return new FakeSwitcher(associations);
            },
            new FakeIcons(), new InMemorySettingsStore(), new DefaultBrowserService(associations),
            new NoOsConfirmCapabilities(),
            CommandLineConsent.Instance);
        model.Initialize();
        consentSeen.Clear();

        // No dialog exists to answer this, so a hang here is the defect reproducing.
        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.True(model.Browsers.Single(b => b.DisplayName == "Firefox").IsDefault);
        Assert.Equal(1, consentSeen.Count(c => c));

        // The report must not read as though somebody clicked "yes" in a dialog.
        Assert.Contains("--switch-default", model.ConsentSource, StringComparison.Ordinal);
        Assert.DoesNotContain("dialog, answered by the user", model.ConsentSource, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reported_consent_source_names_whoever_actually_answers()
    {
        var associations = new FakeAssociations().With("https", "com.apple.Safari");
        var catalog = new FakeCatalog().Add(Browsers.Mac("com.apple.Safari", "Safari"));

        MainViewModel Model(IPlatformCapabilities capabilities, IUserConfirmation? confirmation) => new(
            catalog, _ => new FakeSwitcher(associations), new FakeIcons(), new InMemorySettingsStore(),
            new DefaultBrowserService(associations), capabilities, confirmation);

        // Where the OS asks, BrowserFlip must not claim it did.
        Assert.Contains(
            "operating system", Model(Caps, null).ConsentSource, StringComparison.Ordinal);
        // The fail-closed default says plainly that nothing can consent.
        Assert.Contains(
            "no way of asking", Model(new NoOsConfirmCapabilities(), null).ConsentSource, StringComparison.Ordinal);
        Assert.Contains(
            "--switch-default",
            Model(new NoOsConfirmCapabilities(), CommandLineConsent.Instance).ConsentSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dialog_with_no_window_to_parent_to_still_refuses_rather_than_waits()
    {
        // The ordinary GUI path is untouched and stays fail-closed: with no owner window there
        // is no way to have asked, so the answer is no.
        var confirmation = new BrowserFlip.UI.Views.DialogUserConfirmation(() => null);

        Assert.False(await confirmation.ConfirmAsync("t", "m", TestContext.Current.CancellationToken));
        Assert.Contains("dialog", confirmation.ConsentSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_line_consent_needs_both_flags_so_ordinary_gui_use_still_asks()
    {
        var directory = BrowserFlip.UI.Diagnostics.SelfTest.OutputDirectory;
        var target = BrowserFlip.UI.Diagnostics.SelfTest.SwitchTargetId;
        try
        {
            BrowserFlip.UI.Diagnostics.SelfTest.OutputDirectory = null;
            BrowserFlip.UI.Diagnostics.SelfTest.SwitchTargetId = null;
            Assert.False(BrowserFlip.UI.Diagnostics.SelfTest.OwnerTriggeredSwitch);

            // --switch-default on its own must NOT make an interactive session consent-free.
            BrowserFlip.UI.Diagnostics.SelfTest.SwitchTargetId = "firefox.desktop";
            Assert.False(BrowserFlip.UI.Diagnostics.SelfTest.OwnerTriggeredSwitch);

            BrowserFlip.UI.Diagnostics.SelfTest.SwitchTargetId = null;
            BrowserFlip.UI.Diagnostics.SelfTest.OutputDirectory = "/tmp/report";
            Assert.False(BrowserFlip.UI.Diagnostics.SelfTest.OwnerTriggeredSwitch);

            BrowserFlip.UI.Diagnostics.SelfTest.SwitchTargetId = "firefox.desktop";
            Assert.True(BrowserFlip.UI.Diagnostics.SelfTest.OwnerTriggeredSwitch);
        }
        finally
        {
            BrowserFlip.UI.Diagnostics.SelfTest.OutputDirectory = directory;
            BrowserFlip.UI.Diagnostics.SelfTest.SwitchTargetId = target;
        }
    }

    [Fact]
    public async Task Where_the_os_confirms_browserflip_does_not_ask_a_second_time()
    {
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var prompt = new FakeConfirmation(true);
        var model = new MainViewModel(
            catalog, _ => new FakeSwitcher(associations), new FakeIcons(),
            new InMemorySettingsStore(), new DefaultBrowserService(associations), Caps, prompt);
        model.Initialize();

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, prompt.AskedCount);
        Assert.False(model.RequiresOwnConfirmation);
        Assert.True(model.Browsers.Single(b => b.DisplayName == "Firefox").IsDefault);
    }

    [Fact]
    public void Settings_changes_are_persisted_immediately()
    {
        var (model, _, store) = Build();
        var before = store.SaveCount;

        model.Topmost = true;
        model.ShowFloatingToolbar = true;
        model.Language = "ja";

        Assert.True(store.SaveCount >= before + 3);
        Assert.True(store.Load().Topmost);
        Assert.True(store.Load().ShowFloatingToolbar);
        Assert.Equal("ja", store.Load().Language);
    }

    [Fact]
    public void The_launch_counter_increases_on_every_start()
    {
        var associations = new FakeAssociations();
        var store = new InMemorySettingsStore();
        for (var i = 1; i <= 3; i++)
        {
            var model = new MainViewModel(
                new FakeCatalog(), _ => new FakeSwitcher(associations), new FakeIcons(), store,
                new DefaultBrowserService(associations), Caps);
            model.Initialize();
            Assert.Equal(i, model.LaunchCount);
        }
    }

    [Fact]
    public async Task A_request_the_os_has_not_applied_yet_is_not_reported_as_no_change()
    {
        // Regression: SwitchOutcome.Requested with no observed change used to fall through to
        // "The default browser was not changed", which is wrong while the prompt may still be open.
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var model = new MainViewModel(
            catalog, _ => new FakeSwitcher(associations, SwitchOutcome.Requested), new FakeIcons(),
            new InMemorySettingsStore(), new DefaultBrowserService(associations), Caps);
        model.Initialize();

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(BrowserFlip.UI.Localization.Strings.Get("Status_NoChange"), model.StatusMessage);
        Assert.False(string.IsNullOrWhiteSpace(model.StatusMessage));
    }

    [Fact]
    public void The_toolbar_position_round_trips_through_the_store()
    {
        var (model, _, _) = Build();
        model.SaveToolbarPosition(640, 480);
        Assert.Equal((640, 480), model.ToolbarPosition);
    }

    [Fact]
    public void No_default_at_all_is_reported_rather_than_crashing()
    {
        var (model, _, _) = Build(http: null, https: null);
        Assert.Equal(DefaultStatus.None, model.Status);
        Assert.DoesNotContain(model.Browsers, b => b.IsDefault);
    }

    // ---- D2: a request the OS has not answered is reported as pending, never as failure ----

    /// <summary>
    /// The macOS shape: Safari is the default, the user asks for Firefox, the OS alert is still
    /// open. <paramref name="outcome"/> is whatever the switcher reported.
    /// </summary>
    private static (MainViewModel Model, FakeAssociations Associations) BuildPending(SwitchResult result)
    {
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog()
            .Add(Browsers.Mac("com.apple.Safari", "Safari"))
            .Add(Browsers.Mac("org.mozilla.firefox", "Firefox"));
        var model = new MainViewModel(
            catalog, _ => new ScriptedSwitcher(result), new FakeIcons(), new InMemorySettingsStore(),
            new DefaultBrowserService(associations), Caps)
        {
            PendingWatchInterval = TimeSpan.FromMilliseconds(20),
            PendingWatchBudget = TimeSpan.FromSeconds(5),
        };
        model.Initialize();
        return (model, associations);
    }

    [Fact]
    public async Task A_timed_out_consent_prompt_is_worded_as_still_pending_not_as_a_failure()
    {
        // The reported defect: an applied change was printed as "macOS did not report the
        // change" and graded FAIL, while the OS went on to apply it 44 s / 474 s later.
        Strings.UseLanguage("en");
        var (model, _) = BuildPending(new SwitchResult(SwitchOutcome.TimedOutPending, "no answer within 20s", "com.apple.Safari"));
        model.PendingWatchBudget = TimeSpan.FromMilliseconds(1);

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.Equal(Strings.Format("Status_StillPending", "Firefox"), model.StatusMessage);
        Assert.NotEqual(Strings.Get("Status_NoChange"), model.StatusMessage);
        Assert.DoesNotContain("fail", model.StatusMessage, StringComparison.OrdinalIgnoreCase);
        // And the raw platform text is not what the user is shown.
        Assert.DoesNotContain("no answer within 20s", model.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_change_the_user_confirms_later_updates_the_message_without_a_new_request()
    {
        // The consent alert outlives the wait, so the view model keeps watching and corrects
        // itself when the change finally lands. Without this the window would keep saying
        // "pending" over a default browser that had already changed.
        Strings.UseLanguage("en");
        var (model, associations) = BuildPending(new SwitchResult(SwitchOutcome.TimedOutPending, null, "com.apple.Safari"));

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);
        Assert.Equal(Strings.Format("Status_StillPending", "Firefox"), model.StatusMessage);

        // The user answers the OS dialog.
        associations.With("http", "org.mozilla.firefox").With("https", "org.mozilla.firefox");
        await model.PendingChange;

        Assert.Equal(Strings.Format("Status_Applied", "Firefox"), model.StatusMessage);
        Assert.True(model.Browsers.Single(b => b.DisplayName == "Firefox").IsDefault);
    }

    [Fact]
    public async Task Giving_up_on_an_unanswered_prompt_says_unknown_rather_than_unchanged()
    {
        Strings.UseLanguage("en");
        var (model, _) = BuildPending(new SwitchResult(SwitchOutcome.TimedOutPending, null, "com.apple.Safari"));
        model.PendingWatchBudget = TimeSpan.FromMilliseconds(60);

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);
        await model.PendingChange;

        Assert.Equal(Strings.Format("Status_PendingUnknown", "Firefox"), model.StatusMessage);
        Assert.NotEqual(Strings.Get("Status_NoChange"), model.StatusMessage);
    }

    [Fact]
    public async Task A_declined_prompt_is_the_one_case_that_says_the_default_is_unchanged()
    {
        Strings.UseLanguage("en");
        var (model, _) = BuildPending(new SwitchResult(SwitchOutcome.Declined, "The user declined.", "com.apple.Safari"));

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.StartsWith(Strings.Get("Status_Declined"), model.StatusMessage, StringComparison.Ordinal);
        // Nothing is left watching: the user answered, so there is nothing to wait for.
        Assert.True(model.PendingChange.IsCompleted);
    }

    [Fact]
    public async Task The_switcher_result_is_kept_so_diagnostics_quote_it_instead_of_guessing()
    {
        var (model, _) = BuildPending(new SwitchResult(SwitchOutcome.TimedOutPending, "detail", "com.apple.Safari (/Applications/Safari.app)"));
        model.PendingWatchBudget = TimeSpan.FromMilliseconds(1);

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(model.LastSwitchResult);
        Assert.Equal(SwitchOutcome.TimedOutPending, model.LastSwitchResult.Outcome);
        Assert.Equal("com.apple.Safari (/Applications/Safari.app)", model.LastSwitchResult.ObservedDefault);
    }

    // ---- D3: nothing the user reads is a hardcoded English literal ----

    [Fact]
    public void The_mechanism_description_is_translated_in_every_supported_language()
    {
        // Regression: this sentence sat under the heading as an English literal, inside an
        // otherwise Japanese window.
        var (model, _, _) = Build();
        var seen = new List<string>();
        foreach (var language in Strings.SupportedLanguages)
        {
            Strings.UseLanguage(language);
            var text = model.MechanismDescription;
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("Mech_", text, StringComparison.Ordinal);
            seen.Add(text);
        }

        Assert.Equal(3, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task A_platform_error_is_shown_as_a_labelled_detail_not_as_the_message()
    {
        Strings.UseLanguage("ja");
        var (model, _) = BuildPending(new SwitchResult(SwitchOutcome.Failed, "kLSApplicationNotFoundErr"));

        await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
            TestContext.Current.CancellationToken);

        // The sentence is Japanese; the untranslatable platform text is carried inside it.
        Assert.Equal(Strings.Format("Status_RequestFailed", "kLSApplicationNotFoundErr"), model.StatusMessage);
        Assert.Contains("kLSApplicationNotFoundErr", model.StatusMessage, StringComparison.Ordinal);
        Assert.Contains('失', model.StatusMessage);
        Strings.UseLanguage("en");
    }

    [Fact]
    public async Task The_confirmation_prompt_on_a_platform_that_does_not_ask_is_translated()
    {
        // Regression: BrowserFlip's own consent dialog was an English literal.
        foreach (var language in Strings.SupportedLanguages)
        {
            Strings.UseLanguage(language);
            var (model, _, prompt) = BuildNoOsConfirm(userAnswers: false);
            await model.SetDefaultAsync(model.Browsers.Single(b => b.DisplayName == "Firefox"),
                TestContext.Current.CancellationToken);

            Assert.Equal(1, prompt.AskedCount);
            Assert.Equal(Strings.Format("Confirm_SetDefaultImmediate", "Firefox"), prompt.LastMessage);
        }

        Strings.UseLanguage("en");
    }
}
