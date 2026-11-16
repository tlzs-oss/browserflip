using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using BrowserFlip.Core.Settings;
using static BrowserFlip.Core.Platform.RegistryRoot;
using static BrowserFlip.Core.Platform.RegistryViewKind;
using static BrowserFlip.Core.Tests.RegistryFixtures;

namespace BrowserFlip.Core.Tests;

public class PolicyInspectorTests
{
    private readonly FakeRegistry _reg = new();

    private PolicyStatus Inspect() => new PolicyInspector(_reg).Inspect();

    [Fact]
    public void No_policies_means_nothing_blocked()
    {
        var s = Inspect();
        Assert.Equal(PolicyStatus.None, s);
        Assert.False(s.BlocksNavigation);
    }

    [Theory]
    [InlineData(CurrentUser)]
    [InlineData(LocalMachine)]
    public void NoControlPanel_blocks_settings(RegistryRoot root)
    {
        _reg.Set(root, root == LocalMachine ? Registry64 : Default, PolicyInspector.ExplorerPolicies, "NoControlPanel", 1);
        var s = Inspect();
        Assert.True(s.SettingsAppBlocked);
        Assert.True(s.BlocksNavigation);
    }

    [Fact]
    public void NoControlPanel_zero_is_ignored()
    {
        _reg.Set(CurrentUser, Default, PolicyInspector.ExplorerPolicies, "NoControlPanel", 0);
        Assert.False(Inspect().SettingsAppBlocked);
    }

    [Fact]
    public void SettingsPageVisibility_from_machine_hides_default_apps()
    {
        _reg.Set(LocalMachine, Registry64, PolicyInspector.ExplorerPolicies, "SettingsPageVisibility", "hide:bluetooth;defaultapps");
        Assert.True(Inspect().DefaultAppsPageHidden);
    }

    [Fact]
    public void DefaultAssociationsConfiguration_marks_managed_without_blocking()
    {
        _reg.Set(LocalMachine, Registry64, PolicyInspector.SystemPolicies, "DefaultAssociationsConfiguration", @"\\server\share\assoc.xml");
        var s = Inspect();
        Assert.True(s.AssociationsManaged);
        Assert.False(s.BlocksNavigation);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("hide:defaultapps", true)]
    [InlineData("HIDE: bluetooth ; DefaultApps ", true)]
    [InlineData("hide:bluetooth;display", false)]
    [InlineData("showonly:display;defaultapps", false)]
    [InlineData("showonly:display;network", true)]
    [InlineData("garbage", false)]
    [InlineData("other:defaultapps", false)]
    public void IsDefaultAppsHidden_interprets_policy(string? value, bool hidden)
        => Assert.Equal(hidden, PolicyInspector.IsDefaultAppsHidden(value));
}

public class SettingsLauncherTests
{
    private readonly FakeRegistry _reg = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeOs _os = new(22631);

    private SettingsLauncher Sut() => new(_launcher, _os, new PolicyInspector(_reg));

    private static readonly BrowserInfo MachineFirefox = Browser("Firefox-308046B0AF4A39CB", "FirefoxURL", "FirefoxURL", regName: "Firefox-308046B0AF4A39CB");
    private static readonly BrowserInfo UserVivaldi = Browser("Vivaldi", "VivaldiHTM", "VivaldiHTM", regName: "Vivaldi", regScope: RegistrationScope.User);

    [Fact]
    public void Opens_machine_deep_link_on_windows_11()
    {
        var r = Sut().Open(MachineFirefox);
        Assert.Equal(LaunchKind.OpenedAppPage, r.Kind);
        Assert.True(r.Launched);
        Assert.Equal("ms-settings:defaultapps?registeredAppMachine=Firefox-308046B0AF4A39CB", Assert.Single(_launcher.Launched).OriginalString);
    }

    [Fact]
    public void Opens_user_deep_link_for_user_registration()
    {
        Sut().Open(UserVivaldi);
        Assert.Equal("ms-settings:defaultapps?registeredAppUser=Vivaldi", Assert.Single(_launcher.Launched).OriginalString);
    }

    [Fact]
    public void Escapes_registered_name()
    {
        var b = Browser("Google Chrome", "ChromeHTML", "ChromeHTML", regName: "Google Chrome&x=1");
        Assert.Equal("ms-settings:defaultapps?registeredAppMachine=Google%20Chrome%26x%3D1", Sut().BuildAppPageUri(b)!.OriginalString);
    }

