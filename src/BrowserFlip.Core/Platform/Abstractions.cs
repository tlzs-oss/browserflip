namespace BrowserFlip.Core.Platform;

/// <summary>File system queries used by detection.</summary>
public interface IFileSystem
{
    bool FileExists(string path);
}

/// <summary>Resolves indirect strings such as <c>@C:\app.exe,-101</c> (SHLoadIndirectString).</summary>
public interface IIndirectStringResolver
{
    /// <summary>Returns the resolved string, or <c>null</c> when it cannot be resolved.</summary>
    string? Resolve(string indirect);
}

/// <summary>Information about the running Windows version.</summary>
public interface IOsInfo
{
    /// <summary>Windows build number, e.g. 22631 for Windows 11 23H2.</summary>
    int BuildNumber { get; }

    /// <summary>Update Build Revision (UBR), e.g. 1555 in 10.0.22621.1555. 0 when unknown.</summary>
    int UpdateBuildRevision { get; }
}

/// <summary>Launches a URI (for example <c>ms-settings:</c>) through the Windows shell.</summary>
public interface IUriLauncher
{
    /// <summary>Launches the URI. Throws when the shell cannot launch it.</summary>
    void Launch(Uri uri);
}

/// <summary>The resolved default handler for a URL protocol.</summary>
/// <param name="ProgId">The ProgID Windows currently uses (e.g. <c>ChromeHTML</c>).</param>
/// <param name="FriendlyAppName">Friendly application name reported by Windows, if any.</param>
/// <param name="ExecutablePath">Executable path reported by Windows, if any.</param>
public sealed record AssociationInfo(string ProgId, string? FriendlyAppName, string? ExecutablePath);

/// <summary>Queries the effective default handler for a URL protocol (read-only).</summary>
public interface IAssociationQuery
{
    /// <summary>Returns the default handler for <paramref name="protocol"/>, or <c>null</c> when none is set.</summary>
    AssociationInfo? QueryProtocol(string protocol);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
}

public sealed class EnvironmentOsInfo(IRegistryReader registry) : IOsInfo
{
    public int BuildNumber => Environment.OSVersion.Version.Build;

    public int UpdateBuildRevision
        => registry.GetInt32(RegistryRoot.LocalMachine, RegistryViewKind.Registry64, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR") ?? 0;
}
