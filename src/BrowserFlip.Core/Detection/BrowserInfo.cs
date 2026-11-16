namespace BrowserFlip.Core.Detection;

/// <summary>Where a browser's registration was found.</summary>
public enum RegistrationScope
{
    /// <summary>Per-user registration (HKCU).</summary>
    User,

    /// <summary>Per-machine registration (HKLM).</summary>
    Machine,
}

/// <summary>Health of a browser registration.</summary>
public enum BrowserHealth
{
    /// <summary>Registration is complete and the executable exists.</summary>
    Ok,

    /// <summary>Registration exists but the executable is missing (likely uninstalled).</summary>
    ExecutableMissing,

    /// <summary>The browser is not listed in RegisteredApplications, so a per-app Settings page cannot be targeted.</summary>
    NotInRegisteredApplications,
}

/// <summary>A browser registered with Windows under <c>Software\Clients\StartMenuInternet</c>.</summary>
public sealed record BrowserInfo
{
    /// <summary>StartMenuInternet key name, e.g. <c>Google Chrome</c> or <c>Firefox-308046B0AF4A39CB</c>.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string? ExecutablePath { get; init; }

    public string? IconPath { get; init; }

    public int IconIndex { get; init; }

    /// <summary>ProgID the browser registers for <c>http</c> in Capabilities\URLAssociations.</summary>
    public string? HttpProgId { get; init; }

    /// <summary>ProgID the browser registers for <c>https</c> in Capabilities\URLAssociations.</summary>
    public string? HttpsProgId { get; init; }

    /// <summary>Value name under RegisteredApplications (used by the Settings deep link), if registered.</summary>
    public string? RegisteredApplicationName { get; init; }

    /// <summary>Hive in which <see cref="RegisteredApplicationName"/> was found.</summary>
    public RegistrationScope? RegisteredApplicationScope { get; init; }

    /// <summary>Hive in which the StartMenuInternet registration was found.</summary>
    public RegistrationScope Scope { get; init; }

    public BrowserHealth Health { get; init; }

    /// <summary>True when this browser declares <paramref name="progId"/> for http or https.</summary>
    public bool OwnsProgId(string? progId)
        => !string.IsNullOrEmpty(progId)
           && (string.Equals(progId, HttpProgId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(progId, HttpsProgId, StringComparison.OrdinalIgnoreCase));
}
