namespace BrowserFlip.UI.Platform;

/// <summary>Loads an application's icon as encoded PNG bytes.</summary>
public interface IAppIconProvider
{
    /// <summary>
    /// Returns PNG bytes for the application at <paramref name="applicationPath"/> at roughly
    /// <paramref name="pixelSize"/>, or <c>null</c> when no icon can be produced.
    /// </summary>
    byte[]? GetIconPng(string? applicationPath, int pixelSize = 64);
}
