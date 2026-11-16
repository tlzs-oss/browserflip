using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace BrowserFlip.App.Services;

/// <summary>Follows the Windows app theme (light/dark) and keeps the title bar in sync.</summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static bool IsDark { get; private set; }

    public static void Initialize(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        Apply(app);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            {
                app.Dispatcher.Invoke(() => Apply(app));
            }
        };
    }

    /// <summary>Applies the dark/light title bar to <paramref name="window"/>.</summary>
    public static void ApplyTitleBar(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var dark = IsDark ? 1 : 0;
        // Supported on Windows 10 20H1+ and Windows 11; failure is harmless.
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    private static void Apply(Application app)
    {
        IsDark = !SystemParameters.HighContrast && ReadAppsUseDarkTheme();
        var uri = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source is not null && d.Source.OriginalString.StartsWith("Themes/", StringComparison.Ordinal));
        var next = new ResourceDictionary { Source = uri };
        if (existing is null)
        {
            dictionaries.Insert(0, next);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = next;
        }

        foreach (Window w in app.Windows)
        {
            ApplyTitleBar(w);
        }
    }

    private static bool ReadAppsUseDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
