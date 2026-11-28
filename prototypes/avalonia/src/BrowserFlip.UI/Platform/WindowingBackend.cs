using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace BrowserFlip.UI.Platform;

/// <summary>
/// Which Avalonia windowing backend is actually driving this process's windows.
/// </summary>
/// <remarks>
/// <para>
/// On Linux the backend cannot be derived from the environment. <c>UsePlatformDetect()</c>
/// configures X11 and <c>UseWaylandWithFallback()</c> then tries Wayland first and silently falls
/// back to X11 when the compositor does not meet its requirements, so <c>XDG_SESSION_TYPE</c>,
/// <c>WAYLAND_DISPLAY</c> and <c>DISPLAY</c> between them cannot say which one won. Asking the
/// window itself can: <see cref="TopLevel.PlatformImpl"/> is the object the backend created, so
/// the assembly it lives in is the backend, as a fact rather than an inference.
/// </para>
/// <para>
/// A window must already exist. The composition root therefore creates the main window before it
/// builds the capability model.
/// </para>
/// </remarks>
public static class WindowingBackend
{
    /// <summary>
    /// The assembly implementing the application's main window — <c>Avalonia.X11</c>,
    /// <c>Avalonia.Wayland</c>, <c>Avalonia.Native</c>, <c>Avalonia.Win32</c>,
    /// <c>Avalonia.Headless</c> — or <c>null</c> when no window has been created yet.
    /// </summary>
    public static string? ImplementationAssemblyName()
        => ImplementationAssemblyName(
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);

    /// <summary>The same question asked of one specific window, which is what the tests use.</summary>
    public static string? ImplementationAssemblyName(TopLevel? topLevel)
        => topLevel?.PlatformImpl?.GetType().Assembly.GetName().Name;
}
