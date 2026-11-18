using System.Windows;
using System.Windows.Controls;

namespace BrowserFlip.App.Views;

/// <summary>Current default, policy warning and status message (data context: <c>MainViewModel</c>).</summary>
public partial class StatusStrip : UserControl
{
    /// <summary>False hides the "current default" line (a view that shows it in its own header).</summary>
    public static readonly DependencyProperty ShowCurrentProperty = DependencyProperty.Register(
        nameof(ShowCurrent), typeof(bool), typeof(StatusStrip), new PropertyMetadata(true));

    public StatusStrip() => InitializeComponent();

    public bool ShowCurrent
    {
        get => (bool)GetValue(ShowCurrentProperty);
        set => SetValue(ShowCurrentProperty, value);
    }
}
