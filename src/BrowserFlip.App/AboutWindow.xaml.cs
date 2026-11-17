using System.Windows;
using BrowserFlip.App.Views;

namespace BrowserFlip.App;

/// <summary>Help &gt; About BrowserFlip (one instance, reused by the coordinator).</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        WindowSetup.Prepare(this);
    }

    private void OnOk(object sender, RoutedEventArgs e) => Close();
}
