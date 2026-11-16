using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrowserFlip.IconGen;

/// <summary>Builds a preview sheet: every size at 1:1 plus pixel-zoomed small sizes, on light and dark backgrounds.</summary>
internal static class Preview
{
    private const int Margin = 32;
    private const int Gap = 24;
    private static readonly int[] ZoomSizes = [16, 20, 24, 32];

    public static BitmapSource Build(IReadOnlyDictionary<int, BitmapSource> renders, IReadOnlyList<int> sizes)
    {
        var rowWidth = sizes.Sum() + (Gap * (sizes.Count - 1));
        var width = Math.Max(rowWidth, 900) + (2 * Margin);
        const int panelHeight = 600;
        var height = panelHeight * 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawPanel(dc, renders, sizes, 0, width, panelHeight, "#F3F3F3", "#5C5C5C", "Light background #F3F3F3");
            DrawPanel(dc, renders, sizes, panelHeight, width, panelHeight, "#202020", "#B8B8B8", "Dark background #202020");
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return Renderer.ToStraightAlpha(rtb);
    }

    private static void DrawPanel(DrawingContext dc, IReadOnlyDictionary<int, BitmapSource> renders, IReadOnlyList<int> sizes, int top, int width, int height, string bg, string fg, string title)
    {
        var fgBrush = new SolidColorBrush(Renderer.ToColor(fg));
        dc.DrawRectangle(new SolidColorBrush(Renderer.ToColor(bg)), null, new Rect(0, top, width, height));
        DrawText(dc, title, Margin, top + 20, 16, fgBrush);

        // Row 1: every size at 1:1, bottom-aligned.
        var baseline = top + 64 + 256;
        double x = Margin;
        foreach (var size in sizes)
        {
            dc.DrawImage(renders[size], new Rect(x, baseline - size, size, size));
            DrawText(dc, size.ToString(CultureInfo.InvariantCulture), x, baseline + 8, 12, fgBrush);
            x += size + Gap;
        }

        // Row 2: small sizes zoomed with nearest-neighbour so individual pixels are visible.
        var zoomTop = baseline + 48;
        x = Margin;
        foreach (var size in ZoomSizes)
        {
            var factor = 128 / size;
            var zoomed = Zoom(renders[size], factor);
            dc.DrawImage(zoomed, new Rect(x, zoomTop, zoomed.PixelWidth, zoomed.PixelHeight));
            DrawText(dc, size.ToString(CultureInfo.InvariantCulture) + " px @ " + factor.ToString(CultureInfo.InvariantCulture) + "x", x, zoomTop + zoomed.PixelHeight + 8, 12, fgBrush);
            x += zoomed.PixelWidth + (Gap * 2);
        }

        // Row 2 (right): a taskbar-like strip with the 24 and 32 px icons in context.
        var stripX = x + Gap;
        dc.DrawRectangle(new SolidColorBrush(Renderer.ToColor(bg == "#202020" ? "#2C2C2C" : "#E6E6E6")), null, new Rect(stripX, zoomTop, 200, 48));
        dc.DrawImage(renders[24], new Rect(stripX + 12, zoomTop + 12, 24, 24));
        dc.DrawImage(renders[32], new Rect(stripX + 52, zoomTop + 8, 32, 32));
        dc.DrawImage(renders[16], new Rect(stripX + 100, zoomTop + 16, 16, 16));
        dc.DrawImage(renders[20], new Rect(stripX + 132, zoomTop + 14, 20, 20));
        DrawText(dc, "in context: 24, 32, 16, 20", stripX, zoomTop + 56, 12, fgBrush);
    }

    private static BitmapSource Zoom(BitmapSource src, int factor)
    {
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        src.CopyPixels(pixels, stride, 0);
        int zw = w * factor, zh = h * factor, zstride = zw * 4;
        var outPixels = new byte[zstride * zh];
        for (var y = 0; y < zh; y++)
        {
            for (var xx = 0; xx < zw; xx++)
            {
                Buffer.BlockCopy(pixels, ((y / factor) * stride) + ((xx / factor) * 4), outPixels, (y * zstride) + (xx * 4), 4);
            }
        }

        var bmp = BitmapSource.Create(zw, zh, 96, 96, PixelFormats.Bgra32, null, outPixels, zstride);
        bmp.Freeze();
        return bmp;
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0);
        dc.DrawText(ft, new Point(x, y));
    }
}
