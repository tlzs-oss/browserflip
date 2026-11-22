using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BrowserFlip.UI.Diagnostics;
using BrowserFlip.UI.Localization;
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
    public static Func<(Platform.IDefaultBrowserSwitcher Switcher, Platform.IAppIconProvider Icons, Platform.ISettingsStore Store)>? AdapterAccessor { get; set; }

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
    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var show = new NativeMenuItem(Strings.Get("AppTitle"));
        show.Click += (_, _) => ShowMainWindow(desktop);

        var refresh = new NativeMenuItem(Strings.Get("Refresh"));
        refresh.Click += (_, _) => _model?.Refresh();

        var toolbar = new NativeMenuItem(Strings.Get("Menu_ShowToolbar"));
        toolbar.Click += (_, _) =>
        {
            if (_model is not null)
            {
                _model.ShowFloatingToolbar = !_model.ShowFloatingToolbar;
            }
        };

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(show);
        menu.Items.Add(refresh);
        menu.Items.Add(toolbar);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        // Avalonia owns and disposes the TrayIcon once it is registered with SetIcons,
        // so the application does not keep a disposable field of its own.
        var trayIcon = new TrayIcon
        {
            ToolTipText = Strings.Get("AppTitle"),
            Menu = menu,
            IsVisible = true,
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
                try
                {
                    // The toolbar is part of what the self-test inspects, so make sure it is up.
                    if (_model is not null)
                    {
                        _model.ShowFloatingToolbar = true;
                        ApplyToolbarVisibility();
                    }

                    var adapters = (AdapterAccessor ?? throw new InvalidOperationException(
                        "App.AdapterAccessor must be set when running the self-test."))();

                    var report = await SelfTest.RunAsync(
                        _model!, desktop.MainWindow!, _toolbar, _trayIconCreated,
                        adapters.Switcher, adapters.Icons, adapters.Store).ConfigureAwait(true);

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

                desktop.Shutdown(exitCode);
            },
            TimeSpan.FromSeconds(2.5));
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

        if (!_model.ShowFloatingToolbar)
        {
            _toolbar?.Hide();
            return;
        }

        _toolbar ??= new FloatingToolbar { DataContext = _model };
        _toolbar.Show();
        _toolbar.RestorePosition();
    }
}
