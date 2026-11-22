using BrowserFlip.Core.Detection;
using BrowserFlip.Platform.MacOS;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// Mapping tests for the macOS adapter: they assert how a macOS application bundle becomes the
/// platform-neutral <see cref="BrowserInfo"/> that Core consumes. No OS call is made, but the
/// adapter types are annotated macOS-only, so the tests are reported as SKIPPED elsewhere rather
/// than silently not covering anything.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public class MacAdapterTests
{
    private const string Home = "/Users/someone";

    [MacFact]
    public void Bundle_identifier_becomes_the_id_and_both_protocol_handlers()
    {
        var info = MacBrowserCatalog.ToBrowserInfo(
            new MacApp("/Applications/Safari.app", "com.apple.Safari", "Safari"), Home);

        Assert.Equal("com.apple.Safari", info.Id);
        Assert.Equal("com.apple.Safari", info.HttpProgId);
        Assert.Equal("com.apple.Safari", info.HttpsProgId);
        Assert.Equal("Safari", info.DisplayName);
        Assert.Equal("/Applications/Safari.app", info.ExecutablePath);
        Assert.True(info.OwnsProgId("com.apple.Safari"));
    }

    [MacFact]
    public void A_bundle_without_an_identifier_falls_back_to_its_path()
    {
        var info = MacBrowserCatalog.ToBrowserInfo(new MacApp("/Applications/Odd.app", null, "Odd"), Home);
        Assert.Equal("/Applications/Odd.app", info.Id);
    }

    [MacFact]
    public void A_bundle_in_the_home_directory_is_a_per_user_registration()
    {
        var user = MacBrowserCatalog.ToBrowserInfo(
            new MacApp($"{Home}/Applications/Brave Browser.app", "com.brave.Browser", "Brave"), Home);
        var machine = MacBrowserCatalog.ToBrowserInfo(
            new MacApp("/Applications/Brave Browser.app", "com.brave.Browser", "Brave"), Home);

        Assert.Equal(RegistrationScope.User, user.Scope);
        Assert.Equal(RegistrationScope.Machine, machine.Scope);
    }

    [MacFact]
    public void A_missing_bundle_is_reported_as_unavailable()
    {
        var info = MacBrowserCatalog.ToBrowserInfo(
            new MacApp("/Applications/Gone-" + Guid.NewGuid().ToString("N") + ".app", "com.gone", "Gone"), Home);
        Assert.Equal(BrowserHealth.ExecutableMissing, info.Health);
    }

    [MacTheory]
    [InlineData("/Applications/Google Chrome.app", "Google Chrome")]
    [InlineData("/Applications/Safari.app/", "Safari")]
    [InlineData("/Applications/NoSuffix", "NoSuffix")]
    public void The_bundle_file_name_is_the_display_name_fallback(string path, string expected)
        => Assert.Equal(expected, MacLaunchServices.FileNameOf(path));

    [MacFact]
    public void A_switch_target_matches_by_bundle_identifier_or_by_path()
    {
        var firefox = Browsers.Mac("org.mozilla.firefox", "Firefox");
        Assert.True(MacDefaultBrowserSwitcher.Matches(
            new MacApp("/Applications/Firefox.app", "org.mozilla.firefox", "Firefox"), firefox));
        // Same bundle, identifier not readable: the path still identifies it.
        Assert.True(MacDefaultBrowserSwitcher.Matches(
            new MacApp("/Applications/Firefox.app/", null, "Firefox"), firefox));
        Assert.False(MacDefaultBrowserSwitcher.Matches(
            new MacApp("/Applications/Safari.app", "com.apple.Safari", "Safari"), firefox));
    }
}
