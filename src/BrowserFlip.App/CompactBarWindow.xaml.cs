using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;

namespace BrowserFlip.App;

/// <summary>View model pair used by the compact bar.</summary>
public sealed record DesktopContext(MainViewModel Main, ShellViewModel Shell);

/// <summary>
/// Compact toolbar (horizontal or vertical). Placement, docking and auto-hide are driven by
/// <see cref="Desktop.DesktopCoordinator"/>; this class only renders and reports user gestures.
/// </summary>
public partial class CompactBarWindow : Window
{
    public CompactBarWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.Instance.PropertyChanged -= OnLanguageChanged;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        DataContextChanged += (_, _) => HookShell();
    }

    /// <summary>The user finished dragging the bar (DragMove returned).</summary>
    public event EventHandler? DragCompleted;

    /// <summary>The user dragging started.</summary>
    public event EventHandler? DragStarted;

    /// <summary>The collapsed tab was clicked or invoked from the keyboard.</summary>
    public event EventHandler? TabInvoked;

    /// <summary>The bar's context menu was requested (right click / menu key).</summary>
    public event EventHandler? MenuRequested;

    /// <summary>True while DragMove is running.</summary>
    public bool IsDragging { get; private set; }

    private ShellViewModel? Shell => (DataContext as DesktopContext)?.Shell;

    /// <summary>Measures the expanded bar in DIP for the current orientation and maximum length.</summary>
    public Size MeasureExpanded(double maxLength)
    {
        var shell = Shell;
        if (shell is null)
        {
            return new Size(1, 1);
        }

        ApplyOrientation(shell.IsVertical);
        var constraint = shell.IsVertical
            ? new Size(shell.BarThickness, maxLength)
            : new Size(maxLength, shell.BarThickness);
        BarRoot.Measure(constraint);
        var desired = BarRoot.DesiredSize;
        return shell.IsVertical
            ? new Size(shell.BarThickness, Math.Min(Math.Max(desired.Height, shell.BarThickness), maxLength))
            : new Size(Math.Min(Math.Max(desired.Width, shell.BarThickness), maxLength), shell.BarThickness);
    }

    /// <summary>Duration of the expand / collapse fades.</summary>
    internal static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(160);

    /// <summary>False runs collapse / expand without animation (tests; Windows animations off).</summary>
    internal bool AnimationsEnabled { get; set; } = true;

    /// <summary>True while the collapse fade is running.</summary>
    internal bool IsCollapsing { get; private set; }

    /// <summary>True runs the fades even when Windows animations are off (tests of the fade paths).</summary>
    internal bool ForceAnimations { get; set; }

    private bool Animate => AnimationsEnabled && (ForceAnimations || SystemParameters.ClientAreaAnimation);

    /// <summary>Short fade-in when expanding, unless Windows animations are turned off.</summary>
    public void PlayExpandAnimation()
    {
        CancelCollapseAnimation();
        if (!Animate)
        {
            return;
        }

        BarRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, FadeDuration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>
    /// Fades the expanded bar out, then runs <paramref name="completed"/> (which switches to the edge
    /// strip). <see cref="CancelCollapseAnimation"/> (cursor came back) stops it without running the callback.
    /// </summary>
    public void PlayCollapseAnimation(Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (!Animate)
        {
            completed();
            return;
        }

        IsCollapsing = true;
        var fade = new DoubleAnimation(1, 0, FadeDuration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            if (!IsCollapsing)
            {
                return; // cancelled (re-expanded) meanwhile
            }

            IsCollapsing = false;
            BarRoot.BeginAnimation(OpacityProperty, null);
            BarRoot.Opacity = 1; // the bar content is hidden by the collapsed trigger; the strip is the tab
            completed();
        };
        BarRoot.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Stops a running collapse fade and shows the bar fully.</summary>
    public void CancelCollapseAnimation()
    {
        IsCollapsing = false;
        BarRoot.BeginAnimation(OpacityProperty, null);
        BarRoot.Opacity = 1;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        e.Handled = true;
        MenuRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
        {
            e.Handled = true;
            MenuRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Drag from empty bar space only (buttons handle their own clicks).
        if (e.ButtonState != MouseButtonState.Pressed || IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        IsDragging = true;
        DragStarted?.Invoke(this, EventArgs.Empty);
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse was released before DragMove started.
        }
        finally
        {
            IsDragging = false;
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTabClick(object sender, RoutedEventArgs e) => TabInvoked?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close(); // the coordinator cancels and retracts / hides

    /// <summary>
    /// Brings the bar above other (non-topmost) windows without activating it: a bar that is not "always on
    /// top" must still be visible when it expands from its edge.
    /// </summary>
    public void BringToFront()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && !Topmost)
        {
            const uint SwpNoSize = 0x0001;
            const uint SwpNoMove = 0x0002;
            _ = Desktop.Win32.NativeMethods.SetWindowPos(hwnd, IntPtr.Zero /* HWND_TOP */, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | Desktop.Win32.NativeMethods.SwpNoActivate | Desktop.Win32.NativeMethods.SwpNoOwnerZOrder);
        }
    }

    private void OnBrowserScrollerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Horizontal bar: map the vertical wheel to horizontal scrolling.
        if (Shell is { IsVertical: false })
        {
            BrowserScroller.ScrollToHorizontalOffset(BrowserScroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void HookShell()
    {
        if (Shell is { } shell)
        {
            shell.PropertyChanged -= OnShellChanged;
            shell.PropertyChanged += OnShellChanged;
            ApplyOrientation(shell.IsVertical);
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Orientation) && Shell is { } shell)
        {
            ApplyOrientation(shell.IsVertical);
        }
    }

    private void ApplyOrientation(bool vertical)
    {
        var start = vertical ? Dock.Top : Dock.Left;
        var end = vertical ? Dock.Bottom : Dock.Right;
        DockPanel.SetDock(HomeButton, start);
        DockPanel.SetDock(PinButton, start);
        DockPanel.SetDock(OptionsButton, end);
        DockPanel.SetDock(CloseButton, end);
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        for (var d = source; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag);
        FontFamily = new FontFamily(Loc.Instance.FontFamilyName);
    }
}
