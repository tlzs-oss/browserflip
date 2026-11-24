using Avalonia;
using BrowserFlip.Core.Defaults;
using BrowserFlip.UI;
using BrowserFlip.UI.Diagnostics;
using BrowserFlip.UI.Platform;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.Prototype.Desktop;

/// <summary>
/// The composition root: the one place that knows which OS it is running on. Everything above it
/// (view models, views, Core) is platform-neutral.
/// </summary>
internal static class Program
{
    [STAThread]
    internal static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // --selftest <dir>            run the R2 capability checks, write the report, exit.
        // --switch-default <bundle-id>  additionally perform a REAL default-browser change.
        //                             Owner-triggered only; macOS still shows its own prompt.
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--selftest", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                SelfTest.OutputDirectory = args[i + 1];
            }
            else if (string.Equals(args[i], "--switch-default", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                SelfTest.SwitchTargetId = args[i + 1];
            }
        }

        App.ViewModelFactory = Compose;
        App.AdapterAccessor = () => (_switcher!, _icons!, _store!, _capabilities!);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static IDefaultBrowserSwitcher? _switcher;
    private static IAppIconProvider? _icons;
    private static ISettingsStore? _store;
    private static IPlatformCapabilities? _capabilities;

    internal static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>Selects the platform adapters for the current OS.</summary>
    private static MainViewModel Compose()
    {
        var store = new JsonSettingsStore();
        _store = store;

        if (OperatingSystem.IsMacOS())
        {
            var query = new BrowserFlip.Platform.MacOS.MacAssociationQuery();
            _switcher = new BrowserFlip.Platform.MacOS.MacDefaultBrowserSwitcher();
            _icons = new BrowserFlip.Platform.MacOS.MacAppIconProvider();
            // macOS imposes none of the restrictions Linux does, and the OS owns the consent prompt.
            _capabilities = new FullCapabilities($"macOS {Environment.OSVersion.Version}", osConfirms: true);
            App.Capabilities = _capabilities;
            return new MainViewModel(
                new BrowserFlip.Platform.MacOS.MacBrowserCatalog(),
                _switcher,
                _icons,
                store,
                new DefaultBrowserService(query));
        }

        // No adapter for this platform yet: start, and say so honestly rather than crash or
        // pretend an empty list means "no browsers installed".
        _switcher = new UnsupportedSwitcher();
        _icons = new NoIconProvider();
        _capabilities = new NoAdapterCapabilities();
        App.Capabilities = _capabilities;
        return new MainViewModel(
            new EmptyBrowserCatalog(),
            _switcher,
            _icons,
            store,
            new DefaultBrowserService(new NoAssociations()));
    }

    private sealed class EmptyBrowserCatalog : IBrowserCatalog
    {
        public IReadOnlyList<Core.Detection.BrowserInfo> GetBrowsers() => [];
    }

    private sealed class UnsupportedSwitcher : IDefaultBrowserSwitcher
    {
        public SwitchMechanism Mechanism => SwitchMechanism.Unsupported;

        public Task<SwitchResult> RequestSwitchAsync(Core.Detection.BrowserInfo browser, CancellationToken cancellationToken = default)
            => Task.FromResult(new SwitchResult(SwitchOutcome.Unsupported, "This prototype round implements macOS only."));
    }

    private sealed class NoIconProvider : IAppIconProvider
    {
        public byte[]? GetIconPng(string? applicationPath, int pixelSize = 64) => null;
    }

    private sealed class NoAssociations : Core.Platform.IAssociationQuery
    {
        public Core.Platform.AssociationInfo? QueryProtocol(string protocol) => null;
    }

    /// <summary>Capabilities for a platform that has no adapter yet. Everything unsupported.</summary>
    private sealed class NoAdapterCapabilities : PlatformCapabilitiesBase
    {
        private const string Reason = "no BrowserFlip platform adapter is implemented for this OS yet";

        public override string Description =>
            $"{(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Unknown")} (no adapter)";

        public override Capability AlwaysOnTop => Capability.Available;

        public override Capability WindowPositioning => Capability.Available;

        public override Capability TrayIcon => Capability.Available;

        public override Capability DefaultBrowserSwitch => Capability.Unsupported(Reason);

        public override bool OsConfirmsDefaultBrowserChange => true;
    }
}
