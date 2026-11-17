using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BrowserFlip.App.Views;

/// <summary>
/// Six-dot drag handle (Phase 4). With <see cref="MovesWindow"/> it moves its window with the native Windows move loop
/// (<see cref="Window.DragMove"/>, the same as dragging the title bar; no simulated input). Without it, it only shows
/// where to grab: the press bubbles to the owner's existing drag handling (the floating toolbar's DragMove, which keeps
/// docking, the drag preview and auto-hide unchanged).
/// </summary>
public partial class DragGrip : UserControl
{
    public static readonly DependencyProperty MovesWindowProperty = DependencyProperty.Register(
        nameof(MovesWindow), typeof(bool), typeof(DragGrip), new PropertyMetadata(false));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(DragGrip), new PropertyMetadata(Orientation.Vertical, OnOrientationChanged));

    public DragGrip()
    {
        InitializeComponent();
        MouseEnter += (_, _) => Bd.SetResourceReference(Border.BackgroundProperty, "BfCardHoverBrush");
        MouseLeave += (_, _) => Bd.Background = System.Windows.Media.Brushes.Transparent;
    }

    /// <summary>True: pressing the grip moves the window it is in (Compact view).</summary>
    public bool MovesWindow
    {
        get => (bool)GetValue(MovesWindowProperty);
        set => SetValue(MovesWindowProperty, value);
    }

    /// <summary>Vertical = dots in 2 columns x 3 rows (beside a horizontal row); Horizontal = 3 x 2 (above a vertical one).</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Number of window moves started from this grip (tests).</summary>
    internal int MovesStarted { get; private set; }

    /// <summary>Test hook: replaces <see cref="Window.DragMove"/> (a real move loop needs a real mouse button).</summary>
    internal Action<Window>? DragMoveOverride { get; set; }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (!MovesWindow || e.ClickCount > 1 || Window.GetWindow(this) is not { } window)
        {
            return; // toolbar: the bar's own drag handling takes the press; a double click does nothing here
        }

        e.Handled = true; // never reaches buttons, the menu or the caption (no maximize / no browser switch)
        MovesStarted++;
        if (DragMoveOverride is { } move)
        {
            move(window);
            return;
        }

        try
        {
            window.DragMove(); // the native move loop: follows the mouse until the button is released
        }
        catch (InvalidOperationException)
        {
            // The button was released before the move started: nothing to move.
        }
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grip = (DragGrip)d;
        var vertical = (Orientation)e.NewValue == Orientation.Vertical;
        grip.Dots.Rows = vertical ? 3 : 2;
        grip.Dots.Columns = vertical ? 2 : 3;
        grip.Dots.Width = vertical ? 8 : 14;
        grip.Dots.Height = vertical ? 14 : 8;
    }
}
