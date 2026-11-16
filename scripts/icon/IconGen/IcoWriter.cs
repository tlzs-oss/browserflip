using System.IO;
using System.Windows.Media.Imaging;

namespace BrowserFlip.IconGen;

/// <summary>
/// Writes a multi-resolution .ico. Entries of 256 px are stored as PNG (the Vista+ convention);
/// smaller entries are stored as classic 32-bit BMP (BGRA + 1-bit AND mask) for maximum compatibility.
/// </summary>
internal static class IcoWriter
{
    public static void Write(string path, IReadOnlyList<BitmapSource> images)
    {
        var payloads = images.Select(img => img.PixelWidth >= 256 ? Renderer.EncodePng(img) : EncodeDib(img)).ToList();

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        // ICONDIR
        w.Write((ushort)0); // reserved
        w.Write((ushort)1); // type: icon
        w.Write((ushort)images.Count);

        // ICONDIRENTRY[]
        var offset = 6 + (16 * images.Count);
        for (var i = 0; i < images.Count; i++)
        {
            var size = images[i].PixelWidth;
            w.Write((byte)(size >= 256 ? 0 : size)); // width (0 = 256)
            w.Write((byte)(size >= 256 ? 0 : size)); // height
            w.Write((byte)0); // palette colours
            w.Write((byte)0); // reserved
            w.Write((ushort)1); // colour planes
            w.Write((ushort)32); // bits per pixel
            w.Write(payloads[i].Length);
            w.Write(offset);
            offset += payloads[i].Length;
        }

        foreach (var p in payloads)
        {
            w.Write(p);
        }
    }

    private static byte[] EncodeDib(BitmapSource img)
    {
        int width = img.PixelWidth, height = img.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        img.CopyPixels(pixels, stride, 0); // Bgra32, straight alpha, top-down

        var maskStride = ((width + 31) / 32) * 4;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // BITMAPINFOHEADER (height is doubled: XOR image + AND mask)
        w.Write(40);
        w.Write(width);
        w.Write(height * 2);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0); // BI_RGB
        w.Write(stride * height + maskStride * height);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);

        // XOR (colour) bitmap, bottom-up
        for (var y = height - 1; y >= 0; y--)
        {
            w.Write(pixels, y * stride, stride);
        }

        // AND mask, bottom-up: 1 = transparent
        for (var y = height - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] == 0)
                {
                    row[x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }

            w.Write(row);
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Reads back an .ico header and returns (width, height, bpp, bytes, format) for each entry.</summary>
    public static IReadOnlyList<(int Width, int Height, int Bpp, int Bytes, string Format)> Inspect(string path)
    {
        var data = File.ReadAllBytes(path);
        using var r = new BinaryReader(new MemoryStream(data));
        if (r.ReadUInt16() != 0 || r.ReadUInt16() != 1)
        {
            throw new InvalidDataException("Not an icon file.");
        }

        int count = r.ReadUInt16();
        var result = new List<(int, int, int, int, string)>();
        for (var i = 0; i < count; i++)
        {
            int width = r.ReadByte(), height = r.ReadByte();
            r.ReadByte();
            r.ReadByte();
            r.ReadUInt16();
            int bpp = r.ReadUInt16();
            var bytes = r.ReadInt32();
            var offset = r.ReadInt32();
            var isPng = data[offset] == 0x89 && data[offset + 1] == (byte)'P' && data[offset + 2] == (byte)'N' && data[offset + 3] == (byte)'G';
            result.Add((width == 0 ? 256 : width, height == 0 ? 256 : height, bpp, bytes, isPng ? "PNG" : "BMP"));
        }

        return result;
    }
}