    [Fact]
    public void Uses_top_page_on_windows_10()
    {
        _os.BuildNumber = 19045;
        var r = Sut().Open(MachineFirefox);
        Assert.Equal(LaunchKind.OpenedTopPage, r.Kind);
        Assert.Equal("ms-settings:defaultapps", Assert.Single(_launcher.Launched).OriginalString);
    }

    [Theory]
    [InlineData(19045, 9999, false)] // Windows 10 22H2
    [InlineData(22000, 1816, false)] // Windows 11 21H2 before 2023-04 CU
    [InlineData(22000, 1817, true)]
    [InlineData(22621, 1554, false)] // Windows 11 22H2 before 2023-04 CU
    [InlineData(22621, 1555, true)]
    [InlineData(22631, 0, true)] // 23H2
    [InlineData(26100, 0, true)] // 24H2
    [InlineData(26300, 0, true)]
    public void SupportsAppDeepLink_follows_microsoft_documentation(int build, int ubr, bool expected)
        => Assert.Equal(expected, SettingsLauncher.SupportsAppDeepLink(build, ubr));

    [Fact]
    public void Uses_top_page_on_unpatched_windows_11_22h2()
    {
        _os.BuildNumber = 22621;
        _os.UpdateBuildRevision = 1000;
        Assert.Equal(LaunchKind.OpenedTopPage, Sut().Open(MachineFirefox).Kind);
    }

    [Fact]
    public void Uses_top_page_when_not_in_registered_applications()
    {
        var b = Browser("Opera", "OperaStable", "OperaStable", health: BrowserHealth.NotInRegisteredApplications);
        Assert.Equal(LaunchKind.OpenedTopPage, Sut().Open(b).Kind);
    }

    [Fact]
    public void Falls_back_to_top_page_when_deep_link_fails()
    {
        _launcher.FailWhen = u => u.OriginalString.Contains('?', StringComparison.Ordinal) ? new InvalidOperationException("no") : null;
        var r = Sut().Open(MachineFirefox);
        Assert.Equal(LaunchKind.OpenedTopPage, r.Kind);
        Assert.Equal(SettingsLauncher.DefaultAppsUri, r.Uri);
    }

    [Fact]
    public void Reports_failure_when_settings_cannot_open()
    {
        _launcher.FailWhen = _ => new System.ComponentModel.Win32Exception(1155, "No application is associated");
        var r = Sut().Open(MachineFirefox);
        Assert.Equal(LaunchKind.Failed, r.Kind);
        Assert.False(r.Launched);
        Assert.Contains("No application", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_launch_for_missing_browser()
    {
        var b = Browser("Gone", "GoneURL", "GoneURL", regName: "Gone", health: BrowserHealth.ExecutableMissing);
        Assert.Equal(LaunchKind.BrowserUnavailable, Sut().Open(b).Kind);
        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public void Does_not_launch_when_policy_blocks()
    {
        _reg.Set(LocalMachine, Registry64, PolicyInspector.ExplorerPolicies, "SettingsPageVisibility", "hide:defaultapps");
        Assert.Equal(LaunchKind.BlockedByPolicy, Sut().Open(MachineFirefox).Kind);
        Assert.Equal(LaunchKind.BlockedByPolicy, Sut().OpenTopPage().Kind);
        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public void OpenTopPage_launches_and_reports_failure()
    {
        Assert.Equal(LaunchKind.OpenedTopPage, Sut().OpenTopPage().Kind);
        _launcher.FailWhen = _ => new InvalidOperationException("x");
        Assert.Equal(LaunchKind.Failed, Sut().OpenTopPage().Kind);
    }

    [Fact]
    public void Real_launcher_refuses_non_settings_uris()
        => Assert.Throws<ArgumentException>(() => new ShellUriLauncher().Launch(new Uri("https://example.com")));

    [Fact]
    public void Only_ms_settings_uris_are_ever_built()
    {
        Sut().Open(MachineFirefox);
        Sut().Open(UserVivaldi);
        Assert.All(_launcher.Launched, u => Assert.Equal("ms-settings", u.Scheme));
    }
}
