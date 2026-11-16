using BrowserFlip.Core.Platform;

namespace BrowserFlip.Core.Detection;

/// <summary>
/// Discovers browsers from the Windows "Default Programs" registration model:
/// <c>Software\Clients\StartMenuInternet\&lt;key&gt;</c> plus <c>Software\RegisteredApplications</c>,
/// in HKCU and HKLM (64-bit and 32-bit views). No executable paths are hard-coded.
/// </summary>
public sealed class BrowserDetector
{
    internal const string StartMenuInternet = @"SOFTWARE\Clients\StartMenuInternet";
    internal const string RegisteredApplications = @"SOFTWARE\RegisteredApplications";

    private static readonly (RegistryRoot Root, RegistryViewKind View)[] SearchOrder =
    [
        (RegistryRoot.CurrentUser, RegistryViewKind.Default),
        (RegistryRoot.LocalMachine, RegistryViewKind.Registry64),
        (RegistryRoot.LocalMachine, RegistryViewKind.Registry32),
    ];

    private readonly IRegistryReader _registry;
    private readonly IFileSystem _fileSystem;
    private readonly IIndirectStringResolver _strings;

    public BrowserDetector(IRegistryReader registry, IFileSystem fileSystem, IIndirectStringResolver strings)
    {
        _registry = registry;
        _fileSystem = fileSystem;
        _strings = strings;
    }

    /// <summary>
    /// Returns registered browsers that declare an http or https URL association, sorted by display name.
    /// Per-user registrations take precedence over per-machine ones with the same key name.
    /// </summary>
    public IReadOnlyList<BrowserInfo> Detect()
    {
        var registered = LoadRegisteredApplications();
        var result = new Dictionary<string, BrowserInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var (root, view) in SearchOrder)
        {
            foreach (var id in _registry.GetSubKeyNames(root, view, StartMenuInternet))
            {
                if (result.ContainsKey(id))
                {
                    continue;
                }

                var browser = Read(root, view, id, registered);
                if (browser is not null)
                {
                    result[id] = browser;
                }
            }
        }

        return [.. result.Values.OrderBy(b => b.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    private BrowserInfo? Read(RegistryRoot root, RegistryViewKind view, string id, List<RegisteredApp> registered)
    {
        var key = $@"{StartMenuInternet}\{id}";
        var http = NonEmpty(_registry.GetString(root, view, $@"{key}\Capabilities\URLAssociations", "http"));
        var https = NonEmpty(_registry.GetString(root, view, $@"{key}\Capabilities\URLAssociations", "https"));
        if (http is null && https is null)
        {
            // Not a web browser for our purposes (e.g. legacy IEXPLORE.EXE registration on Windows 11).
            return null;
        }

        var exe = RegistryValueParser.ParseExecutable(_registry.GetString(root, view, $@"{key}\shell\open\command", null));
        var (iconPath, iconIndex) = RegistryValueParser.ParseIconLocation(_registry.GetString(root, view, $@"{key}\DefaultIcon", null));
        var displayName = ResolveDisplayName(root, view, key, id);

        var capabilitiesPath = RegistryValueParser.NormalizeKeyPath($@"{key}\Capabilities");
        var preferredScope = root == RegistryRoot.CurrentUser ? RegistrationScope.User : RegistrationScope.Machine;
        var app = registered.FirstOrDefault(r => r.Scope == preferredScope && PathEquals(r.CapabilitiesPath, capabilitiesPath))
                  ?? registered.FirstOrDefault(r => PathEquals(r.CapabilitiesPath, capabilitiesPath))
                  // Some installers register under a different key-name casing/path; fall back to the key name.
                  ?? registered.FirstOrDefault(r => string.Equals(r.Name, id, StringComparison.OrdinalIgnoreCase));

        var health = BrowserHealth.Ok;
        if (exe is null || !_fileSystem.FileExists(exe))
        {
            health = BrowserHealth.ExecutableMissing;
        }
        else if (app is null)
        {
            health = BrowserHealth.NotInRegisteredApplications;
        }

        return new BrowserInfo
        {
            Id = id,
            DisplayName = displayName,
            ExecutablePath = exe,
            IconPath = iconPath ?? exe,
            IconIndex = iconPath is null ? 0 : iconIndex,
            HttpProgId = http,
            HttpsProgId = https,
            RegisteredApplicationName = app?.Name,
            RegisteredApplicationScope = app?.Scope,
            Scope = root == RegistryRoot.CurrentUser ? RegistrationScope.User : RegistrationScope.Machine,
            Health = health,
        };
    }

    private string ResolveDisplayName(RegistryRoot root, RegistryViewKind view, string key, string id)
    {
        foreach (var candidate in new[]
                 {
                     _registry.GetString(root, view, key, null),
                     _registry.GetString(root, view, $@"{key}\Capabilities", "ApplicationName"),
                     _registry.GetString(root, view, key, "LocalizedString"),
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var resolved = candidate.StartsWith('@') ? _strings.Resolve(candidate) : candidate;
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved.Trim();
            }
        }

        return id;
    }

    private List<RegisteredApp> LoadRegisteredApplications()
    {
        var list = new List<RegisteredApp>();
        foreach (var (root, view) in SearchOrder)
        {
            var scope = root == RegistryRoot.CurrentUser ? RegistrationScope.User : RegistrationScope.Machine;
            foreach (var name in _registry.GetValueNames(root, view, RegisteredApplications))
            {
                var path = _registry.GetString(root, view, RegisteredApplications, name);
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                {
                    list.Add(new RegisteredApp(name, RegistryValueParser.NormalizeKeyPath(path), scope));
                }
            }
        }

        return list;
    }

    private static bool PathEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed record RegisteredApp(string Name, string CapabilitiesPath, RegistrationScope Scope);
}
