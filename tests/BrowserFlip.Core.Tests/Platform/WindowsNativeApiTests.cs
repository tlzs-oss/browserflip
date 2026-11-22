using BrowserFlip.Core.Platform;

namespace BrowserFlip.Core.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that is reported as SKIPPED instead of failing when the test host
/// is not Windows. These tests call real Windows APIs (shlwapi.dll, the registry); running them on
/// macOS or Linux would only prove that the P/Invoke target is missing.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only: exercises a native Windows API (UNSUPPORTED on this OS).";
        }
    }
}

/// <summary>
/// Smoke tests for the Windows implementations in <c>BrowserFlip.Core.Platform</c>. They are
/// compiled only for the <c>net10.0-windows</c> target and skipped when the host is not Windows,
/// so the portable Core test run never touches native Windows APIs.
/// </summary>
public class WindowsNativeApiTests
{
    [WindowsFact]
    public void Real_association_query_returns_non_empty_progid_or_null()
    {
        // Smoke test against the real shell API: must never throw, regardless of machine configuration.
        var info = new ShellAssociationQuery().QueryProtocol("https");
        Assert.True(info is null || !string.IsNullOrWhiteSpace(info.ProgId));
        if (info is not null)
        {
            // Regression: the executable must be resolvable (needs the "open" verb for URL protocols).
            Assert.False(string.IsNullOrWhiteSpace(info.ExecutablePath));
            Assert.True(File.Exists(info.ExecutablePath), info.ExecutablePath);
        }

        Assert.Null(new ShellAssociationQuery().QueryProtocol("browserflip-nonexistent-protocol"));
    }

    [WindowsFact]
    public void Real_launcher_refuses_non_settings_uris()
        => Assert.Throws<ArgumentException>(() => new ShellUriLauncher().Launch(new Uri("https://example.com")));

    [WindowsFact]
    public void Real_registry_reader_reads_the_current_windows_build()
    {
        // Read-only probe of a key that exists on every Windows installation.
        var reader = new WindowsRegistryReader();
        var build = reader.GetString(
            RegistryRoot.LocalMachine, RegistryViewKind.Registry64,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild");
        Assert.False(string.IsNullOrWhiteSpace(build));

        // Missing keys and values must degrade to empty/null, never throw.
        Assert.Empty(reader.GetSubKeyNames(RegistryRoot.CurrentUser, RegistryViewKind.Default, @"SOFTWARE\BrowserFlip-does-not-exist"));
        Assert.Empty(reader.GetValueNames(RegistryRoot.CurrentUser, RegistryViewKind.Default, @"SOFTWARE\BrowserFlip-does-not-exist"));
        Assert.Null(reader.GetString(RegistryRoot.CurrentUser, RegistryViewKind.Default, @"SOFTWARE\BrowserFlip-does-not-exist", "x"));
        Assert.Null(reader.GetInt32(RegistryRoot.CurrentUser, RegistryViewKind.Default, @"SOFTWARE\BrowserFlip-does-not-exist", "x"));
    }

    [WindowsFact]
    public void Real_os_info_reads_the_update_build_revision()
    {
        var registry = new WindowsRegistryReader();
        var os = new EnvironmentOsInfo(registry);
        Assert.True(os.BuildNumber > 0);

        // EnvironmentOsInfo falls back to 0, so assert that the UBR value is genuinely
        // readable: a wrong key path or registry view would silently report 0 instead.
        var ubr = registry.GetInt32(
            RegistryRoot.LocalMachine, RegistryViewKind.Registry64,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
        Assert.NotNull(ubr);
        Assert.Equal(ubr.Value, os.UpdateBuildRevision);
    }
}
