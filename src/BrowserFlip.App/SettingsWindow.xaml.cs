using System.Windows;
using System.Windows.Input;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;
using BrowserFlip.App.Views;

namespace BrowserFlip.App;

/// <summary>
/// The independent Settings window (v0.3). It binds to the same <see cref="MainViewModel"/> / <see cref="ShellViewModel"/>
/// properties as the Classic view's display options, so there is one settings logic: a change applies and is saved at
/// once. Cancel puts back the values this window started with. Only one instance exists (the coordinator reuses it).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _main;
    private readonly ShellViewModel _shell;
    private readonly Snapshot _start;

    public SettingsWindow(MainViewModel main, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(shell);
        _main = main;
        _shell = shell;
        InitializeComponent();
        DataContext = new DesktopContext(main, shell);
        SettingsPathBox.Text = shell.Settings.FilePath ?? Loc.Instance["Settings_InMemory"];
        _start = Take();
        WindowSetup.Prepare(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(); // Esc = Close (changes are already applied; Cancel is the explicit undo)
            }
        };
    }

    /// <summary>The user asked for the floating toolbar from the Floating Toolbar page.</summary>
    internal event EventHandler? ShowToolbarRequested;

    /// <summary>Shows a page (e.g. Settings &gt; Floating Toolbar Settings) and remembers the values to undo to.</summary>
    internal void ShowPage(SettingsPage page)
    {
        Pages.SelectedIndex = page switch
        {
            SettingsPage.Appearance => 1,
            SettingsPage.FloatingToolbar => 2,
            SettingsPage.Startup => 3,
            SettingsPage.Advanced => 4,
            _ => 0,
        };
    }

    /// <summary>Puts back the values from when the window was opened (Cancel).</summary>
    internal void Revert()
    {
        var s = _start;
        _shell.IsTopmost = s.Topmost;
        _shell.Theme = s.Theme;
        _shell.BarSize = s.BarSize;
        _shell.FloatingOrientation = s.Orientation;
        _shell.CurrentView = s.View;
        if (_main.Languages.FirstOrDefault(l => string.Equals(l.Code, s.Language, StringComparison.OrdinalIgnoreCase)) is { } language)
        {
            _main.SelectedLanguage = language;
        }
    }

    private Snapshot Take() => new(_main.SelectedLanguage.Code, _shell.Theme, _shell.BarSize, _shell.FloatingOrientation, _shell.IsTopmost, _shell.CurrentView);

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Revert();
        Close();
    }

    private void OnShowToolbar(object sender, RoutedEventArgs e) => ShowToolbarRequested?.Invoke(this, EventArgs.Empty);

    private readonly record struct Snapshot(string Language, ThemePreference Theme, BarSize BarSize, BarOrientation Orientation, bool Topmost, MainView View);
}
