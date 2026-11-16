using System.Windows;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;
using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;
using BrowserFlip.Core.Platform;
using BrowserFlip.Core.Settings;

namespace BrowserFlip.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = UserSettings.Load();
        Loc.Instance.SetLanguage(settings.Language);
        ThemeManager.Initialize(this);

        // Composition root: real Windows implementations of the Core abstractions.
        var registry = new WindowsRegistryReader();
        var policy = new PolicyInspector(registry);
        var viewModel = new MainViewModel(
            new BrowserDetector(registry, new PhysicalFileSystem(), new ShellIndirectStringResolver()),
            new DefaultBrowserService(new ShellAssociationQuery()),
            new SettingsLauncher(new ShellUriLauncher(), new EnvironmentOsInfo(registry), policy),
            policy,
            b => IconLoader.Load(b.IconPath, b.IconIndex, b.ExecutablePath),
            settings);

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        viewModel.Refresh();
        window.Show();
    }
}
