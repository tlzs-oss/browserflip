using System.IO;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;
using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Settings;
using static BrowserFlip.Core.Platform.RegistryRoot;
using static BrowserFlip.Core.Platform.RegistryViewKind;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BrowserFlip.Core.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private const string ChromeExe = @"C:\Chrome\chrome.exe";
    private const string EdgeExe = @"C:\Edge\msedge.exe";

    private readonly FakeRegistry _reg = new();
    private readonly FakeFileSystem _fs = new();
    private readonly FakeAssociations _assoc = new();
    private readonly FakeLauncher _launcher = new();
    private readonly List<MainViewModel> _created = [];
    private DateTimeOffset _now = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

    public MainViewModelTests()
    {
        Loc.Instance.SetLanguage("en");
        _reg.AddBrowser(LocalMachine, Registry64, "Google Chrome", "Google Chrome", $"\"{ChromeExe}\"", "ChromeHTML", "ChromeHTML", "Google Chrome")
            .AddBrowser(LocalMachine, Registry64, "Microsoft Edge", "Microsoft Edge", $"\"{EdgeExe}\"", "MSEdgeHTM", "MSEdgeHTM", "Microsoft Edge");
        _fs.Files.UnionWith([ChromeExe, EdgeExe]);
        _assoc.With("http", "MSEdgeHTM").With("https", "MSEdgeHTM");
    }

    public void Dispose()
    {
        _created.ForEach(vm => vm.Dispose());
        Loc.Instance.SetLanguage("en");
    }

    private MainViewModel Create()
    {
        var policy = new PolicyInspector(_reg);
        var vm = new MainViewModel(
            new BrowserDetector(_reg, _fs, new FakeStrings()),
            new DefaultBrowserService(_assoc),
            new SettingsLauncher(_launcher, new FakeOs(26100), policy),
            policy,
            _ => null,
            UserSettings.InMemory(),
            startTimer: false,
            clock: () => _now);
        vm.Refresh();
        _created.Add(vm);
        return vm;
    }

    private static BrowserCardViewModel Card(MainViewModel vm, string id) => vm.Browsers.Single(c => c.Browser.Id == id);

    [Fact]
    public void Refresh_lists_browsers_and_marks_default()
    {
        var vm = Create();
        Assert.Equal(["Google Chrome", "Microsoft Edge"], vm.Browsers.Select(b => b.DisplayName));
        Assert.True(Card(vm, "Microsoft Edge").IsDefault);
        Assert.False(Card(vm, "Google Chrome").HasBadge);
        Assert.Equal("Current default browser: Microsoft Edge", vm.CurrentDefaultText);
        Assert.False(vm.IsMismatch);
        Assert.False(vm.HasNoBrowsers);
    }

    [Fact]
    public void Select_opens_settings_and_reports_success_after_user_confirms()
    {
        var vm = Create();
        vm.Select(Card(vm, "Google Chrome"));

        Assert.Equal("ms-settings:defaultapps?registeredAppMachine=Google%20Chrome", Assert.Single(_launcher.Launched).OriginalString);
        Assert.True(vm.IsWaitingForChange);
        Assert.Contains("Set default", vm.GuidanceText, StringComparison.Ordinal);

        vm.Poll();
        Assert.True(vm.IsWaitingForChange);

        _assoc.With("http", "ChromeHTML").With("https", "ChromeHTML");
        vm.Poll();

        Assert.False(vm.IsWaitingForChange);
        Assert.Equal(MessageKind.Success, vm.MessageKind);
        Assert.Equal("Google Chrome is now your default browser.", vm.MessageText);
        Assert.True(Card(vm, "Google Chrome").IsDefault);
    }

    [Fact]
    public void Returning_without_change_reports_no_change()
    {
        var vm = Create();
        vm.Select(Card(vm, "Google Chrome"));
        vm.OnActivated(); // still focused right after launch: not a "return"
        Assert.True(vm.IsWaitingForChange);

        vm.OnDeactivated();
        vm.OnActivated();

        Assert.False(vm.IsWaitingForChange);
        Assert.Equal("The default browser was not changed.", vm.MessageText);
    }

    [Fact]
    public void Timeout_ends_waiting()
    {
        var vm = Create();
        vm.Select(Card(vm, "Google Chrome"));
        _now += MainViewModel.WaitLimit + TimeSpan.FromSeconds(1);
        vm.Poll();
        Assert.False(vm.IsWaitingForChange);
        Assert.Equal(MessageKind.Info, vm.MessageKind);
    }

    [Fact]
    public void Partial_change_is_warned()
    {
        var vm = Create();
        vm.Select(Card(vm, "Google Chrome"));
        vm.OnDeactivated();
        _assoc.With("http", "ChromeHTML");
        vm.OnActivated();
        _now += MainViewModel.PartialGrace;
        vm.Poll();
        Assert.Equal(MessageKind.Warning, vm.MessageKind);
        Assert.True(vm.IsMismatch);
        Assert.Equal("HTTP and HTTPS open in different apps — HTTP: Google Chrome / HTTPS: Microsoft Edge", vm.CurrentDefaultText);
    }

    [Fact]
    public void Partial_on_return_waits_briefly_for_the_second_protocol()
    {
        var vm = Create();
        vm.Select(Card(vm, "Google Chrome"));
        vm.OnDeactivated();
        _assoc.With("http", "ChromeHTML");
        vm.OnActivated();
        Assert.True(vm.IsWaitingForChange); // grace period, no warning yet

        _assoc.With("https", "ChromeHTML");
        vm.Poll();
        Assert.Equal(MessageKind.Success, vm.MessageKind);
    }

    [Fact]
    public void Partial_is_reported_after_grace_period()
    {
        var vm = Create();
        vm.Select(Card(vm, "Google Chrome"));
        vm.OnDeactivated();
        _assoc.With("http", "ChromeHTML");
        vm.OnActivated();
        vm.Poll();
        Assert.True(vm.IsWaitingForChange);

        _now += MainViewModel.PartialGrace;
        vm.Poll();
        Assert.Equal(MessageKind.Warning, vm.MessageKind);
        Assert.False(vm.IsWaitingForChange);
    }

    [Fact]
    public void Policy_error_clears_after_policy_is_removed()
    {
        _reg.Set(LocalMachine, Registry64, PolicyInspector.ExplorerPolicies, "SettingsPageVisibility", "hide:defaultapps");
        var vm = Create();
        Assert.Equal(MessageKind.Error, vm.MessageKind);

        _reg.Set(LocalMachine, Registry64, PolicyInspector.ExplorerPolicies, "SettingsPageVisibility", "hide:bluetooth");
        vm.Refresh();
        Assert.Equal(MessageKind.None, vm.MessageKind);
    }

    [Fact]
    public void Browser_without_registered_application_explains_top_page()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "Opera", "Opera", "\"C:\\Opera\\opera.exe\"", "OperaStable", "OperaStable");
        _fs.Files.Add(@"C:\Opera\opera.exe");
        var vm = Create();
        var card = Card(vm, "Opera");
        Assert.True(card.IsAvailable);
        Assert.Equal("Click to open Default apps, then choose it there", card.HintText);

        vm.Select(card);
        Assert.Equal("ms-settings:defaultapps", _launcher.Launched.Single().OriginalString);
        Assert.Contains("Default apps", vm.GuidanceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_card_hint_offers_review()
        => Assert.Equal("Click to review in Windows Settings", Card(Create(), "Microsoft Edge").HintText);

    [Fact]
    public void Language_change_updates_font_stack()
    {
        Loc.Instance.SetLanguage("zh-CN");
        Assert.Contains("Microsoft YaHei UI", Loc.Instance.FontFamilyName, StringComparison.Ordinal);
        Assert.DoesNotContain("Yu Gothic", Loc.Instance.FontFamilyName, StringComparison.Ordinal);
        Loc.Instance.SetLanguage("ja");
        Assert.Contains("Yu Gothic UI", Loc.Instance.FontFamilyName, StringComparison.Ordinal);
    }

    [Fact]
    public void Selecting_current_default_does_not_track()
    {
        var vm = Create();
        vm.Select(Card(vm, "Microsoft Edge"));
        Assert.False(vm.IsWaitingForChange);
        Assert.Single(_launcher.Launched);
        Assert.Contains("already", vm.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_browser_card_is_disabled_and_select_reports_error()
    {
        _fs.Files.Remove(ChromeExe);
        var vm = Create();
        var card = Card(vm, "Google Chrome");
        Assert.False(card.IsAvailable);

        vm.Select(card);

        Assert.Empty(_launcher.Launched);
        Assert.Equal(MessageKind.Error, vm.MessageKind);
    }

    [Fact]
    public void Policy_block_is_shown_and_nothing_is_launched()
    {
        _reg.Set(LocalMachine, Registry64, PolicyInspector.ExplorerPolicies, "SettingsPageVisibility", "hide:defaultapps");
        var vm = Create();
        Assert.True(vm.IsBlockedByPolicy);
        Assert.Equal(MessageKind.Error, vm.MessageKind);

        vm.Select(Card(vm, "Google Chrome"));
        vm.OpenDefaultAppsCommand.Execute(null);
        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public void Managed_associations_show_warning()
    {
        _reg.Set(LocalMachine, Registry64, PolicyInspector.SystemPolicies, "DefaultAssociationsConfiguration", @"C:\assoc.xml");
        Assert.True(Create().IsAssociationsManaged);
    }

    [Fact]
    public void Launch_failure_is_reported()
    {
        _launcher.FailWhen = _ => new InvalidOperationException("boom");
        var vm = Create();
        vm.Select(Card(vm, "Google Chrome"));
        Assert.Equal(MessageKind.Error, vm.MessageKind);
        Assert.Contains("boom", vm.MessageText, StringComparison.Ordinal);
        Assert.False(vm.IsWaitingForChange);
    }

    [Fact]
    public void Activation_refreshes_state_when_changed_elsewhere()
    {
        var vm = Create();
        _assoc.With("http", "ChromeHTML").With("https", "ChromeHTML");
        vm.OnActivated();
        Assert.True(Card(vm, "Google Chrome").IsDefault);
    }

    [Fact]
    public void Unrecognized_and_none_states_are_described()
    {
        _assoc.Map["http"] = new BrowserFlip.Core.Platform.AssociationInfo("OtherURL", "Other Browser", null);
        _assoc.Map["https"] = new BrowserFlip.Core.Platform.AssociationInfo("OtherURL", "Other Browser", null);
        Assert.Equal("Current default: Other Browser (not in this list)", Create().CurrentDefaultText);

        _assoc.Map.Clear();
        Assert.Equal("No default browser is set.", Create().CurrentDefaultText);
    }

    [Fact]
    public void Language_switch_relocalizes_texts()
    {
        var vm = Create();
        vm.Select(Card(vm, "Google Chrome"));

        Loc.Instance.SetLanguage("ja");
        Assert.Equal("現在の既定のブラウザー: Microsoft Edge", vm.CurrentDefaultText);
        Assert.Equal("Windows の設定での変更を待っています…", vm.MessageText);
        Assert.Equal("クリックして既定に設定", Card(vm, "Google Chrome").HintText);
        Assert.Equal("システムに合わせる", vm.Languages[0].DisplayName);

        Loc.Instance.SetLanguage("zh-CN");
        Assert.Equal("当前默认浏览器：Microsoft Edge", vm.CurrentDefaultText);
    }

    [Theory]
    [InlineData("ja", "en-US", "ja")]
    [InlineData("zh-CN", "en-US", "zh-CN")]
    [InlineData("en", "ja-JP", "en")]
    [InlineData("", "ja-JP", "ja")]       // follow the user's display language
    [InlineData("", "zh-TW", "zh-CN")]
    [InlineData("", "zh-Hans-CN", "zh-CN")]
    [InlineData("", "de-DE", "en")]
    [InlineData("fr", "ja-JP", "ja")]     // unsupported setting falls back to the display language
    public void Loc_resolves_languages_from_user_display_language(string requested, string userUi, string expected)
        => Assert.Equal(expected, Loc.Resolve(requested, System.Globalization.CultureInfo.GetCultureInfo(userUi)).Name);

    [Fact]
    public void Settings_round_trip_and_tolerate_corrupt_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "browserflip-tests-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "settings.json");
        try
        {
            var s = UserSettings.Load(file);
            s.Language = "ja";
            s.Save();
            Assert.Equal("ja", UserSettings.Load(file).Language);

            File.WriteAllText(file, "{ not json");
            Assert.Equal(string.Empty, UserSettings.Load(file).Language);

            File.WriteAllText(file, "{\"Language\": null}");
            Assert.Equal(string.Empty, UserSettings.Load(file).Language);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
