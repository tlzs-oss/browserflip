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

    /// <summary>
    /// Supplied by the entry point where the platform can read its own status area back. Null
    /// means "not measurable here", which the report says instead of guessing.
    /// </summary>
    public static Platform.IStatusAreaProbe? StatusAreaProbe { get; set; }

    private MainViewModel? _model;
    private FloatingToolbar? _toolbar;
    private bool _trayIconCreated;
    private NativeMenu? _trayMenu;
    private string? _trayToolTip;
    private IReadOnlyList<Menus.StatusMenuEntry>? _trayEntries;

    public override void Initialize()
    {
        // The product name, not Avalonia's default. Left unset, Avalonia reports itself as
        // "Avalonia Application", and macOS then uses that everywhere the application names
        // itself: the menu-bar application menu read "Hide Avalonia Application", and the
        // crash reporter dialog said "Avalonia Application quit unexpectedly" - measured.
        // The product is called BrowserFlip, so it has to say BrowserFlip.
        Name = "BrowserFlip";
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The window is created BEFORE the view model on purpose. The Linux capability
            // model asks the live window which Avalonia backend implemented it - X11 or Wayland
            // - instead of guessing from XDG_SESSION_TYPE, and that answer only exists once a
            // window object exists. Window's constructor creates its platform implementation, so
            // the backend is known here even though the window has not been shown yet.
            var mainWindow = new MainWindow();
            mainWindow.SettingsRequested += () => ShowSettings(desktop);
            desktop.MainWindow = mainWindow;

            _model = (ViewModelFactory ?? throw new InvalidOperationException(
                "App.ViewModelFactory must be set by the platform entry point."))();
            _model.Initialize();
            mainWindow.DataContext = _model;

            // Not on macOS. Measured on macOS 27 with Avalonia 12.1.4: Avalonia exports the
            // application menu from its windowing-subsystem initialiser, which AppBuilder runs
            // BEFORE the Application object exists, so its exporter reads Application.Current as
            // null, installs its own default menu ("About Avalonia", "Hide", "Quit") and binds
            // its native menu to that one for good. A later NativeMenu.SetMenu(Application, ...)
            // is therefore silently dropped - the menu bar still showed Avalonia's defaults, with
            // no File/View/Settings - and replacing an exported menu is the upstream crash of
            // AvaloniaUI/Avalonia#22287. Calling it anyway would only pretend to have a menu bar.
            // Everything in it is reachable from the status-area menu below.
            if (!OperatingSystem.IsMacOS())
            {
                CreateApplicationMenu(desktop);
            }
            CreateTrayIcon(desktop);
            ApplyToolbarVisibility();
            _model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ShowFloatingToolbar))
                {
                    ApplyToolbarVisibility();
                }
                else if (e.PropertyName == nameof(MainViewModel.Language))
                {
                    RelabelTrayMenu();
                }
            };

            if (SelfTest.ShowSettingsRequested)
            {
                DispatcherTimer.RunOnce(
                    () =>
                    {
                        if (SelfTest.ShowSettingsLanguage is { } language && _model is not null)
                        {
                            Console.WriteLine($"switching the UI language to \"{language}\" ...");
                            _model.Language = language;
                        }

                        _ = SelfTest.ShowSettingsThroughTheMenuAsync(_model!, _trayMenu, _trayEntries);
                    },
                    TimeSpan.FromSeconds(2));
            }

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
        var top = new List<NativeMenuItem>();
        foreach (var (parent, child) in Menus.StatusAreaMenu.BuildApplicationMenu(
                     refresh: () => _model?.Refresh(),
                     toggleToolbar: () =>
                     {
                         if (_model is not null && Capabilities is null or { WindowPositioning.IsAvailable: true })
                         {
                             _model.ShowFloatingToolbar = !_model.ShowFloatingToolbar;
                         }
                     },
                     showSettings: () => ShowSettings(desktop)))
        {
            var item = new NativeMenuItem(child.Label);
            var invoke = child.Invoke!;
            item.Click += (_, _) => invoke();
            top.Add(new NativeMenuItem(parent.Label) { Menu = [item] });
        }

        NativeMenu.SetMenu(this, [.. top]);
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

        // Showing is not enough. The item is chosen from the status-area menu, which does not
        // bring the application forward, so the window appeared behind whatever the user was
        // looking at - measured as visible=True active=False - and choosing "Settings..." looked
        // like it did nothing. Re-opening an already-open window did activate it; the first open
        // did not. The Owner failed to find the settings screen twice because of this.
        _settings.Activate();
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Where the platform says there is no usable status area, do not create one: a stray
        // invisible item is worse than honestly having none.
        if (Capabilities is { TrayIcon.IsAvailable: false })
        {
            return;
        }

        // One definition, in BrowserFlip.UI.Menus.StatusAreaMenu, so the labels and the
        // handlers are testable without a display and cannot drift from what is shown.
        var entries = Menus.StatusAreaMenu.Build(
            showMainWindow: () => ShowMainWindow(desktop),
            refresh: () => _model?.Refresh(),
            toggleToolbar: () =>
            {
                if (_model is not null)
                {
                    _model.ShowFloatingToolbar = !_model.ShowFloatingToolbar;
                }
            },
            showSettings: () => ShowSettings(desktop),
            quit: () => desktop.Shutdown());

        _trayEntries = entries;
        var menu = Menus.StatusAreaMenu.ToNativeMenu(entries);

        // Avalonia owns and disposes the TrayIcon once it is registered with SetIcons,
        // so the application does not keep a disposable field of its own.
        // Remembered, not looked up a second time: the probe identifies the live status item by
        // this tooltip, and a fresh Strings.Get could return a different string if the UI culture
        // has moved on since the item was created - which would fail checks 3 and 13 on a
        // working product.
        _trayToolTip = Strings.Get("AppTitle");
        var trayIcon = new TrayIcon
        {
            ToolTipText = _trayToolTip,
            Menu = menu,
            IsVisible = true,
            // Without this the StatusNotifierItem goes onto the bus with IconName="" and a 0x0
            // pixmap, and a shell draws a blank placeholder. Measured over D-Bus on a live
            // Wayland session before this was set.
            Icon = LoadTrayIcon(),
        };

        TrayIcon.SetIcons(this, [trayIcon]);
        _trayIconCreated = true;
        _trayMenu = menu;
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
                CapabilityReport? finished = null;
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
                        adapters.Switcher, adapters.Icons, adapters.Store, adapters.Capabilities,
                        StatusAreaProbe, _trayMenu, _trayToolTip, _trayEntries).ConfigureAwait(true);

                    var directory = SelfTest.OutputDirectory!;
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(Path.Combine(directory, "capability-report.json"), report.ToJson())
                        .ConfigureAwait(true);
                    var table = report.ToTable();
                    await File.WriteAllTextAsync(Path.Combine(directory, "capability-report.txt"), table)
                        .ConfigureAwait(true);
                    Console.WriteLine(table);
                    exitCode = report.ExitCode;
                    finished = report;
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

                // The interaction check runs last, after the report is on disk, because a menu
                // that really opens blocks the UI thread in the platform's modal tracking loop.
                // It ends the process itself; only when there is nothing to click do we shut
                // down the ordinary way.
                if (StatusAreaProbe is not null && _trayMenu is not null
                    && finished is not null && SelfTest.OutputDirectory is not null)
                {
                    SelfTest.VerifyMenuOpensThenExit(
                        StatusAreaProbe, _trayMenu, _trayToolTip ?? string.Empty, finished, SelfTest.OutputDirectory);
                    return;
                }

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

    /// <summary>
    /// Re-reads the status-area menu's labels after a language change. They are resource lookups
    /// made when the menu was built, so without this the menu-bar menu keeps the language the
    /// application started in - measured: a Japanese UI whose menu item still read 设置….
    /// </summary>
    private void RelabelTrayMenu()
    {
        if (_trayMenu is null || _trayEntries is null || _model is null)
        {
            return;
        }

        // Built only to re-read the labels in the current language. The handlers already
        // attached to the live menu items are left alone, and _trayEntries keeps the real
        // definition - a list of no-op actions must not be allowed to replace it.
        var relabelled = Menus.StatusAreaMenu.Build(
            showMainWindow: () => { }, refresh: () => { }, toggleToolbar: () => { },
            showSettings: () => { }, quit: () => { });
        Menus.StatusAreaMenu.Relabel(_trayMenu, relabelled);
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
