using Avalonia.Controls;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.UI.Views;

/// <summary>
/// The product's own About window, opened by the first item of the macOS application menu.
/// <para>
/// Until this existed that item was Avalonia's, and it opened Avalonia's About dialog - the
/// Owner reported seeing "About Avalonia" in BrowserFlip's menu bar. The window is shared code,
/// not macOS-specific, so the same window is available to every platform.
/// </para>
/// </summary>
public partial class AboutWindow : Window
{
    private AboutViewModel? _bound;

    public AboutWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => Bind();
        Closed += (_, _) => Unbind();

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null)
        {
            close.Click += (_, _) => Close();
        }
    }

    private void Bind()
    {
        Unbind();
        _bound = DataContext as AboutViewModel;
    }

    private void Unbind()
    {
        if (_bound is not null)
        {
            // The view model follows the application's main view model, which outlives this
            // window, so it has to be told when the window is finished with it.
            _bound.Detach();
            _bound = null;
        }
    }
}
