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

    /// <summary>
    /// The window menu's live items, kept so a language change can re-read their labels. Empty on
    /// macOS, which gets <see cref="_macApplicationMenu"/> instead. Without this, switching
    /// language on Windows or Linux left File / View / Settings in the old language beside a
    /// status-area menu that had updated.
    /// </summary>
    private readonly List<(NativeMenuItem Header, NativeMenuItem Child)> _applicationMenuItems = [];

    /// <summary>
    /// The macOS application menu - the one under the product's name at the left of the system
    /// menu bar. Null off macOS, where there is no such thing. See
    /// <see cref="Menus.MacApplicationMenu"/>.
    /// </summary>
    private Menus.MacApplicationMenuDefinition? _macApplicationMenu;

    /// <summary>
    /// Its items with the resource key each label comes from: BrowserFlip's own two, plus the
    /// standard macOS items Avalonia appends and this application relabels.
    /// </summary>
    private readonly List<(NativeMenuItem Item, string Key)> _macApplicationMenuItems = [];

    public override void Initialize()
    {
        // The product name, not Avalonia's default. Left unset, Avalonia reports itself as
        // "Avalonia Application", and macOS then uses that everywhere the application names
        // itself: the menu-bar application menu read "Hide Avalonia Application", and the
        // crash reporter dialog said "Avalonia Application quit unexpectedly" - measured.
        // The product is called BrowserFlip, so it has to say BrowserFlip.
        Name = "BrowserFlip";
        AvaloniaXamlLoader.Load(this);

        // The macOS application menu has to be attached HERE, from Initialize, and nowhere later.
        // AppBuilder.SetupUnsafe runs Initialize() and only AFTERWARDS the AfterSetup callback in
        // which Avalonia's macOS backend exports the application menu; that export reads
        // NativeMenu.GetMenu(Application.Current) exactly once and binds the native menu to
        // whatever it finds. Attaching from OnFrameworkInitializationCompleted - where the
        // previous round attached it - is one step too late: the backend found nothing, installed
        // its own "About Avalonia" default and ignored everything afterwards. That is why the
        // Owner kept seeing Avalonia's menu, and why the earlier conclusion that this could not
        // be done at all was wrong.
        //
        // Not off macOS: Windows and Linux have no application-level native menu, and an
        // Application-attached NativeMenu there is read by nothing. Theirs is built below.
        if (OperatingSystem.IsMacOS())
        {
            _macApplicationMenu = Menus.MacApplicationMenu.Create(
                showAbout: ShowAbout,
                showSettings: ShowSettingsFromApplicationMenu,
                applicationName: Name);
            _macApplicationMenuItems.AddRange(_macApplicationMenu.Items);
            NativeMenu.SetMenu(this, _macApplicationMenu.Menu);
        }
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

            if (_macApplicationMenu is not null)
            {
                // By now Avalonia has exported the menu attached in Initialize and appended the
                // standard macOS items to it, so they exist and can be taken over. This runs
                // before the relabel below, because they are recognised by the English header
                // Avalonia gave them and the relabel is what replaces it.
                _macApplicationMenuItems.AddRange(
                    Menus.MacApplicationMenu.Adopt(_macApplicationMenu.Menu, Name ?? "BrowserFlip"));
            }
            else
            {
                CreateApplicationMenu(desktop);
            }

            // Once, now, because the menu above was built before the stored language was loaded:
            // _model.Initialize() applies it, and it does so before the subscription below
            // exists, so nothing else would re-read these labels until the user changed language
            // by hand. Without this a Japanese user's menu bar opened in English.
            RelabelApplicationMenu();

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
                    RelabelApplicationMenu();
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

                        _ = ShowSettingsForAcceptanceAsync();
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

    private AboutWindow? _about;

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
            var header = new NativeMenuItem(parent.Label) { Menu = [item] };
            top.Add(header);
            _applicationMenuItems.Add((header, item));
        }

        NativeMenu.SetMenu(this, [.. top]);
    }

    /// <summary>
    /// The macOS application menu's settings item. It is built in <see cref="Initialize"/>,
    /// before the lifetime has a main window, so the lifetime is resolved when the item is
    /// chosen rather than captured when it is built.
    /// </summary>
    private void ShowSettingsFromApplicationMenu()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ShowSettings(desktop);
        }
    }

    /// <summary>
    /// Opens the single About window, or focuses it when it is already open. Reached from the
    /// macOS application menu, where it replaces Avalonia's "About Avalonia".
    /// </summary>
    private void ShowAbout()
    {
        if (_model is null)
        {
            return;
        }

        if (_about is not null)
        {
            _about.Activate();
            return;
        }

        var window = new AboutWindow { DataContext = new AboutViewModel(_model) };
        window.Closed += (_, _) => _about = null;

        // Same two hazards as the settings window, and the same answer: own it only to a main
        // window that is still open, and activate it, because the application menu does not
        // bring the application forward by itself.
        var owner = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is { IsVisible: true })
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        _about = window;
        _about.Activate();
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

        var window = new SettingsWindow
        {
            DataContext = new SettingsViewModel(_model, Capabilities ?? new FullCapabilities("Unknown", osConfirms: true)),
        };
        window.Closed += (_, _) => _settings = null;

        // Owned only by a main window that is still open. Avalonia throws "Cannot show a window
        // with a closed owner", and with the floating toolbar up the application outlives its
        // main window being closed while desktop.MainWindow still points at it. The field is
        // assigned only after the window is really showing: assigning first left it pointing at
        // a window that had never been shown, and every later attempt just activated that
        // invisible window, so "Settings..." was dead for the rest of the session.
        var owner = desktop.MainWindow is { IsVisible: true } visible ? visible : null;
        if (owner is not null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        _settings = window;

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

    /// <summary>Re-reads the application menu's labels. See <see cref="_applicationMenuItems"/>.</summary>
    private void RelabelApplicationMenu()
    {
        if (_macApplicationMenuItems.Count > 0)
        {
            Menus.MacApplicationMenu.Relabel(_macApplicationMenuItems, Name ?? "BrowserFlip");
            return;
        }

        if (_applicationMenuItems.Count == 0)
        {
            return;
        }

        var definition = Menus.StatusAreaMenu.BuildApplicationMenu(() => { }, () => { }, () => { });
        var count = Math.Min(_applicationMenuItems.Count, definition.Count);
        for (var i = 0; i < count; i++)
        {
            _applicationMenuItems[i].Header.Header = definition[i].Parent.Label;
            _applicationMenuItems[i].Child.Header = definition[i].Child.Label;
        }
    }

    /// <summary>
    /// Runs the acceptance routine and reports a failure instead of swallowing it. It is started
    /// without being awaited, so without this an exception inside it would fault the task
    /// silently: the output simply stopped at whatever line it had reached.
    /// </summary>
    private async Task ShowSettingsForAcceptanceAsync()
    {
        try
        {
            Console.WriteLine("the application menu, read back from the live macOS menu bar:");
            await SelfTest.ReportApplicationMenuAsync(StatusAreaProbe, _model!).ConfigureAwait(true);
            await SelfTest.ShowSettingsThroughTheMenuAsync(_model!, _trayMenu, _trayEntries).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // It has to be reported, whatever it is.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine("the acceptance run failed: " + ex);
        }
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
