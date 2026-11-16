using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using static BrowserFlip.Core.Platform.RegistryRoot;
using static BrowserFlip.Core.Platform.RegistryViewKind;

namespace BrowserFlip.Core.Tests;

public class BrowserDetectorTests
{
    private const string ChromeExe = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string EdgeExe = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
    private const string FirefoxExe = @"C:\Program Files\Mozilla Firefox\firefox.exe";

    private readonly FakeRegistry _reg = new();
    private readonly FakeFileSystem _fs = new();
    private readonly FakeStrings _strings = new();

    private IReadOnlyList<BrowserInfo> Detect() => new BrowserDetector(_reg, _fs, _strings).Detect();

    [Fact]
    public void Detects_machine_browsers_with_registered_application_names()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "Google Chrome", "Google Chrome", $"\"{ChromeExe}\"", "ChromeHTML", "ChromeHTML", "Google Chrome", $"{ChromeExe},0")
            .AddBrowser(LocalMachine, Registry64, "Firefox-308046B0AF4A39CB", "Mozilla Firefox", $"\"{FirefoxExe}\" -osint -url \"%1\"", "FirefoxURL-308046B0AF4A39CB", "FirefoxURL-308046B0AF4A39CB", "Firefox-308046B0AF4A39CB");
        _fs.Files.UnionWith([ChromeExe, FirefoxExe]);

        var browsers = Detect();

        Assert.Collection(browsers,
            chrome =>
            {
                Assert.Equal("Google Chrome", chrome.Id);
                Assert.Equal(ChromeExe, chrome.ExecutablePath);
                Assert.Equal("ChromeHTML", chrome.HttpProgId);
                Assert.Equal("Google Chrome", chrome.RegisteredApplicationName);
                Assert.Equal(RegistrationScope.Machine, chrome.RegisteredApplicationScope);
                Assert.Equal(RegistrationScope.Machine, chrome.Scope);
                Assert.Equal(BrowserHealth.Ok, chrome.Health);
                Assert.Equal(ChromeExe, chrome.IconPath);
            },
            firefox =>
            {
                Assert.Equal("Mozilla Firefox", firefox.DisplayName);
                Assert.Equal(FirefoxExe, firefox.ExecutablePath);
                Assert.Equal("Firefox-308046B0AF4A39CB", firefox.RegisteredApplicationName);
            });
    }

    [Fact]
    public void Skips_entries_without_url_associations()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "IEXPLORE.EXE", "Internet Explorer", @"C:\Program Files\Internet Explorer\iexplore.exe", null, null);
        Assert.Empty(Detect());
    }

    [Fact]
    public void User_registration_wins_over_machine_and_uses_user_scope()
    {
        const string userExe = @"C:\Users\u\AppData\Local\Vivaldi\Application\vivaldi.exe";
        _reg.AddBrowser(CurrentUser, Default, "Vivaldi", "Vivaldi (user)", $"\"{userExe}\"", "VivaldiHTM", "VivaldiHTM", "Vivaldi")
            .AddBrowser(LocalMachine, Registry64, "Vivaldi", "Vivaldi (machine)", "\"C:\\Program Files\\Vivaldi\\vivaldi.exe\"", "VivaldiHTM", "VivaldiHTM", "Vivaldi");
        _fs.Files.Add(userExe);

        var b = Assert.Single(Detect());

        Assert.Equal("Vivaldi (user)", b.DisplayName);
        Assert.Equal(RegistrationScope.User, b.Scope);
        Assert.Equal(RegistrationScope.User, b.RegisteredApplicationScope);
        Assert.Equal(BrowserHealth.Ok, b.Health);
    }

    [Fact]
    public void Finds_32bit_only_registrations_and_dedupes_identical_views()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "Microsoft Edge", "Microsoft Edge", $"\"{EdgeExe}\"", "MSEdgeHTM", "MSEdgeHTM", "Microsoft Edge")
            .AddBrowser(LocalMachine, Registry32, "Microsoft Edge", "Microsoft Edge", $"\"{EdgeExe}\"", "MSEdgeHTM", "MSEdgeHTM")
            .AddBrowser(LocalMachine, Registry32, "OldBrowser", "Old Browser", "\"C:\\Old\\old.exe\"", "OldURL", null, "OldBrowser");
        _fs.Files.UnionWith([EdgeExe, @"C:\Old\old.exe"]);

        var browsers = Detect();

        Assert.Equal(["Microsoft Edge", "Old Browser"], browsers.Select(b => b.DisplayName));
        Assert.Null(browsers[1].HttpsProgId);
    }

    [Fact]
    public void Marks_missing_executable()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "Brave", "Brave", "\"C:\\Gone\\brave.exe\"", "BraveHTML", "BraveHTML", "Brave");
        Assert.Equal(BrowserHealth.ExecutableMissing, Assert.Single(Detect()).Health);
    }

    [Fact]
    public void Marks_missing_command_as_executable_missing()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "Broken", "Broken", null, "BrokenURL", "BrokenURL", "Broken");
        var b = Assert.Single(Detect());
        Assert.Equal(BrowserHealth.ExecutableMissing, b.Health);
        Assert.Null(b.ExecutablePath);
    }

    [Fact]
    public void Marks_browser_without_registered_application()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "Opera", "Opera", "\"C:\\Opera\\opera.exe\"", "OperaStable", "OperaStable");
        _fs.Files.Add(@"C:\Opera\opera.exe");

        var b = Assert.Single(Detect());

        Assert.Equal(BrowserHealth.NotInRegisteredApplications, b.Health);
        Assert.Null(b.RegisteredApplicationName);
    }

    [Fact]
    public void Matches_registered_application_by_key_name_when_path_differs()
    {
        _reg.AddBrowser(CurrentUser, Default, "Opera GXStable", "Opera GX", "\"C:\\GX\\opera.exe\"", "Opera GXStable", "Opera GXStable");
        _reg.Set(CurrentUser, Default, RegistryFixtures.RegApps, "Opera GXStable", @"Software\Clients\StartMenuInternet\Opera GXStable\Capabilities_Old");
        _fs.Files.Add(@"C:\GX\opera.exe");

        var b = Assert.Single(Detect());

        Assert.Equal("Opera GXStable", b.RegisteredApplicationName);
        Assert.Equal(RegistrationScope.User, b.RegisteredApplicationScope);
    }

    [Fact]
    public void Machine_browser_registered_only_in_user_RegisteredApplications_uses_user_scope()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "Brave", "Brave", "\"C:\\Brave\\brave.exe\"", "BraveHTML", "BraveHTML");
        _reg.Set(CurrentUser, Default, RegistryFixtures.RegApps, "Brave", @"Software\Clients\StartMenuInternet\Brave\Capabilities");
        _fs.Files.Add(@"C:\Brave\brave.exe");

        var b = Assert.Single(Detect());

        Assert.Equal(RegistrationScope.Machine, b.Scope);
        Assert.Equal(RegistrationScope.User, b.RegisteredApplicationScope);
    }

    [Fact]
    public void Resolves_indirect_display_names_and_falls_back_to_key_name()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "Indirect", "@C:\\x\\res.dll,-1", "\"C:\\x\\a.exe\"", "IndirectURL", "IndirectURL", "Indirect");
        _reg.AddBrowser(LocalMachine, Registry64, "Unresolvable", "@C:\\x\\missing.dll,-1", "\"C:\\x\\b.exe\"", "UURL", "UURL", "Unresolvable");
        _reg.AddBrowser(LocalMachine, Registry64, "NoName", null, "\"C:\\x\\c.exe\"", "NURL", "NURL", "NoName");
        _reg.Set(LocalMachine, Registry64, $@"{RegistryFixtures.Smi}\NoName\Capabilities", "ApplicationName", "No Name App");
        _strings.Map["@C:\\x\\res.dll,-1"] = "Resolved Browser";

        var names = Detect().Select(b => b.DisplayName).ToList();

        Assert.Contains("Resolved Browser", names);
        Assert.Contains("Unresolvable", names);
        Assert.Contains("No Name App", names);
    }

    [Fact]
    public void Uses_default_icon_when_present_otherwise_exe()
    {
        _reg.AddBrowser(LocalMachine, Registry64, "A", "A", "\"C:\\a\\a.exe\"", "AURL", "AURL", "A", "\"C:\\a\\icons.dll\",-5");
        _reg.AddBrowser(LocalMachine, Registry64, "B", "B", "\"C:\\b\\b.exe\"", "BURL", "BURL", "B");

        var list = Detect();

        Assert.Equal((@"C:\a\icons.dll", -5), (list[0].IconPath, list[0].IconIndex));
        Assert.Equal((@"C:\b\b.exe", 0), (list[1].IconPath, list[1].IconIndex));
    }

    [Fact]
    public void Returns_empty_when_nothing_registered()
        => Assert.Empty(Detect());

    [Fact]
    public void OwnsProgId_is_case_insensitive_and_null_safe()
    {
        var b = RegistryFixtures.Browser("X", http: "XHTML", https: "XSURL");
        Assert.True(b.OwnsProgId("xhtml"));
        Assert.True(b.OwnsProgId("XSURL"));
        Assert.False(b.OwnsProgId(null));
        Assert.False(b.OwnsProgId("Other"));
    }
}
