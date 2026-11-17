using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace BrowserFlip.App.Views;

/// <summary>
/// Application name, version, copyright, license and project link. Localized text is bound to the shared
/// <c>Loc</c> instance (weak binding); no event subscription, so a closed About window is never kept alive.
/// </summary>
public partial class AboutContent : UserControl
{
    /// <summary>Project page (the public repository).</summary>
    internal const string ProjectUrl = "https://github.com/tlzs-oss/browserflip";

    public AboutContent()
    {
        InitializeComponent();
        var assembly = typeof(AboutContent).Assembly;
        VersionRun.Text = DisplayVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        CopyrightText.Text = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
        WebsiteButton.ToolTip = ProjectUrl;
    }

    /// <summary>"0.3.0 (2657df8)" from the informational version "0.3.0+2657df8…" (the source commit), or "0.3.0".</summary>
    internal static string DisplayVersion(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "?";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return informational;
        }

        var commit = informational[(plus + 1)..];
        return commit.Length == 0 ? informational[..plus] : $"{informational[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
    }

    private void OnWebsiteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No handler for https (or it failed to start): nothing else to do from an About box.
        }
    }
}
