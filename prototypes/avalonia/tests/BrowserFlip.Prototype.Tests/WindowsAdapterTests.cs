// The Windows adapter targets net10.0-windows (it consumes Core's Windows-only types), while this
// test project targets net10.0 and therefore cannot reference it. Everything below is compiled
// only when the test project is built for a Windows TFM, where the SDK defines WINDOWS for us.
// Wiring needed (see the lane report): add net10.0-windows to this project's TargetFrameworks and
// a ProjectReference to BrowserFlip.Platform.Windows conditioned on that TFM.
#if WINDOWS
using System.IO.Compression;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using BrowserFlip.Core.Settings;
using BrowserFlip.Platform.Windows;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// Tests for the Windows platform adapter. None of them touches the real machine: the registry,
/// the file system, the OS version and the shell are all injected, so what is under test is the
/// adapter's own behaviour — the mapping onto the UI contracts — and not Windows itself.
/// </summary>
public class WindowsAdapterTests
{
    // ---- Default-browser switcher: LaunchKind -> SwitchOutcome ------------------------------

    [Fact]
    public void The_mechanism_is_always_the_system_settings_page()
    {
        // Even on Windows 10, where the per-app deep link does not exist, the mechanism is the
        // same page; only the targeting is coarser. The UI wording must not change.
        var switcher = new WindowsDefaultBrowserSwitcher(Launcher(out _, build: 19045));
        Assert.Equal(SwitchMechanism.OpensSystemSettings, switcher.Mechanism);
    }

