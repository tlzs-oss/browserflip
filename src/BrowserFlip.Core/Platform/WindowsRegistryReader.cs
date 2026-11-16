using System.Security;
using Microsoft.Win32;

namespace BrowserFlip.Core.Platform;

/// <summary>Read-only <see cref="IRegistryReader"/> backed by <see cref="Registry"/>.</summary>
public sealed class WindowsRegistryReader : IRegistryReader
{
    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, RegistryViewKind view, string path)
        => WithKey(root, view, path, k => (IReadOnlyList<string>)k.GetSubKeyNames(), []);

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, RegistryViewKind view, string path)
        => WithKey(root, view, path, k => (IReadOnlyList<string>)k.GetValueNames(), []);

    public string? GetString(RegistryRoot root, RegistryViewKind view, string path, string? name)
        => WithKey(root, view, path, k => k.GetValue(name ?? string.Empty) as string, null);

    public int? GetInt32(RegistryRoot root, RegistryViewKind view, string path, string name)
        => WithKey(root, view, path, k => k.GetValue(name) is int i ? i : (int?)null, null);

    private static T WithKey<T>(RegistryRoot root, RegistryViewKind view, string path, Func<RegistryKey, T> read, T fallback)
    {
        var hive = root == RegistryRoot.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        var regView = view switch
        {
            RegistryViewKind.Registry64 => RegistryView.Registry64,
            RegistryViewKind.Registry32 => RegistryView.Registry32,
            _ => RegistryView.Default,
        };

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, regView);
            // writable: false — BrowserFlip never modifies the registry.
            using var key = baseKey.OpenSubKey(path, writable: false);
            return key is null ? fallback : read(key);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return fallback;
        }
    }
}
