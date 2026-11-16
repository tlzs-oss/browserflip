using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace BrowserFlip.App.Services;

/// <summary>Applies the Light / Dark / High Contrast theme (following Windows or a user override) and keeps title bars in sync.</summary>
public static class ThemeManager
{
    private static UserPreferenceChangedEventHandler? _handler;
    private static Application? _app;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static bool IsDark { get; private set; }

    /// <summary>User override; <see cref="ThemePreference.System"/> follows the Windows app theme.</summary>
    public static ThemePreference Preference { get; private set; } = ThemePreference.System;

    public static void Initialize(Application app, ThemePreference preference = ThemePreference.System)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
        Preference = preference;
        Apply(app);
        _handler = (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color
                or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility)
            {
                app.Dispatcher.BeginInvoke(() => Apply(app));
            }
        };
        SystemEvents.UserPreferenceChanged += _handler;
    }

    /// <summary>Changes the theme preference and re-applies it.</summary>
    public static void SetPreference(ThemePreference preference)
    {
        Preference = preference;
        if (_app is not null)
        {
            Apply(_app);
        }
    }

    /// <summary>Unsubscribes from the static SystemEvents (avoids leaking the handler).</summary>
    public static void Shutdown()
    {
        if (_handler is not null)
        {
            SystemEvents.UserPreferenceChanged -= _handler;
            _handler = null;
        }

        _app = null;
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
        var highContrast = SystemParameters.HighContrast;
        IsDark = !highContrast && Preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => ReadAppsUseDarkTheme(),
        };
        var file = highContrast ? "Themes/HighContrast.xaml" : IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        // Assembly-qualified so it also resolves when hosted by another entry assembly (tests).
        var uri = new Uri($"pack://application:,,,/BrowserFlip;component/{file}", UriKind.Absolute);
        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source is not null && d.Source.OriginalString.Contains("/BrowserFlip;component/Themes/", StringComparison.Ordinal));
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

        foreach (var w in FluentWindows)
        {
            ApplyFluent(w);
        }
    }

    private static readonly List<Window> FluentWindows = [];

    /// <summary>
    /// Opts a window into the built-in WPF Fluent (Windows 11) control styles, following the current
    /// light / dark choice. High Contrast keeps the classic system-colour styles. Not used for the
    /// compact bar (a transparent layered window).
    /// </summary>
    public static void UseFluent(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!FluentWindows.Contains(window))
        {
            FluentWindows.Add(window);
            window.Closed += (_, _) => FluentWindows.Remove(window);
        }

        ApplyFluent(window);
    }

    private static void ApplyFluent(Window window)
    {
#pragma warning disable WPF0001 // ThemeMode is marked experimental in .NET 10 WPF.
        window.ThemeMode = SystemParameters.HighContrast ? ThemeMode.None : IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
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
