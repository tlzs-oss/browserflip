using System.ComponentModel;
using Avalonia.Controls;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.UI.Views;

/// <summary>
/// The settings window, shared by every platform. It is deliberately the only place that shows
/// the platform capability matrix, so a user on Wayland can see why a toggle is disabled rather
/// than finding that it silently does nothing.
/// </summary>
public partial class SettingsWindow : Window
{
    private SettingsViewModel? _bound;

    public SettingsWindow()
    {
        InitializeComponent();

        // This window has to follow "always on top" as well, not just the main window. The main
        // window is its owner, and with the owner kept above everything and this window not, it
        // opened UNDERNEATH the window it almost exactly overlaps - measured: main 560x620 at
        // 476,153 topmost=True, settings 520x700 at 496,127 topmost=False - so choosing
        // "Settings..." looked like it did nothing. That is what the Owner hit: their saved
        // preference had always-on-top switched on.
        DataContextChanged += (_, _) => Bind();
        Closed += (_, _) => Unbind();

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null)
        {
            close.Click += (_, _) => Close();
        }
    }

    private SettingsViewModel? Model => DataContext as SettingsViewModel;

    private void Bind()
    {
        Unbind();
        if (Model is null)
        {
            return;
        }

        _bound = Model;
        Topmost = _bound.Topmost;
        _bound.PropertyChanged += OnModelPropertyChanged;
    }

    private void Unbind()
    {
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnModelPropertyChanged;
            // The view model follows the application's main view model, which outlives both of
            // them, so it has to be told when this window is finished with it.
            _bound.Detach();
            _bound = null;
        }
    }

    /// <summary>
    /// Keeps up with the toggle. The user switches always-on-top on from this very window, and
    /// doing so raises the main window above it unless this one rises too.
    /// </summary>
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Topmost) && _bound is not null)
        {
            Topmost = _bound.Topmost;
        }
    }
}
