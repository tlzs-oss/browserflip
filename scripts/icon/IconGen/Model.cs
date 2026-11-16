using System.Globalization;

namespace BrowserFlip.IconGen;

/// <summary>A paint: either a solid color or a linear gradient in artwork (user-space) coordinates.</summary>
internal abstract record Paint;

internal sealed record SolidPaint(string Hex, double Opacity = 1) : Paint;

internal sealed record GradientStop(double Offset, string Hex, double Opacity = 1);

/// <summary>Linear gradient; coordinates are absolute in the 256x256 artwork space (SVG userSpaceOnUse).</summary>
internal sealed record LinearPaint(string Id, double X1, double Y1, double X2, double Y2, IReadOnlyList<GradientStop> Stops) : Paint;

internal enum ShapeKind
{
    Rect,
    Ellipse,
    Path,
}

/// <summary>
/// One drawable primitive. Only a tiny subset of SVG is used so that the exact same data can be
/// emitted as SVG and rendered with WPF: rounded rect, ellipse/circle, and path data using M/L/H/V/A/Z.
/// </summary>
internal sealed record Shape
{
    public required ShapeKind Kind { get; init; }
    public required string Comment { get; init; }

    // Rect: X, Y, W, H, Rx. Ellipse: Cx, Cy, Rx, Ry.
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public double Rx { get; init; }
    public double Ry { get; init; }

    // Path data (SVG syntax, absolute commands only).
    public string? D { get; init; }

    public Paint? Fill { get; init; }
    public Paint? Stroke { get; init; }
    public double StrokeWidth { get; init; }
    public string LineCap { get; init; } = "round";
    public string LineJoin { get; init; } = "round";
}

internal sealed record Artwork(string Name, string Description, IReadOnlyList<Shape> Shapes);

/// <summary>Small geometry helpers. All angles are in degrees, measured clockwise from +X (SVG y-down).</summary>
internal static class Geo
{
    public const double C = 128;

    public static string F(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    public static (double X, double Y) Polar(double angleDeg, double r)
    {
        var a = angleDeg * Math.PI / 180;
        return (C + (r * Math.Cos(a)), C + (r * Math.Sin(a)));
    }

    public static string P((double X, double Y) p) => F(p.X) + "," + F(p.Y);

    /// <summary>Clockwise circular arc around the centre from a1 to a2.</summary>
    public static string Arc(double r, double a1, double a2)
    {
        var large = (a2 - a1) > 180 ? 1 : 0;
        return "M" + P(Polar(a1, r)) + " A" + F(r) + "," + F(r) + " 0 " + large.ToString(CultureInfo.InvariantCulture) + " 1 " + P(Polar(a2, r));
    }

    /// <summary>
    /// Arrowhead that continues a clockwise arc of radius r ending at angle a. The base straddles the
    /// arc (outer/inner half-widths may differ) and the tip lies on the arc, so the head follows the curve.
    /// </summary>
    public static string ArrowHead(double r, double a, double halfOut, double halfIn, double length)
    {
        var tipAngle = a + (length / r * 180 / Math.PI);
        return "M" + P(Polar(a, r + halfOut)) + " L" + P(Polar(tipAngle, r)) + " L" + P(Polar(a, r - halfIn)) + " Z";
    }

    /// <summary>Horizontal chord of a circle (radius r, centred) at vertical offset dy, inset by the stroke.</summary>
    public static string Chord(double r, double dy)
    {
        var half = Math.Sqrt((r * r) - (dy * dy));
        return "M" + F(C - half) + "," + F(C + dy) + " H" + F(C + half);
    }
}
