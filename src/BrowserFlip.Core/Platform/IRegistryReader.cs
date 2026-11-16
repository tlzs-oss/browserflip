namespace BrowserFlip.Core.Platform;

/// <summary>Registry root that BrowserFlip reads from. BrowserFlip never writes to the registry.</summary>
public enum RegistryRoot
{
    CurrentUser,
    LocalMachine,
}

/// <summary>Registry view (WOW64 redirection) to read.</summary>
public enum RegistryViewKind
{
    Default,
    Registry64,
    Registry32,
}

/// <summary>
/// Read-only registry abstraction. Implementations must never create or modify keys.
/// </summary>
public interface IRegistryReader
{
    /// <summary>Returns the sub-key names, or an empty list when the key does not exist or cannot be read.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, RegistryViewKind view, string path);

    /// <summary>Returns the value names, or an empty list when the key does not exist or cannot be read.</summary>
    IReadOnlyList<string> GetValueNames(RegistryRoot root, RegistryViewKind view, string path);

    /// <summary>
    /// Returns a string value (environment variables expanded), or <c>null</c> when missing / not a string.
    /// A <c>null</c> or empty <paramref name="name"/> reads the key's default value.
    /// </summary>
    string? GetString(RegistryRoot root, RegistryViewKind view, string path, string? name);

    /// <summary>Returns a DWORD value, or <c>null</c> when missing / not a DWORD.</summary>
    int? GetInt32(RegistryRoot root, RegistryViewKind view, string path, string name);
}
