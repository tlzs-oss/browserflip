using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// In-memory platform adapters. Every test that is not explicitly a native-API test runs against
/// these, so the suite never touches the real operating system.
/// </summary>
internal sealed class FakeCatalog : IBrowserCatalog
{
    private readonly List<BrowserInfo> _browsers = [];

    public FakeCatalog Add(BrowserInfo browser)
    {
        _browsers.Add(browser);
        return this;
    }

    public IReadOnlyList<BrowserInfo> GetBrowsers() => _browsers;
}

internal sealed class FakeAssociations : IAssociationQuery
{
    private readonly Dictionary<string, AssociationInfo?> _map = new(StringComparer.OrdinalIgnoreCase);

    public FakeAssociations With(string protocol, string? progId, string? exe = null, string? friendly = null)
    {
        _map[protocol] = progId is null ? null : new AssociationInfo(progId, friendly, exe);
        return this;
    }

    public AssociationInfo? QueryProtocol(string protocol)
        => _map.TryGetValue(protocol, out var value) ? value : null;
}

internal sealed class FakeSwitcher : IDefaultBrowserSwitcher
{
    private readonly FakeAssociations _associations;
    private readonly SwitchOutcome _outcome;

    public FakeSwitcher(FakeAssociations associations, SwitchOutcome outcome = SwitchOutcome.Applied)
    {
        _associations = associations;
        _outcome = outcome;
    }

    public SwitchMechanism Mechanism { get; set; } = SwitchMechanism.SystemConsentPrompt;

    public List<string> Requested { get; } = [];

    /// <summary>Mimics the OS accepting the change, without touching the real system.</summary>
    public Task<SwitchResult> RequestSwitchAsync(BrowserInfo browser, CancellationToken cancellationToken = default)
    {
        Requested.Add(browser.Id);
        if (_outcome is SwitchOutcome.Applied)
        {
            _associations.With("http", browser.HttpProgId, browser.ExecutablePath, browser.DisplayName);
            _associations.With("https", browser.HttpsProgId, browser.ExecutablePath, browser.DisplayName);
        }

        return Task.FromResult(new SwitchResult(_outcome));
    }
}

internal sealed class FakeIcons : IAppIconProvider
{
    public byte[]? GetIconPng(string? applicationPath, int pixelSize = 64)
        => applicationPath is null ? null : [0x89, 0x50, 0x4E, 0x47];
}

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private PrototypeSettings _settings = PrototypeSettings.Default;

    public string Location => "(in memory)";

    public int SaveCount { get; private set; }

    public PrototypeSettings Load() => _settings;

    public bool Save(PrototypeSettings settings)
    {
        _settings = settings;
        SaveCount++;
        return true;
    }
}

internal static class Browsers
{
    /// <summary>A browser shaped the way the macOS catalogue produces them (bundle id as ProgID).</summary>
    public static BrowserInfo Mac(string bundleId, string name, string? path = null) => new()
    {
        Id = bundleId,
        DisplayName = name,
        ExecutablePath = path ?? $"/Applications/{name}.app",
        IconPath = path ?? $"/Applications/{name}.app",
        HttpProgId = bundleId,
        HttpsProgId = bundleId,
        RegisteredApplicationName = bundleId,
        RegisteredApplicationScope = RegistrationScope.Machine,
        Scope = RegistrationScope.Machine,
        Health = BrowserHealth.Ok,
    };
}
