using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using BrowserFlip.UI.Diagnostics;
using BrowserFlip.UI.Localization;
using BrowserFlip.UI.Platform;
using BrowserFlip.UI.ViewModels;
using BrowserFlip.UI.Views;

namespace BrowserFlip.UI;

/// <summary>
/// The shared Avalonia application. The host (per-platform entry point) supplies the composed
/// view model, so this class has no platform-specific code at all.
/// </summary>
public partial class App : Application
{
    /// <summary>Set by the platform entry point before <c>Run</c>.</summary>
    public static Func<MainViewModel>? ViewModelFactory { get; set; }

    /// <summary>Supplied by the entry point so the self-test can report on the real adapters.</summary>
    public static Func<(Platform.IDefaultBrowserSwitcher Switcher, Platform.IAppIconProvider Icons, Platform.ISettingsStore Store, Platform.IPlatformCapabilities Capabilities)>? AdapterAccessor { get; set; }

    private MainViewModel? _model;
    private FloatingToolbar? _toolbar;
    private bool _trayIconCreated;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _model = (ViewModelFactory ?? throw new InvalidOperationException(
                "App.ViewModelFactory must be set by the platform entry point."))();
            _model.Initialize();

            desktop.MainWindow = new MainWindow { DataContext = _model };

            CreateApplicationMenu(desktop);
            CreateTrayIcon(desktop);
            ApplyToolbarVisibility();
            _model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ShowFloatingToolbar))
                {
                    ApplyToolbarVisibility();
                }
            };

            if (SelfTest.OutputDirectory is not null)
            {
                ScheduleSelfTest(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Creates the status-bar item. On macOS Avalonia maps <see cref="TrayIcon"/> onto
    /// <c>NSStatusItem</c>, so this is the native menu-bar icon; on Windows it is the
    /// notification-area icon and on Linux the StatusNotifierItem.
    /// </summary>
    /// <summary>
    /// Supplied by the entry point: what this platform and session can actually do, so the UI
    /// can disable what is unsupported instead of failing silently.
    /// </summary>
    public static Platform.IPlatformCapabilities? Capabilities { get; set; }

    private SettingsWindow? _settings;

    /// <summary>
    /// Builds the application menu. On macOS Avalonia puts this in the system menu bar at the
    /// top of the screen, which is where a Mac user expects it; on Windows and Linux the same
    /// definition becomes the window's own menu. The structure mirrors the WPF product's.
    /// </summary>
    private void CreateApplicationMenu(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var refresh = new NativeMenuItem(Strings.Get("MenuBar_RefreshBrowsers").Replace("_", string.Empty, StringComparison.Ordinal));
        refresh.Click += (_, _) => _model?.Refresh();

        var settings = new NativeMenuItem(Strings.Get("Menu_Settings"));
        settings.Click += (_, _) => ShowSettings(desktop);

        var toolbar = new NativeMenuItem(Strings.Get("Menu_ShowToolbar"));
        toolbar.Click += (_, _) =>
        {
            if (_model is not null && Capabilities is null or { WindowPositioning.IsAvailable: true })
            {
                _model.ShowFloatingToolbar = !_model.ShowFloatingToolbar;
            }
        };

        var file = new NativeMenuItem("File") { Menu = [refresh] };
        var view = new NativeMenuItem("View") { Menu = [toolbar] };
        var tools = new NativeMenuItem("Settings") { Menu = [settings] };

        NativeMenu.SetMenu(this, [file, view, tools]);
    }

    /// <summary>Opens the single settings window, or focuses it when it is already open.</summary>
    private void ShowSettings(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_model is null)
        {
            return;
        }

        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow
        {
            DataContext = new SettingsViewModel(_model, Capabilities ?? new FullCapabilities("Unknown", osConfirms: true)),
        };
        _settings.Closed += (_, _) => _settings = null;

        if (desktop.MainWindow is not null)
        {
            _settings.Show(desktop.MainWindow);
        }
        else
        {
            _settings.Show();
        }
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Where the platform says there is no usable status area, do not create one: a stray
        // invisible item is worse than honestly having none.
        if (Capabilities is { TrayIcon.IsAvailable: false })
        {
            return;
        }

        var show = new NativeMenuItem(Strings.Get("AppTitle"));
        show.Click += (_, _) => ShowMainWindow(desktop);

        var refresh = new NativeMenuItem(Strings.Get("Refresh"));
        refresh.Click += (_, _) => _model?.Refresh();

        var trayToolbar = new NativeMenuItem(Strings.Get("Menu_ShowToolbar"));
        trayToolbar.Click += (_, _) =>
        {
            if (_model is not null)
            {
                _model.ShowFloatingToolbar = !_model.ShowFloatingToolbar;
            }
        };

        var traySettings = new NativeMenuItem(Strings.Get("Menu_Settings"));
        traySettings.Click += (_, _) => ShowSettings(desktop);

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(show);
        menu.Items.Add(refresh);
        menu.Items.Add(trayToolbar);
        menu.Items.Add(traySettings);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        // Avalonia owns and disposes the TrayIcon once it is registered with SetIcons,
        // so the application does not keep a disposable field of its own.
        var trayIcon = new TrayIcon
        {
            ToolTipText = Strings.Get("AppTitle"),
            Menu = menu,
            IsVisible = true,
            // Without this the StatusNotifierItem goes onto the bus with IconName="" and a 0x0
            // pixmap, and a shell draws a blank placeholder. Measured over D-Bus on a live
            // Wayland session before this was set.
            Icon = LoadTrayIcon(),
        };

        TrayIcon.SetIcons(this, [trayIcon]);
        _trayIconCreated = true;
    }

    /// <summary>
    /// Runs the capability self-test once the window is really on screen, then exits. The window,
    /// the status item and the platform adapters are the production ones: nothing is stubbed.
    /// </summary>
    private void ScheduleSelfTest(IClassicDesktopStyleApplicationLifetime desktop)
    {
        DispatcherTimer.RunOnce(
            async () =>
            {
                var exitCode = 1;
                var adapters = (AdapterAccessor ?? throw new InvalidOperationException(
                    "App.AdapterAccessor must be set when running the self-test."))();

                // Snapshot the user's real settings BEFORE the self-test changes anything, and
                // put them back at the end. Running the diagnostics must not reconfigure the app.
                var settingsBefore = adapters.Store.Load();
                try
                {
                    // The toolbar is part of what the self-test inspects, so make sure it is up.
                    if (_model is not null)
                    {
                        _model.ShowFloatingToolbar = true;
                        ApplyToolbarVisibility();
                    }

                    var report = await SelfTest.RunAsync(
                        _model!, desktop.MainWindow!, _toolbar, _trayIconCreated,
                        adapters.Switcher, adapters.Icons, adapters.Store, adapters.Capabilities).ConfigureAwait(true);

                    var directory = SelfTest.OutputDirectory!;
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(Path.Combine(directory, "capability-report.json"), report.ToJson())
                        .ConfigureAwait(true);
                    var table = report.ToTable();
                    await File.WriteAllTextAsync(Path.Combine(directory, "capability-report.txt"), table)
                        .ConfigureAwait(true);
                    Console.WriteLine(table);
                    exitCode = report.Checks.Any(c => c.Result == CheckResult.FAIL) ? 2 : 0;
                }
#pragma warning disable CA1031 // The self-test must report rather than crash.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    Console.Error.WriteLine("self-test failed: " + ex);
                }

                // Close the toolbar first: its Closed handler persists a position, and that
                // write has to happen before the settings are restored.
                _toolbar?.Close();
                _toolbar = null;
                adapters.Store.Save(settingsBefore);

                desktop.Shutdown(exitCode);
            },
            TimeSpan.FromSeconds(2.5));
    }

    /// <summary>The tray icon bitmap, or <c>null</c> when the asset cannot be loaded.</summary>
    private static WindowIcon? LoadTrayIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://BrowserFlip.UI/Assets/trayicon.png"));
            return new WindowIcon(stream);
        }
#pragma warning disable CA1031 // A missing icon must not stop the tray item being created.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is null)
        {
            return;
        }

        desktop.MainWindow.Show();
        desktop.MainWindow.Activate();
    }

    private void ApplyToolbarVisibility()
    {
        if (_model is null)
        {
            return;
        }

        // Capabilities gate this too: restoring a saved "toolbar on" setting onto Wayland would
        // open a window the compositor then places wherever it likes, which is exactly the
        // silent failure the capability model exists to prevent.
        if (!_model.ShowFloatingToolbar || Capabilities is { WindowPositioning.IsAvailable: false })
        {
            _toolbar?.Hide();
            return;
        }

        _toolbar ??= new FloatingToolbar { DataContext = _model };
        _toolbar.Show();
        _toolbar.RestorePosition();
    }
}
