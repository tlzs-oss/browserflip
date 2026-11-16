using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrowserFlip.IconGen;

/// <summary>Renders an <see cref="Artwork"/> with WPF and encodes PNG files.</summary>
internal static class Renderer
{
    public static BitmapSource Render(Artwork art, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
            foreach (var s in art.Shapes)
            {
                Geometry geometry = s.Kind switch
                {
                    ShapeKind.Rect => new RectangleGeometry(new Rect(s.X, s.Y, s.W, s.H), s.Rx, s.Rx),
                    ShapeKind.Ellipse => new EllipseGeometry(new Point(s.X, s.Y), s.Rx, s.Ry),
                    ShapeKind.Path => Geometry.Parse("F1 " + s.D),
                    _ => throw new InvalidOperationException("Unknown shape kind."),
                };

                Pen? pen = null;
                if (s.Stroke is not null)
                {
                    var cap = s.LineCap == "round" ? PenLineCap.Round : PenLineCap.Flat;
                    pen = new Pen(ToBrush(s.Stroke), s.StrokeWidth)
                    {
                        StartLineCap = cap,
                        EndLineCap = cap,
                        LineJoin = s.LineJoin == "round" ? PenLineJoin.Round : PenLineJoin.Miter,
                    };
                }

                dc.DrawGeometry(s.Fill is null ? null : ToBrush(s.Fill), pen, geometry);
            }

            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return ToStraightAlpha(rtb);
    }

    public static BitmapSource ToStraightAlpha(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static Brush ToBrush(Paint paint)
    {
        Brush brush = paint switch
        {
            SolidPaint sp => new SolidColorBrush(ToColor(sp.Hex, sp.Opacity)),
            LinearPaint lp => new LinearGradientBrush(
                new GradientStopCollection(lp.Stops.Select(st => new System.Windows.Media.GradientStop(ToColor(st.Hex, st.Opacity), st.Offset))),
                new Point(lp.X1, lp.Y1),
                new Point(lp.X2, lp.Y2))
            {
                MappingMode = BrushMappingMode.Absolute,
                ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation,
            },
            _ => throw new InvalidOperationException("Unknown paint."),
        };
        brush.Freeze();
        return brush;
    }

    public static Color ToColor(string hex, double opacity = 1)
    {
        var rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb(
            (byte)Math.Round(opacity * 255),
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
    }
}
