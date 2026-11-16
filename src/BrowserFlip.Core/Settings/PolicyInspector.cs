using BrowserFlip.Core.Platform;

namespace BrowserFlip.Core.Settings;

/// <summary>Group Policy / MDM restrictions relevant to changing the default browser.</summary>
public sealed record PolicyStatus(
    bool SettingsAppBlocked,
    bool DefaultAppsPageHidden,
    bool AssociationsManaged)
{
    public static PolicyStatus None { get; } = new(false, false, false);

    /// <summary>True when the user cannot reach the Default apps page at all.</summary>
    public bool BlocksNavigation => SettingsAppBlocked || DefaultAppsPageHidden;
}

/// <summary>Reads (never writes) policy values that affect default-app changes.</summary>
public sealed class PolicyInspector
{
    internal const string ExplorerPolicies = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    internal const string SystemPolicies = @"SOFTWARE\Policies\Microsoft\Windows\System";

    private readonly IRegistryReader _registry;

    public PolicyInspector(IRegistryReader registry) => _registry = registry;

    public PolicyStatus Inspect()
    {
        var noControlPanel = IsSet(RegistryRoot.CurrentUser, ExplorerPolicies, "NoControlPanel")
                             || IsSet(RegistryRoot.LocalMachine, ExplorerPolicies, "NoControlPanel");

        var visibility = _registry.GetString(RegistryRoot.LocalMachine, RegistryViewKind.Registry64, ExplorerPolicies, "SettingsPageVisibility")
                         ?? _registry.GetString(RegistryRoot.CurrentUser, RegistryViewKind.Default, ExplorerPolicies, "SettingsPageVisibility");

        var managed = !string.IsNullOrWhiteSpace(
            _registry.GetString(RegistryRoot.LocalMachine, RegistryViewKind.Registry64, SystemPolicies, "DefaultAssociationsConfiguration"));

        return new PolicyStatus(noControlPanel, IsDefaultAppsHidden(visibility), managed);
    }

    /// <summary>
    /// Interprets the <c>SettingsPageVisibility</c> policy (<c>hide:a;b</c> or <c>showonly:a;b</c>).
    /// </summary>
    internal static bool IsDefaultAppsHidden(string? visibility)
    {
        if (string.IsNullOrWhiteSpace(visibility))
        {
            return false;
        }

        var v = visibility.Trim();
        var colon = v.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return false;
        }

        var mode = v[..colon].Trim();
        var pages = v[(colon + 1)..]
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var listed = pages.Any(p => string.Equals(p, "defaultapps", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(mode, "hide", StringComparison.OrdinalIgnoreCase))
        {
            return listed;
        }

        if (string.Equals(mode, "showonly", StringComparison.OrdinalIgnoreCase))
        {
            return !listed;
        }

        return false;
    }

    private bool IsSet(RegistryRoot root, string path, string name)
    {
        var view = root == RegistryRoot.LocalMachine ? RegistryViewKind.Registry64 : RegistryViewKind.Default;
        return _registry.GetInt32(root, view, path, name) is int v && v != 0;
    }
}
