using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;

namespace BrowserFlip.Core.Tests;

/// <summary>In-memory registry. Keys are "Root|View|Path" (view Default for HKCU).</summary>
internal sealed class FakeRegistry : IRegistryReader
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public FakeRegistry Set(RegistryRoot root, RegistryViewKind view, string path, string? name, object value)
    {
        foreach (var ancestor in Ancestors(path))
        {
            Key(root, view, ancestor, create: true);
        }

        Key(root, view, path, create: true)![name ?? string.Empty] = value;
        return this;
    }

    public FakeRegistry Touch(RegistryRoot root, RegistryViewKind view, string path)
    {
        foreach (var ancestor in Ancestors(path))
        {
            Key(root, view, ancestor, create: true);
        }

        return this;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, RegistryViewKind view, string path)
    {
        var prefix = Id(root, view, path) + "\\";
        return [.. _keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..])
            .Where(rest => !rest.Contains('\\', StringComparison.Ordinal))];
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
        if (_keys.TryGetValue(id, out var k))
        {
            return k;
        }

        if (!create)
        {
            return null;
        }

        k = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        _keys[id] = k;
        return k;
    }

    private static string Id(RegistryRoot root, RegistryViewKind view, string path)
    {
        // HKCU has no WOW64 split for these keys; normalize every HKCU view to Default.
        var v = root == RegistryRoot.CurrentUser ? RegistryViewKind.Default : view;
        return $"{root}|{v}|{path.Trim('\\')}";
    }

    private static IEnumerable<string> Ancestors(string path)
    {
        var parts = path.Trim('\\').Split('\\');
        for (var i = 1; i < parts.Length; i++)
        {
            yield return string.Join('\\', parts.Take(i));
        }
    }
}

internal sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Files.Contains(path);
}

internal sealed class FakeStrings : IIndirectStringResolver
{
    public Dictionary<string, string> Map { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Resolve(string indirect) => Map.TryGetValue(indirect, out var v) ? v : null;
}

internal sealed class FakeOs(int build, int ubr = 0) : IOsInfo
{
    public int BuildNumber { get; set; } = build;

    public int UpdateBuildRevision { get; set; } = ubr;
}

internal sealed class FakeLauncher : IUriLauncher
{
    public List<Uri> Launched { get; } = [];

    public Func<Uri, Exception?> FailWhen { get; set; } = _ => null;

    public void Launch(Uri uri)
    {
        var ex = FailWhen(uri);
        if (ex is not null)
        {
            throw ex;
        }

        Launched.Add(uri);
    }
}

internal sealed class FakeAssociations : IAssociationQuery
{
    public Dictionary<string, AssociationInfo?> Map { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AssociationInfo? QueryProtocol(string protocol) => Map.TryGetValue(protocol, out var v) ? v : null;

    public FakeAssociations With(string protocol, string? progId, string? exe = null)
    {
        Map[protocol] = progId is null ? null : new AssociationInfo(progId, null, exe);
        return this;
    }
}

/// <summary>Helpers that register browsers the way real installers do.</summary>
internal static class RegistryFixtures
{
    public const string Smi = @"SOFTWARE\Clients\StartMenuInternet";
    public const string RegApps = @"SOFTWARE\RegisteredApplications";

    public static FakeRegistry AddBrowser(
        this FakeRegistry reg,
        RegistryRoot root,
        RegistryViewKind view,
        string id,
        string? displayName,
        string? command,
        string? httpProgId,
        string? httpsProgId,
        string? registeredName = null,
        string? defaultIcon = null)
    {
        var key = $@"{Smi}\{id}";
        reg.Touch(root, view, key);
        if (displayName is not null)
        {
            reg.Set(root, view, key, null, displayName);
        }

        if (command is not null)
        {
            reg.Set(root, view, $@"{key}\shell\open\command", null, command);
        }

        if (defaultIcon is not null)
        {
            reg.Set(root, view, $@"{key}\DefaultIcon", null, defaultIcon);
        }

        if (httpProgId is not null)
        {
            reg.Set(root, view, $@"{key}\Capabilities\URLAssociations", "http", httpProgId);
        }

        if (httpsProgId is not null)
        {
            reg.Set(root, view, $@"{key}\Capabilities\URLAssociations", "https", httpsProgId);
        }

        if (registeredName is not null)
        {
            reg.Set(root, view, RegApps, registeredName, $@"Software\Clients\StartMenuInternet\{id}\Capabilities");
        }

        return reg;
    }

    public static BrowserInfo Browser(string id, string? http = null, string? https = null, string? exe = null, string? regName = null,
        RegistrationScope? regScope = RegistrationScope.Machine, BrowserHealth health = BrowserHealth.Ok)
        => new()
        {
            Id = id,
            DisplayName = id,
            HttpProgId = http,
            HttpsProgId = https,
            ExecutablePath = exe,
            RegisteredApplicationName = regName,
            RegisteredApplicationScope = regName is null ? null : regScope,
            Health = health,
        };
}
