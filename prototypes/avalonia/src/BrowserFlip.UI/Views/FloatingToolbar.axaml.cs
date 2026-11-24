using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

        // PositionChanged fires continuously while dragging. Persisting there would rewrite the
        // settings file dozens of times a second, so the position is only stored once the drag
        // ends (and again when the window closes, to catch a programmatic move).
        AddHandler(PointerReleasedEvent, OnPointerReleasedAnywhere, RoutingStrategies.Tunnel);
        Closed += (_, _) => PersistPosition();
    }

    private void OnPointerReleasedAnywhere(object? sender, PointerReleasedEventArgs e) => PersistPosition();

    /// <summary>
    /// Stores the current position, but only once it is really the toolbar's own position.
    /// Before <see cref="RestorePosition"/> runs the window reports (0, 0), and writing that
    /// would silently discard the position the user had saved.
    /// </summary>
    private void PersistPosition()
    {
        if (_positionRestored)
        {
            Model?.SaveToolbarPosition(Position.X, Position.Y);
        }
    }

    private bool _positionRestored;

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
        _positionRestored = true;
    }

    private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
