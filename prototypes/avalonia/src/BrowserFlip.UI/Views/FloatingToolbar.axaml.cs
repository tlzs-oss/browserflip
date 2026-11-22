using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.UI.Views;

/// <summary>
/// Always-on-top floating toolbar, the Avalonia counterpart of the Windows product's
/// compact bar. Drag it by the six-dot handle; the position is persisted.
/// </summary>
public partial class FloatingToolbar : Window
{
    public FloatingToolbar()
    {
        InitializeComponent();

        var handle = this.FindControl<Border>("DragHandle");
        if (handle is not null)
        {
            handle.PointerPressed += OnDragHandlePressed;
        }

        var refresh = this.FindControl<Button>("ToolbarRefresh");
        if (refresh is not null)
        {
            refresh.Click += (_, _) => Model?.Refresh();
        }

        PositionChanged += (_, _) => Model?.SaveToolbarPosition(Position.X, Position.Y);
    }

    private MainViewModel? Model => DataContext as MainViewModel;

    /// <summary>Restores the persisted position once a view model is attached.</summary>
    public void RestorePosition()
    {
        if (Model is null)
        {
            return;
        }

        var (x, y) = Model.ToolbarPosition;
        Position = new PixelPoint((int)x, (int)y);
    }

    private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
