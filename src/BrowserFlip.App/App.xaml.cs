using System.Windows;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Desktop.Win32;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;
using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using BrowserFlip.Core.Settings;

namespace BrowserFlip.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "WPF Application lifetime: disposed in OnExit.")]
public partial class App : Application
{
    private DesktopCoordinator? _coordinator;
    private MainViewModel? _main;
    private ShellViewModel? _shell;
    private Win32MonitorService? _monitors;
    private SingleInstance? _instance;

    /// <summary>Menu bar drop-down opened (template in App.xaml): small rounded corners on Windows 11.</summary>
    private void OnMenuPopupOpened(object? sender, EventArgs e) => Controls.MenuPopup.RoundCorners(sender, e);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // "--settings <path>" uses an alternative settings file (testing / future portable mode).
        var index = Array.FindIndex(e.Args, a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase));
        var settingsPath = index >= 0 && index + 1 < e.Args.Length ? System.IO.Path.GetFullPath(e.Args[index + 1]) : null;

        // One process per settings file: a second start brings the running instance forward and exits.
        _instance = SingleInstance.TryAcquire(settingsPath ?? UserSettings.DefaultFilePath);
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        var settings = UserSettings.Load(settingsPath);
        Loc.Instance.SetLanguage(settings.Language);
        ThemeManager.Initialize(this, settings.Theme);

        // Composition root: real Windows implementations of the Core abstractions.
        var registry = new WindowsRegistryReader();
        var policy = new PolicyInspector(registry);
        _main = new MainViewModel(
            new BrowserDetector(registry, new PhysicalFileSystem(), new ShellIndirectStringResolver()),
            new DefaultBrowserService(new ShellAssociationQuery()),
            new SettingsLauncher(new ShellUriLauncher(), new EnvironmentOsInfo(registry), policy),
            policy,
            b => IconLoader.Load(b.IconPath, b.IconIndex, b.ExecutablePath),
            settings);
        _shell = new ShellViewModel(settings);
        _monitors = new Win32MonitorService();

        _coordinator = new DesktopCoordinator(this, _main, _shell, _monitors, new Win32WindowPlacement(), () => new Win32TrayIcon());
        MainWindow = _coordinator.MainWindow;
        _instance.ShowRequested += (_, _) => Dispatcher.BeginInvoke(() => _coordinator?.ShowRunningInstance());
        _main.Refresh();
        _coordinator.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Release the tray icon, timers and static event subscriptions deterministically.
        _coordinator?.Dispose();
        _monitors?.Dispose();
        _shell?.Dispose();
        _main?.Dispose();
        ThemeManager.Shutdown();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
