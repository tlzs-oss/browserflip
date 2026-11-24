using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.UI.Views;

/// <summary>The prototype's main window. Shared across every platform Avalonia supports.</summary>
public partial class MainWindow : Window
{
    private MainViewModel? _bound;

    public MainWindow()
    {
        InitializeComponent();

        // Topmost is a view concern, so the window follows the view model's persisted setting.
        DataContextChanged += (_, _) => Bind();
        Closed += (_, _) => Unbind();
        var refresh = this.FindControl<Button>("RefreshButton");
        if (refresh is not null)
        {
            refresh.Click += (_, _) => Model?.Refresh();
        }
    }

    private MainViewModel? Model => DataContext as MainViewModel;

    /// <summary>
    /// Attaches to the current view model, detaching from the previous one first. Without the
    /// detach, every DataContext change would leave a live handler on the old view model.
    /// </summary>
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
            _bound = null;
        }
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Topmost) && _bound is not null)
        {
            Topmost = _bound.Topmost;
        }
    }

    private async void OnSetDefaultClick(object? sender, RoutedEventArgs e)
    {
        if (Model is null || (sender as Button)?.DataContext is not BrowserItemViewModel item)
        {
            return;
        }

        await Model.SetDefaultAsync(item).ConfigureAwait(true);
    }
}
