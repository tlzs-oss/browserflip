using System.ComponentModel;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using BrowserFlip.App.Localization;
using BrowserFlip.App.Services;
using BrowserFlip.App.ViewModels;

namespace BrowserFlip.App;

public partial class MainWindow : Window
{
    /// <summary>Desktop-experience state (always on top, compact mode, options).</summary>
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(MainWindow), new PropertyMetadata(null));

    public ShellViewModel? Shell
    {
        get => (ShellViewModel?)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.Instance.PropertyChanged -= OnLanguageChanged;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Activated += (_, _) => (DataContext as MainViewModel)?.OnActivated();
        Deactivated += (_, _) => (DataContext as MainViewModel)?.OnDeactivated();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => ApplyLanguage();

    /// <summary>xml:lang + per-language font stack so CJK text uses the correct regional glyphs.</summary>
    private void ApplyLanguage()
    {
        Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag);
        FontFamily = new FontFamily(Loc.Instance.FontFamilyName);
    }
}
