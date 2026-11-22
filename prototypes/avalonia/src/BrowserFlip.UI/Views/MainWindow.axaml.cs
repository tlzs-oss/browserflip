using Avalonia.Controls;
using Avalonia.Interactivity;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.UI.Views;

/// <summary>The prototype's main window. Shared across every platform Avalonia supports.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Topmost is a view concern, so the window follows the view model's persisted setting.
        DataContextChanged += (_, _) => Bind();
        var refresh = this.FindControl<Button>("RefreshButton");
        if (refresh is not null)
        {
            refresh.Click += (_, _) => Model?.Refresh();
        }
    }

    private MainViewModel? Model => DataContext as MainViewModel;

    private void Bind()
    {
        if (Model is null)
        {
            return;
        }

        Topmost = Model.Topmost;
        Model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Topmost))
            {
                Topmost = Model.Topmost;
            }
        };
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
