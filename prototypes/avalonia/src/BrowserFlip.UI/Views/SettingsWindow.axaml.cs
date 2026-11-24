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
    public SettingsWindow()
    {
        InitializeComponent();

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null)
        {
            close.Click += (_, _) => Close();
        }
    }

    private SettingsViewModel? Model => DataContext as SettingsViewModel;
}
