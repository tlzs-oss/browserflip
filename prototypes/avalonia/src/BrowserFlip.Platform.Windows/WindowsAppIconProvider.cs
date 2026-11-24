using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BrowserFlip.Core.Detection;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.Windows;

/// <summary>
/// Extracts a browser's icon with the shell (<c>SHDefExtractIconW</c>, the same call the shipping
/// WPF product uses) and encodes it as PNG, because the shared UI consumes PNG bytes.
/// </summary>
/// <remarks>
/// <para>
/// The WPF product hands the <c>HICON</c> to <c>Imaging.CreateBitmapSourceFromHIcon</c>. That is
/// not available here: the Avalonia prototype must not reference WPF, and
/// <c>System.Drawing.Common</c> is Windows-only-and-unsupported for this kind of use, while the
/// prototype's production projects carry no third-party packages at all. So the bitmap bits are
/// read with <c>GetDIBits</c> and written out by the minimal PNG writer below — about sixty lines
/// over <see cref="ZLibStream"/>, which already produces exactly the zlib stream a PNG
/// <c>IDAT</c> chunk needs, Adler-32 and all.
/// </para>
/// <para>Never throws: a missing or malformed icon returns <c>null</c> and the list shows none.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppIconProvider : IAppIconProvider
{
    private const int MinimumPixelSize = 16;
    private const int MaximumPixelSize = 512;

    /// <inheritdoc />
    /// <remarks>
    /// The shared UI only has one string to pass (<c>MainViewModel</c> passes
    /// <see cref="BrowserInfo.IconPath"/>), so an icon-resource index may travel inside it in the
    /// form the registry stores — <c>"C:\Program Files\...\firefox.exe",1</c>. Core's own parser
    /// splits it, so the two Windows front-ends interpret <c>DefaultIcon</c> identically.
    /// </remarks>
    public byte[]? GetIconPng(string? applicationPath, int pixelSize = 64)
    {
        var (path, index) = RegistryValueParser.ParseIconLocation(applicationPath);
        return GetIconPng(path, index, pixelSize);
    }

    /// <summary>
    /// Returns PNG bytes for icon <paramref name="iconIndex"/> of <paramref name="iconPath"/> at
    /// roughly <paramref name="pixelSize"/>, or <c>null</c> when no icon can be produced.
    /// </summary>
    public byte[]? GetIconPng(string? iconPath, int iconIndex, int pixelSize)
    {
        try
        {
            return Extract(iconPath, iconIndex, Math.Clamp(pixelSize, MinimumPixelSize, MaximumPixelSize));
        }
#pragma warning disable CA1031 // A missing icon must never break the list.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>
    /// Returns PNG bytes for a detected browser: its registered icon resource, falling back to
    /// the executable's own icon — the same two-step the WPF product's icon loader performs.
    /// </summary>
    public byte[]? GetBrowserIconPng(BrowserInfo browser, int pixelSize = 64)
    {
        ArgumentNullException.ThrowIfNull(browser);
        var fromIcon = GetIconPng(browser.IconPath, browser.IconIndex, pixelSize);
        if (fromIcon is not null || string.Equals(browser.IconPath, browser.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            return fromIcon;
        }

        return GetIconPng(browser.ExecutablePath, 0, pixelSize);
    }

    private static byte[]? Extract(string? iconPath, int iconIndex, int pixelSize)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        var large = IntPtr.Zero;
        var small = IntPtr.Zero;
        try
        {
            // nIconSize: LOWORD = large size, HIWORD = small size. S_FALSE (1) means only the
            // small icon came back, which is still usable.
            var sizes = (uint)pixelSize | (16u << 16);
            var hr = NativeMethods.SHDefExtractIconW(iconPath, iconIndex, 0, out large, out small, sizes);
            if (hr is not (0 or 1))
            {
                return null;
            }

            var icon = large != IntPtr.Zero ? large : small;
            return icon == IntPtr.Zero ? null : Encode(icon);
        }
        finally
        {
            if (large != IntPtr.Zero)
            {
                _ = NativeMethods.DestroyIcon(large);
            }

            if (small != IntPtr.Zero)
            {
                _ = NativeMethods.DestroyIcon(small);
            }
        }
    }

    /// <summary>Reads an <c>HICON</c>'s colour bitmap as BGRA and encodes it as PNG.</summary>
    private static byte[]? Encode(IntPtr icon)
    {
        if (!NativeMethods.GetIconInfo(icon, out var info))
        {
            return null;
        }

        try
        {
            // A 1-bit icon has no colour bitmap. Browser icons never are one, and reconstructing
            // such an icon from its two masks would be more code than the case deserves.
            if (info.ColorBitmap == IntPtr.Zero || !TryGetSize(info.ColorBitmap, out var width, out var height))
            {
                return null;
            }

            var pixels = ReadBgra(info.ColorBitmap, width, height);
            if (pixels is null)
            {
                return null;
            }

            // Icons older than Windows XP carry no alpha channel; their transparency lives in the
            // AND mask. Without this, such an icon would render as a black square.
            if (!HasTransparency(pixels))
            {
                ApplyMask(pixels, info.MaskBitmap, width, height);
            }

            return Png.FromBgra(pixels, width, height);
        }
        finally
        {
            // GetIconInfo creates copies of both bitmaps; the caller owns them.
            if (info.ColorBitmap != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteObject(info.ColorBitmap);
            }

            if (info.MaskBitmap != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteObject(info.MaskBitmap);
            }
        }
    }

    private static bool TryGetSize(IntPtr bitmap, out int width, out int height)
    {
        width = 0;
        height = 0;
        var header = default(NativeMethods.BitmapDescription);
        if (NativeMethods.GetObjectW(bitmap, Marshal.SizeOf<NativeMethods.BitmapDescription>(), ref header) == 0)
        {
            return false;
        }

        width = header.Width;
        height = header.Height;
        // 512 x 512 is the largest icon Windows ships; anything larger is a corrupt resource.
        return width is > 0 and <= 1024 && height is > 0 and <= 1024;
    }

    /// <summary>
    /// Copies a GDI bitmap into a managed BGRA buffer. A negative <c>Height</c> in the requested
    /// header asks GDI for top-down rows, which is the order PNG stores them in.
    /// </summary>
    private static byte[]? ReadBgra(IntPtr bitmap, int width, int height)
    {
        var screen = NativeMethods.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var info = default(NativeMethods.BitmapInfo);
            info.Header.Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>();
            info.Header.Width = width;
            info.Header.Height = -height;
            info.Header.Planes = 1;
            info.Header.BitCount = 32;
            info.Header.Compression = NativeMethods.BiRgb;

            var pixels = new byte[width * height * 4];
            var copied = NativeMethods.GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref info, NativeMethods.DibRgbColors);
            return copied == height ? pixels : null;
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>True when any pixel is not fully transparent, i.e. the alpha channel is real.</summary>
    private static bool HasTransparency(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Derives alpha from the icon's AND mask: a set mask bit means "transparent". Read back as
    /// 32-bit, a set bit arrives as white and a clear bit as black.
    /// </summary>
    private static void ApplyMask(byte[] bgra, IntPtr mask, int width, int height)
    {
        byte[]? bits = null;
        if (mask != IntPtr.Zero && TryGetSize(mask, out var maskWidth, out var maskHeight)
            && maskWidth == width && maskHeight == height)
        {
            bits = ReadBgra(mask, width, height);
        }

        for (var i = 0; i < bgra.Length; i += 4)
        {
            // No usable mask: treat the icon as fully opaque rather than invisible.
            bgra[i + 3] = bits is null || bits[i] < 128 ? (byte)0xFF : (byte)0x00;
        }
    }
}

/// <summary>
/// Minimal PNG writer: 8-bit RGBA, no interlacing, filter 0. Self-contained on purpose — the
/// prototype's production projects take no third-party dependency, and the only thing needed
/// from the platform is Deflate, which the BCL provides.
/// </summary>
internal static class Png
{
    private const byte BitDepth8 = 8;
    private const byte ColorTypeRgba = 6;
    private const int MaxPixels = 1024 * 1024;

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Encodes a top-down, non-premultiplied BGRA buffer as a PNG.</summary>
    internal static byte[]? FromBgra(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels || bgra.Length < width * height * 4)
        {
            return null;
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = BitDepth8;
        header[9] = ColorTypeRgba;
        // header[10..12] stay 0: deflate compression, adaptive filtering, no interlace.
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", Deflate(ToScanlines(bgra, width, height)));
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>Swaps BGRA to RGBA and prefixes every row with its filter byte (0 = none).</summary>
    private static byte[] ToScanlines(byte[] bgra, int width, int height)
    {
        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        var target = 0;
        for (var y = 0; y < height; y++)
        {
            raw[target++] = 0;
            var row = y * stride;
            for (var x = 0; x < stride; x += 4)
            {
                raw[target++] = bgra[row + x + 2];
                raw[target++] = bgra[row + x + 1];
                raw[target++] = bgra[row + x];
                raw[target++] = bgra[row + x + 3];
            }
        }

        return raw;
    }

    /// <summary>
    /// <see cref="ZLibStream"/> emits the zlib wrapper (header + Adler-32 checksum) that the PNG
    /// specification requires of an <c>IDAT</c> stream, so no checksum code is needed here.
    /// </summary>
    private static byte[] Deflate(byte[] raw)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var name = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            name[i] = (byte)type[i];
        }

        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length, 0, 4);
        output.Write(name, 0, 4);
        output.Write(data, 0, data.Length);

        // The CRC covers the chunk type and its data, but not the length field.
        var crc = Crc32(Crc32(0xFFFFFFFFu, name), data) ^ 0xFFFFFFFFu;
        var trailer = new byte[4];
        WriteBigEndian(trailer, 0, (int)crc);
        output.Write(trailer, 0, 4);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(uint seed, byte[] data)
    {
        var crc = seed;
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256u; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

/// <summary>
/// The Win32 entry points used to turn an icon resource into pixels. <c>[DllImport]</c> rather
/// than <c>[LibraryImport]</c>: the repository keeps <c>AllowUnsafeBlocks</c> off, and the
/// source-generated marshalling requires unsafe code.
/// </summary>
internal static class NativeMethods
{
    internal const uint BiRgb = 0;
    internal const uint DibRgbColors = 0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SHDefExtractIconW(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetIconInfo(IntPtr hIcon, out IconInfo piconinfo);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetObjectW(IntPtr hgdiobj, int cbBuffer, ref BitmapDescription lpvObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[]? lpvBits, ref BitmapInfo lpbmi, uint usage);

    /// <summary>ICONINFO. <c>IsIcon</c> is a BOOL; only the two bitmap handles are used here.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct IconInfo
    {
        internal int IsIcon;
        internal int HotspotX;
        internal int HotspotY;
        internal IntPtr MaskBitmap;
        internal IntPtr ColorBitmap;
    }

    /// <summary>BITMAP, as filled in by <c>GetObjectW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapDescription
    {
        internal int Type;
        internal int Width;
        internal int Height;
        internal int WidthBytes;
        internal ushort Planes;
        internal ushort BitsPerPixel;
        internal IntPtr Bits;
    }

    /// <summary>BITMAPINFOHEADER.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int XPelsPerMeter;
        internal int YPelsPerMeter;
        internal uint ClrUsed;
        internal uint ClrImportant;
    }

    /// <summary>
    /// BITMAPINFO. The trailing colour table is a single entry: at 32 bits per pixel with
    /// <c>BI_RGB</c> GDI never reads it, and a fixed-size array would require unsafe code.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        internal BitmapInfoHeader Header;
        internal uint FirstColorTableEntry;
    }
}
