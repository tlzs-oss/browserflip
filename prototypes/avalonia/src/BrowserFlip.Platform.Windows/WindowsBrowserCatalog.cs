using System.Runtime.Versioning;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Windows;

/// <summary>
/// Windows implementation of <see cref="IBrowserCatalog"/>. It is a thin adapter over
/// <see cref="BrowserDetector"/>: the registry detection model (StartMenuInternet +
/// RegisteredApplications, HKCU before HKLM, 64- and 32-bit views, indirect display strings,
/// health classification) already ships in <c>BrowserFlip.Core</c> and is deliberately NOT
/// repeated here, so the Avalonia UI and the WPF product see byte-identical
/// <see cref="BrowserInfo"/> records.
/// </summary>
/// <remarks>
/// The composition mirrors <c>BrowserFlip.App.App.OnStartup</c>:
/// <c>new BrowserDetector(new WindowsRegistryReader(), new PhysicalFileSystem(), new ShellIndirectStringResolver())</c>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsBrowserCatalog : IBrowserCatalog
{
    private readonly BrowserDetector _detector;

    public WindowsBrowserCatalog()
        : this(new BrowserDetector(new WindowsRegistryReader(), new PhysicalFileSystem(), new ShellIndirectStringResolver()))
    {
    }

    /// <summary>
    /// Injects the detector so tests can drive the very same Core detection code over an
    /// in-memory registry instead of the machine's own.
    /// </summary>
    internal WindowsBrowserCatalog(BrowserDetector detector) => _detector = detector;

    /// <summary>
    /// Injects the Core abstractions individually, for hosts that already own a registry reader.
    /// </summary>
    internal WindowsBrowserCatalog(IRegistryReader registry, IFileSystem fileSystem, IIndirectStringResolver strings)
        : this(new BrowserDetector(registry, fileSystem, strings))
    {
    }

    public IReadOnlyList<BrowserInfo> GetBrowsers()
    {
        try
        {
            // Already sorted by display name by the detector; no re-ordering here, so the two
            // Windows front-ends cannot drift apart in list order either.
            return _detector.Detect();
        }
#pragma warning disable CA1031 // The contract is "never throws": an unreadable hive must yield an empty list, not a crash.
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }
    }
}
