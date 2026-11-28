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
            else if (string.Equals(args[i], "--settings-path", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                // Lets an acceptance or regression run use its own settings file, so verifying
                // the application never reads or writes the user's real one. The settings path is
                // resolved from the account's home directory, not from $HOME, so an environment
                // override cannot isolate it - measured: a run under `env -i HOME=<temp>` still
                // wrote the real file.
                _settingsPath = args[i + 1];
            }
            else if (string.Equals(args[i], "--show-settings", StringComparison.Ordinal))
            {
                // Opens the settings window through the real status-area menu item and keeps
                // running, so the screen can be looked at rather than a PASS line read.
                SelfTest.ShowSettingsRequested = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    SelfTest.ShowSettingsLanguage = args[i + 1];
                }
            }
        }

        App.ViewModelFactory = Compose;
        App.AdapterAccessor = () => (_switcherFactory!(false), _icons!, _store!, _capabilities!);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Set by <c>--settings-path</c>; <c>null</c> means the real per-user location.</summary>
    private static string? _settingsPath;

    private static Func<bool, IDefaultBrowserSwitcher>? _switcherFactory;
    private static IAppIconProvider? _icons;
    private static ISettingsStore? _store;
    private static IPlatformCapabilities? _capabilities;

    internal static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // UsePlatformDetect() knows nothing about Wayland: Avalonia.Desktop carries only
            // X11, Win32 and macOS. On a Wayland session with no XWayland the X11 backend
            // cannot open a display and the process aborts. UseWaylandWithFallback must come
            // AFTER UsePlatformDetect so Skia and HarfBuzz are already configured, and it
            // falls back to X11 when the compositor advertises xdg_wm_base below version 3.
            .UseWaylandWithFallback()
            .WithInterFont()
            .LogToTrace();

    /// <summary>Selects the platform adapters for the current OS.</summary>
    private static MainViewModel Compose()
    {
        var store = _settingsPath is null ? new JsonSettingsStore() : new JsonSettingsStore(_settingsPath);
        _store = store;

        if (OperatingSystem.IsMacOS())
        {
            var query = new BrowserFlip.Platform.MacOS.MacAssociationQuery();
            // macOS shows its own consent alert, so the consent flag is irrelevant here. The
            // instance is created inside the OperatingSystem.IsMacOS() guard; a lambda body
            // would be outside it as far as the platform-compatibility analyser is concerned.
            var macSwitcher = new BrowserFlip.Platform.MacOS.MacDefaultBrowserSwitcher();
            _switcherFactory = _ => macSwitcher;
            _icons = new BrowserFlip.Platform.MacOS.MacAppIconProvider();
            // macOS imposes none of the restrictions Linux does, and the OS owns the consent prompt.
            _capabilities = new FullCapabilities($"macOS {Environment.OSVersion.Version}", osConfirms: true);
            App.Capabilities = _capabilities;
            // Lets the self-test read the real NSStatusItem back out of the menu bar, and click it.
            App.StatusAreaProbe = new BrowserFlip.Platform.MacOS.MacStatusAreaProbe();
            return new MainViewModel(
                new BrowserFlip.Platform.MacOS.MacBrowserCatalog(),
                _switcherFactory,
                _icons,
                store,
                new DefaultBrowserService(query),
                _capabilities,
                Confirmation());
        }

#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            // Reuses the shipping Windows implementation in Core rather than reimplementing it,
            // so the Avalonia front-end and the WPF product cannot drift apart.
            var windowsQuery = new Core.Platform.ShellAssociationQuery();
            // Windows Settings asks the user, so the consent flag is irrelevant here too.
            _switcherFactory = _ => new BrowserFlip.Platform.Windows.WindowsDefaultBrowserSwitcher();
            _icons = new BrowserFlip.Platform.Windows.WindowsAppIconProvider();
            _capabilities = new BrowserFlip.Platform.Windows.WindowsCapabilities();
            App.Capabilities = _capabilities;
            return new MainViewModel(
                new BrowserFlip.Platform.Windows.WindowsBrowserCatalog(),
                _switcherFactory,
                _icons,
                store,
                new DefaultBrowserService(windowsQuery),
                _capabilities,
                Confirmation());
        }
#endif

        if (OperatingSystem.IsLinux())
        {
            // xdg-settings applies the change immediately with no OS prompt, unlike Windows and
            // macOS. UserHasConfirmed is `required`, so the switcher cannot be built without
            // stating whether consent was obtained - and the view model only passes true after
            // its own dialog returned yes. See LinuxDefaultBrowserSwitcher.
            _switcherFactory = confirmed => new BrowserFlip.Platform.Linux.LinuxDefaultBrowserSwitcher
            {
                UserHasConfirmed = confirmed,
            };
            _icons = new BrowserFlip.Platform.Linux.LinuxAppIconProvider();
            _capabilities = new BrowserFlip.Platform.Linux.LinuxCapabilities();
            App.Capabilities = _capabilities;
            return new MainViewModel(
                new BrowserFlip.Platform.Linux.LinuxBrowserCatalog(),
                _switcherFactory,
                _icons,
                store,
                new DefaultBrowserService(new BrowserFlip.Platform.Linux.LinuxAssociationQuery()),
                _capabilities,
                Confirmation());
        }

        // No adapter for this platform yet: start, and say so honestly rather than crash or
        // pretend an empty list means "no browsers installed".
        _switcherFactory = _ => new UnsupportedSwitcher();
        _icons = new NoIconProvider();
        _capabilities = new NoAdapterCapabilities();
        App.Capabilities = _capabilities;
        return new MainViewModel(
            new EmptyBrowserCatalog(),
            _switcherFactory,
            _icons,
            store,
            new DefaultBrowserService(new NoAssociations()),
            _capabilities,
            Confirmation());
    }

    /// <summary>
    /// How the user is asked, on a platform whose OS does not ask for us.
    /// <para>
    /// Normally that is a modal dialog bound to whatever the main window currently is. The one
    /// exception is the unattended, Owner-triggered real change
    /// (<c>--selftest &lt;dir&gt; --switch-default &lt;id&gt;</c>), where the flags are
    /// themselves the deliberate consent and nobody is present to answer a dialog.
    /// </para>
    /// <para>
    /// The self-test does NOT run without a prompt attached, as this was once documented to: the
    /// main window exists throughout it, so the dialog really was shown. Measured on Ubuntu
    /// 26.04.1 GNOME Wayland, that modal dialog had nobody to answer it and the run hung
    /// indefinitely without writing any report. Hanging is not failing closed.
    /// </para>
    /// </summary>
    private static IUserConfirmation Confirmation()
        => SelfTest.OwnerTriggeredSwitch
            ? CommandLineConsent.Instance
            : new BrowserFlip.UI.Views.DialogUserConfirmation(() =>
                (Avalonia.Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow);

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