    [Theory]
    [InlineData(LaunchKind.OpenedAppPage)]
    [InlineData(LaunchKind.OpenedTopPage)]
    public void An_opened_settings_page_is_only_a_request_never_an_applied_change(LaunchKind kind)
    {
        // Windows never reports back what the user chose in Settings, so "Applied" would be a lie.
        var result = WindowsDefaultBrowserSwitcher.ToSwitchResult(
            new LaunchResult(kind, SettingsLauncher.DefaultAppsUri));

        Assert.Equal(SwitchOutcome.Requested, result.Outcome);
        Assert.True(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void A_missing_executable_is_reported_as_browser_unavailable()
    {
        var result = WindowsDefaultBrowserSwitcher.ToSwitchResult(new LaunchResult(LaunchKind.BrowserUnavailable, null));

        Assert.Equal(SwitchOutcome.BrowserUnavailable, result.Outcome);
        Assert.Equal(WindowsDefaultBrowserSwitcher.BrowserMissingMessage, result.Error);
        Assert.False(result.Accepted);
    }

    [Fact]
    public void A_policy_block_is_a_reported_failure_that_names_the_policy()
    {
        // Core leaves Error null for this kind, so the adapter has to supply the reason itself.
        var result = WindowsDefaultBrowserSwitcher.ToSwitchResult(new LaunchResult(LaunchKind.BlockedByPolicy, null));

        Assert.Equal(SwitchOutcome.Failed, result.Outcome);
        Assert.Equal(WindowsDefaultBrowserSwitcher.PolicyBlockedMessage, result.Error);
        Assert.Contains("Policy", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shell_failure_keeps_the_error_text_and_the_uri()
    {
        var result = WindowsDefaultBrowserSwitcher.ToSwitchResult(
            new LaunchResult(LaunchKind.Failed, SettingsLauncher.DefaultAppsUri, "class not registered"));

        Assert.Equal(SwitchOutcome.Failed, result.Outcome);
        Assert.Contains("class not registered", result.Error, StringComparison.Ordinal);
        Assert.Contains("ms-settings:defaultapps", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_without_a_message_still_says_something_useful()
    {
        var result = WindowsDefaultBrowserSwitcher.ToSwitchResult(
            new LaunchResult(LaunchKind.Failed, SettingsLauncher.DefaultAppsUri, null));

        Assert.Equal(SwitchOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    // ---- Default-browser switcher: delegation to Core's SettingsLauncher -------------------

    [Fact]
    public async Task A_switch_request_opens_the_browsers_own_settings_page()
    {
        var switcher = new WindowsDefaultBrowserSwitcher(Launcher(out var shell, build: 22631, ubr: 2000));

        var result = await switcher.RequestSwitchAsync(
            WindowsFixtures.Browser("Firefox-308046B0AF4A39CB", registeredName: "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Requested, result.Outcome);
        // Proves the work really is Core's: the deep-link form and the build matrix are its code.
        Assert.Equal(
            "ms-settings:defaultapps?registeredAppMachine=Firefox",
            Assert.Single(shell.Launched).OriginalString);
    }

    [Fact]
    public async Task On_windows_10_the_top_level_page_is_opened_instead()
    {
        var switcher = new WindowsDefaultBrowserSwitcher(Launcher(out var shell, build: 19045));

        var result = await switcher.RequestSwitchAsync(
            WindowsFixtures.Browser("Google Chrome", registeredName: "Google Chrome"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Requested, result.Outcome);
        Assert.Equal("ms-settings:defaultapps", Assert.Single(shell.Launched).OriginalString);
    }

    [Fact]
    public async Task A_policy_blocked_machine_launches_nothing_at_all()
    {
        var registry = new WinFakeRegistry()
            .Set(RegistryRoot.CurrentUser, RegistryViewKind.Default, PolicyPath, "NoControlPanel", 1);
        var switcher = new WindowsDefaultBrowserSwitcher(Launcher(out var shell, registry: registry));

        var result = await switcher.RequestSwitchAsync(
            WindowsFixtures.Browser("Google Chrome", registeredName: "Google Chrome"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Failed, result.Outcome);
        Assert.Equal(WindowsDefaultBrowserSwitcher.PolicyBlockedMessage, result.Error);
        Assert.Empty(shell.Launched);
    }

    [Fact]
    public async Task An_uninstalled_browser_launches_nothing_at_all()
    {
        var switcher = new WindowsDefaultBrowserSwitcher(Launcher(out var shell, build: 22631, ubr: 2000));

        var result = await switcher.RequestSwitchAsync(
            WindowsFixtures.Browser("Gone", registeredName: "Gone", health: BrowserHealth.ExecutableMissing),
            TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.BrowserUnavailable, result.Outcome);
        Assert.Empty(shell.Launched);
    }

    [Fact]
    public async Task A_shell_that_cannot_open_settings_is_reported_as_a_failure()
    {
        var launcher = Launcher(out var shell, build: 22631, ubr: 2000);
        shell.FailWith = new InvalidOperationException("no handler");
        var switcher = new WindowsDefaultBrowserSwitcher(launcher);

        var result = await switcher.RequestSwitchAsync(
            WindowsFixtures.Browser("Firefox", registeredName: "Firefox"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Failed, result.Outcome);
        Assert.Contains("no handler", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_request_does_not_open_settings()
    {
        var switcher = new WindowsDefaultBrowserSwitcher(Launcher(out var shell, build: 22631, ubr: 2000));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => switcher.RequestSwitchAsync(WindowsFixtures.Browser("Firefox"), cancelled.Token));
        Assert.Empty(shell.Launched);
    }

    // ---- Browser catalogue: delegation to Core's BrowserDetector ---------------------------

    [Fact]
    public void The_catalogue_returns_what_cores_detector_finds_sorted_by_display_name()
    {
        var registry = new WinFakeRegistry()
            .AddBrowser(RegistryRoot.LocalMachine, "Google Chrome", "Google Chrome", @"C:\chrome.exe", "ChromeHTML", "Google Chrome")
            .AddBrowser(RegistryRoot.CurrentUser, "Firefox-308046B0AF4A39CB", "Firefox", @"C:\firefox.exe", "FirefoxURL", "Firefox");
        var files = new WinFakeFiles(@"C:\chrome.exe", @"C:\firefox.exe");

        var browsers = new WindowsBrowserCatalog(new BrowserDetector(registry, files, new WinFakeStrings())).GetBrowsers();

        Assert.Equal(["Firefox", "Google Chrome"], browsers.Select(b => b.DisplayName));
        var firefox = browsers[0];
        Assert.Equal("Firefox-308046B0AF4A39CB", firefox.Id);
        Assert.Equal("FirefoxURL", firefox.HttpsProgId);
        Assert.Equal(@"C:\firefox.exe", firefox.ExecutablePath);
        Assert.Equal("Firefox", firefox.RegisteredApplicationName);
        Assert.Equal(RegistrationScope.User, firefox.Scope);
        Assert.Equal(BrowserHealth.Ok, firefox.Health);
    }

    [Fact]
    public void The_catalogue_reports_an_uninstalled_browser_rather_than_hiding_it()
    {
        // Core's health classification is what decides this; the adapter must not filter.
        var registry = new WinFakeRegistry()
            .AddBrowser(RegistryRoot.LocalMachine, "Old Browser", "Old Browser", @"C:\gone.exe", "OldHTML", "Old Browser");

        var browsers = new WindowsBrowserCatalog(new BrowserDetector(registry, new WinFakeFiles(), new WinFakeStrings())).GetBrowsers();

        Assert.Equal(BrowserHealth.ExecutableMissing, Assert.Single(browsers).Health);
    }

    [Fact]
    public void The_catalogue_skips_registrations_that_are_not_web_browsers()
    {
        // No URLAssociations: Core treats it as not a browser (e.g. the legacy IEXPLORE entry).
        var registry = new WinFakeRegistry()
            .AddBrowser(RegistryRoot.LocalMachine, "Internet Explorer", "Internet Explorer", @"C:\iexplore.exe", null, null);

        var browsers = new WindowsBrowserCatalog(new BrowserDetector(registry, new WinFakeFiles(@"C:\iexplore.exe"), new WinFakeStrings())).GetBrowsers();

        Assert.Empty(browsers);
    }

    [Fact]
    public void An_empty_registry_yields_an_empty_catalogue_rather_than_an_error()
    {
        var catalog = new WindowsBrowserCatalog(new WinFakeRegistry(), new WinFakeFiles(), new WinFakeStrings());
        Assert.Empty(catalog.GetBrowsers());
    }

    // ---- Capabilities ----------------------------------------------------------------------

    [Fact]
    public void Windows_supports_every_window_feature_and_confirms_the_change_itself()
    {
        var capabilities = Capabilities(new WinFakeRegistry(), build: 26100, ubr: 2314);

        Assert.True(capabilities.AlwaysOnTop.IsAvailable);
        Assert.True(capabilities.WindowPositioning.IsAvailable);
        Assert.True(capabilities.TrayIcon.IsAvailable);
        Assert.True(capabilities.DefaultBrowserSwitch.IsAvailable);
        Assert.True(capabilities.OsConfirmsDefaultBrowserChange);
        Assert.All(capabilities.All(), entry => Assert.True(entry.Capability.IsAvailable));
        Assert.Equal(4, capabilities.All().Count);
    }

    [Fact]
    public void A_disabled_settings_app_makes_only_the_switch_capability_unavailable()
    {
        var registry = new WinFakeRegistry()
            .Set(RegistryRoot.LocalMachine, RegistryViewKind.Registry64, PolicyPath, "NoControlPanel", 1);

        var capabilities = Capabilities(registry);

        Assert.Equal(CapabilityState.Unavailable, capabilities.DefaultBrowserSwitch.State);
        Assert.Contains("NoControlPanel", capabilities.DefaultBrowserSwitch.Reason, StringComparison.Ordinal);
        // Unavailable, not Unsupported: the platform can do it, this configuration cannot.
        Assert.True(capabilities.AlwaysOnTop.IsAvailable);
        Assert.True(capabilities.WindowPositioning.IsAvailable);
        Assert.True(capabilities.TrayIcon.IsAvailable);
    }

    [Fact]
    public void A_hidden_default_apps_page_makes_the_switch_capability_unavailable()
    {
        var registry = new WinFakeRegistry()
            .Set(RegistryRoot.LocalMachine, RegistryViewKind.Registry64, PolicyPath, "SettingsPageVisibility", "hide:defaultapps");

        var capabilities = Capabilities(registry);

        Assert.Equal(CapabilityState.Unavailable, capabilities.DefaultBrowserSwitch.State);
        Assert.Contains("SettingsPageVisibility", capabilities.DefaultBrowserSwitch.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_managed_association_list_alone_does_not_block_the_switch()
    {
        // DefaultAssociationsConfiguration makes a change stick less, but the page is reachable,
        // so the capability must stay available and the UI must not grey the action out.
        var registry = new WinFakeRegistry()
            .Set(RegistryRoot.LocalMachine, RegistryViewKind.Registry64, @"SOFTWARE\Policies\Microsoft\Windows\System", "DefaultAssociationsConfiguration", @"\\server\assoc.xml");

        Assert.True(Capabilities(registry).DefaultBrowserSwitch.IsAvailable);
    }

    [Theory]
    [InlineData(26100, 2314, "Windows 11 (build 26100.2314)")]
    [InlineData(22000, 1817, "Windows 11 (build 22000.1817)")]
    [InlineData(19045, 0, "Windows 10 (build 19045.0)")]
    public void The_description_names_the_windows_generation_and_the_build(int build, int ubr, string expected)
        => Assert.Equal(expected, Capabilities(new WinFakeRegistry(), build, ubr).Description);

    // ---- PNG encoder -----------------------------------------------------------------------

    [Fact]
    public void The_icon_encoder_writes_a_structurally_valid_png()
    {
        var png = Png.FromBgra(Gradient(4, 3), 4, 3);

        Assert.NotNull(png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        var chunks = ReadChunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));
        var header = chunks[0].Data;
        Assert.Equal(4, BigEndian(header, 0));
        Assert.Equal(3, BigEndian(header, 4));
        Assert.Equal(8, header[8]);  // 8 bits per channel
        Assert.Equal(6, header[9]);  // truecolour with alpha
        Assert.Equal(0, header[12]); // not interlaced
    }

    [Fact]
    public void Every_png_chunk_carries_the_crc_the_specification_requires()
    {
        // Independent oracle: the bit-by-bit CRC-32 from the PNG specification, not the
        // table-driven one the encoder uses.
        foreach (var chunk in ReadChunks(Png.FromBgra(Gradient(8, 8), 8, 8)!))
        {
            Assert.Equal(SpecificationCrc(chunk.Type, chunk.Data), chunk.Crc);
        }
    }

    [Fact]
    public void The_encoded_pixels_are_the_source_pixels_in_rgba_order()
    {
        const int width = 3;
        const int height = 2;
        var bgra = Gradient(width, height);

        var png = Png.FromBgra(bgra, width, height);

        var idat = ReadChunks(png!).Single(c => c.Type == "IDAT").Data;
        using var source = new MemoryStream(idat);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var scanlines = raw.ToArray();

        Assert.Equal(((width * 4) + 1) * height, scanlines.Length);
        for (var y = 0; y < height; y++)
        {
            var row = y * ((width * 4) + 1);
            Assert.Equal(0, scanlines[row]); // filter type 0 (none)
            for (var x = 0; x < width; x++)
            {
                var source4 = ((y * width) + x) * 4;
                var target = row + 1 + (x * 4);
                Assert.Equal(bgra[source4 + 2], scanlines[target]);     // R
                Assert.Equal(bgra[source4 + 1], scanlines[target + 1]); // G
                Assert.Equal(bgra[source4], scanlines[target + 2]);     // B
                Assert.Equal(bgra[source4 + 3], scanlines[target + 3]); // A
            }
        }
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    [InlineData(4096, 4096)]
    public void Impossible_dimensions_produce_no_png_instead_of_an_exception(int width, int height)
        => Assert.Null(Png.FromBgra(new byte[64], width, height));

    [Fact]
    public void A_buffer_too_small_for_its_dimensions_produces_no_png()
        => Assert.Null(Png.FromBgra(new byte[16], 8, 8));

    [Fact]
    public void An_unreadable_icon_path_yields_no_icon_rather_than_an_exception()
    {
        // No OS call happens: the path does not exist, so the provider returns before the shell.
        var provider = new WindowsAppIconProvider();
        Assert.Null(provider.GetIconPng(null));
        Assert.Null(provider.GetIconPng("   "));
        Assert.Null(provider.GetIconPng(@"C:\does-not-exist-" + Guid.NewGuid().ToString("N") + @"\browser.exe,3"));
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private const string PolicyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private static SettingsLauncher Launcher(out WinFakeShell shell, int build = 22631, int ubr = 2000, WinFakeRegistry? registry = null)
    {
        shell = new WinFakeShell();
        return new SettingsLauncher(shell, new WinFakeOs(build, ubr), new PolicyInspector(registry ?? new WinFakeRegistry()));
    }

    private static WindowsCapabilities Capabilities(WinFakeRegistry registry, int build = 26100, int ubr = 2314)
        => new(new WinFakeOs(build, ubr), new PolicyInspector(registry));

    private static byte[] Gradient(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            bgra[i * 4] = (byte)(i * 7);
            bgra[(i * 4) + 1] = (byte)(i * 11);
            bgra[(i * 4) + 2] = (byte)(i * 13);
            bgra[(i * 4) + 3] = (byte)(i % 2 == 0 ? 0xFF : 0x80);
        }

        return bgra;
    }

    private static int BigEndian(byte[] buffer, int offset)
        => (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];

    private static List<(string Type, byte[] Data, uint Crc)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string, byte[], uint)>();
        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var length = BigEndian(png, offset);
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png[(offset + 8)..(offset + 8 + length)];
            var crc = (uint)BigEndian(png, offset + 8 + length);
            chunks.Add((type, data, crc));
            offset += 12 + length;
        }

        Assert.Equal(png.Length, offset);
        return chunks;
    }

    private static uint SpecificationCrc(string type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in System.Text.Encoding.ASCII.GetBytes(type).Concat(data))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}

/// <summary>
/// In-memory registry, keyed "Root|View|Path". Mirrors the fake in <c>BrowserFlip.Core.Tests</c>
/// deliberately rather than sharing it: the prototype test project keeps its own fakes so the
/// shipping product's test assembly stays independent of it.
/// </summary>
internal sealed class WinFakeRegistry : IRegistryReader
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public WinFakeRegistry Set(RegistryRoot root, RegistryViewKind view, string path, string? name, object value)
    {
        Key(root, view, path, create: true)![name ?? string.Empty] = value;
        return this;
    }

    /// <summary>Registers a browser the way an installer does (StartMenuInternet + RegisteredApplications).</summary>
    public WinFakeRegistry AddBrowser(RegistryRoot root, string id, string displayName, string executable, string? progId, string? registeredName)
    {
        var view = root == RegistryRoot.LocalMachine ? RegistryViewKind.Registry64 : RegistryViewKind.Default;
        var key = $@"SOFTWARE\Clients\StartMenuInternet\{id}";
        Set(root, view, key, null, displayName);
        Set(root, view, $@"{key}\shell\open\command", null, $"\"{executable}\" -- \"%1\"");
        if (progId is not null)
        {
            Set(root, view, $@"{key}\Capabilities\URLAssociations", "http", progId);
            Set(root, view, $@"{key}\Capabilities\URLAssociations", "https", progId);
        }

        if (registeredName is not null)
        {
            Set(root, view, @"SOFTWARE\RegisteredApplications", registeredName, $@"Software\Clients\StartMenuInternet\{id}\Capabilities");
        }

        return this;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, RegistryViewKind view, string path)
    {
        var prefix = Id(root, view, path) + "\\";
        return [.. _keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, RegistryViewKind view, string path)
        => Key(root, view, path, create: false) is { } k ? [.. k.Keys] : [];

    public string? GetString(RegistryRoot root, RegistryViewKind view, string path, string? name)
        => Key(root, view, path, create: false) is { } k && k.TryGetValue(name ?? string.Empty, out var v) ? v as string : null;

    public int? GetInt32(RegistryRoot root, RegistryViewKind view, string path, string name)
        => Key(root, view, path, create: false) is { } k && k.TryGetValue(name, out var v) && v is int i ? i : null;

    private Dictionary<string, object>? Key(RegistryRoot root, RegistryViewKind view, string path, bool create)
    {
        var id = Id(root, view, path);
        if (_keys.TryGetValue(id, out var key))
        {
            return key;
        }

        if (!create)
        {
            return null;
        }

        key = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        _keys[id] = key;
        return key;
    }

    // HKCU has no WOW64 split for these keys, so every HKCU view normalizes to Default.
    private static string Id(RegistryRoot root, RegistryViewKind view, string path)
        => $"{root}|{(root == RegistryRoot.CurrentUser ? RegistryViewKind.Default : view)}|{path.Trim('\\')}";
}

internal sealed class WinFakeFiles(params string[] files) : IFileSystem
{
    private readonly HashSet<string> _files = new(files, StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => _files.Contains(path);
}

internal sealed class WinFakeStrings : IIndirectStringResolver
{
    /// <summary>No MUI resources in a test: an indirect string simply does not resolve.</summary>
    public string? Resolve(string indirect) => indirect.StartsWith('@') ? null : indirect;
}

internal sealed class WinFakeOs(int build, int ubr) : IOsInfo
{
    public int BuildNumber => build;

    public int UpdateBuildRevision => ubr;
}

internal sealed class WinFakeShell : IUriLauncher
{
    public List<Uri> Launched { get; } = [];

    public Exception? FailWith { get; set; }

    public void Launch(Uri uri)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Launched.Add(uri);
    }
}

/// <summary>Browsers shaped the way the Windows detector produces them.</summary>
internal static class WindowsFixtures
{
    public static BrowserInfo Browser(string id, string? registeredName = null, BrowserHealth health = BrowserHealth.Ok) => new()
    {
        Id = id,
        DisplayName = id,
        ExecutablePath = @"C:\Program Files\" + id + @"\browser.exe",
        IconPath = @"C:\Program Files\" + id + @"\browser.exe",
        HttpProgId = id + "HTML",
        HttpsProgId = id + "HTML",
        RegisteredApplicationName = registeredName,
        RegisteredApplicationScope = registeredName is null ? null : RegistrationScope.Machine,
        Scope = RegistrationScope.Machine,
        Health = health,
    };
}
#endif
