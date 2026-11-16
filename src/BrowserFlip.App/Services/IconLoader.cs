using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrowserFlip.App.Services;

/// <summary>Extracts browser icons from executables / icon resources via the shell.</summary>
public static class IconLoader
{
    // Cards show icons at 40 DIP; 128 px stays sharp up to 320 % scaling.
    private const int IconSize = 128;

    /// <summary>Returns a frozen image, or <c>null</c> when no icon can be extracted.</summary>
    public static ImageSource? Load(string? path, int index, string? fallbackPath)
        => TryLoad(path, index) ?? (string.Equals(path, fallbackPath, StringComparison.OrdinalIgnoreCase) ? null : TryLoad(fallbackPath, 0));

    private static BitmapSource? TryLoad(string? path, int index)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return null;
        }

        var large = IntPtr.Zero;
        var small = IntPtr.Zero;
        try
        {
            // nIconSize: LOWORD = large size, HIWORD = small size.
            const uint sizes = IconSize | (16u << 16);
            if (SHDefExtractIconW(path, index, 0, out large, out small, sizes) != 0 || large == IntPtr.Zero)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or ExternalException)
        {
            return null;
        }
        finally
        {
            if (large != IntPtr.Zero)
            {
                _ = DestroyIcon(large);
            }

            if (small != IntPtr.Zero)
            {
                _ = DestroyIcon(small);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHDefExtractIconW(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
