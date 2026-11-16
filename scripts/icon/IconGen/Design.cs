namespace BrowserFlip.IconGen;

/// <summary>
/// The BrowserFlip icon artwork. THIS FILE IS THE SINGLE SOURCE OF TRUTH: the SVG files in
/// assets/icon/ are emitted from these shapes, and the PNG/ICO files are rendered from them.
/// Canvas: 256x256, centre (128,128).
/// </summary>
internal static class Design
{
    // Palette (also documented in assets/icon/README.md).
    public const string TileLight = "#4CA6FF";
    public const string TileMid = "#2E7CF0";
    public const string TileDeep = "#1A4FD0";
    public const string Edge = "#0B2F7A";
    public const string White = "#FFFFFF";
    public const string AccentLight = "#FFD23F";
    public const string AccentDeep = "#FF8A1C";

    private static LinearPaint TileGradient(double from, double to, string light = TileLight) => new(
        "bf-tile", from, from, to, to,
        [new GradientStop(0, light), new GradientStop(1, TileDeep)]);

    private static LinearPaint Gloss(double top, double bottom) => new(
        "bf-gloss", 0, top, 0, bottom,
        [new GradientStop(0, White, 0.24), new GradientStop(1, White, 0)]);

    private static LinearPaint AccentGradient() => new(
        "bf-accent", 200, 140, 56, 200,
        [new GradientStop(0, AccentLight), new GradientStop(1, AccentDeep)]);

    /// <summary>Full-detail artwork used for 40 px and larger.</summary>
    public static Artwork Full()
    {
        const double tile = 12, tileSize = 232, radius = 52;
        const double globeR = 54, ringR = 84;
        const double whiteFrom = 196, whiteTo = 326, accentFrom = 16, accentTo = 146;

        var white = new SolidPaint(White);
        var accent = AccentGradient();

        return new Artwork(
            "browserflip",
            "BrowserFlip icon (master, 40 px and up)",
            [
                new Shape { Kind = ShapeKind.Rect, Comment = "tile", X = tile, Y = tile, W = tileSize, H = tileSize, Rx = radius, Fill = TileGradient(tile, tile + tileSize) },
                new Shape { Kind = ShapeKind.Rect, Comment = "tile gloss", X = tile, Y = tile, W = tileSize, H = tileSize, Rx = radius, Fill = Gloss(tile, 140) },
                new Shape { Kind = ShapeKind.Rect, Comment = "tile edge", X = tile + 1, Y = tile + 1, W = tileSize - 2, H = tileSize - 2, Rx = radius - 1, Stroke = new SolidPaint(Edge, 0.3), StrokeWidth = 2 },

                new Shape { Kind = ShapeKind.Ellipse, Comment = "globe body", X = Geo.C, Y = Geo.C, Rx = globeR, Ry = globeR, Fill = new SolidPaint(White, 0.16) },
                new Shape { Kind = ShapeKind.Path, Comment = "globe parallel (north)", D = Geo.Chord(globeR, -27), Stroke = white, StrokeWidth = 6 },
                new Shape { Kind = ShapeKind.Path, Comment = "globe parallel (south)", D = Geo.Chord(globeR, 27), Stroke = white, StrokeWidth = 6 },
                new Shape { Kind = ShapeKind.Path, Comment = "globe equator", D = Geo.Chord(globeR, 0), Stroke = white, StrokeWidth = 7 },
                new Shape { Kind = ShapeKind.Path, Comment = "globe prime meridian", D = "M128,74 V182", Stroke = white, StrokeWidth = 6 },
                new Shape { Kind = ShapeKind.Ellipse, Comment = "globe meridians", X = Geo.C, Y = Geo.C, Rx = 25, Ry = globeR, Stroke = white, StrokeWidth = 7 },
                new Shape { Kind = ShapeKind.Ellipse, Comment = "globe outline", X = Geo.C, Y = Geo.C, Rx = globeR, Ry = globeR, Stroke = white, StrokeWidth = 9 },

                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (white) arc", D = Geo.Arc(ringR, whiteFrom, whiteTo), Stroke = white, StrokeWidth = 13 },
                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (white) head", D = Geo.ArrowHead(ringR, whiteTo, 19, 16, 30), Fill = white, Stroke = white, StrokeWidth = 4 },
                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (accent) arc", D = Geo.Arc(ringR, accentFrom, accentTo), Stroke = accent, StrokeWidth = 13 },
                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (accent) head", D = Geo.ArrowHead(ringR, accentTo, 19, 16, 30), Fill = accent, Stroke = accent, StrokeWidth = 4 },
            ]);
    }

    /// <summary>
    /// Simplified artwork for 20 to 32 px: bigger tile, fewer globe lines, much thicker strokes,
    /// shorter arcs with bolder heads so the two arrows stay separate at 16 px.
    /// </summary>
    public static Artwork Small()
    {
        const double tile = 8, tileSize = 240, radius = 54;
        const double globeR = 47, ringR = 88;
        const double whiteFrom = 202, whiteTo = 318, accentFrom = 22, accentTo = 138;

        var white = new SolidPaint(White);
        var accent = new SolidPaint(AccentDeep);

        return new Artwork(
            "browserflip-small",
            "BrowserFlip icon (simplified, 20 to 32 px)",
            [
                new Shape { Kind = ShapeKind.Rect, Comment = "tile", X = tile, Y = tile, W = tileSize, H = tileSize, Rx = radius, Fill = TileGradient(tile, tile + tileSize, TileMid) },

                new Shape { Kind = ShapeKind.Path, Comment = "globe equator", D = Geo.Chord(globeR, 0), Stroke = white, StrokeWidth = 12 },
                new Shape { Kind = ShapeKind.Ellipse, Comment = "globe meridian", X = Geo.C, Y = Geo.C, Rx = 17, Ry = globeR, Stroke = white, StrokeWidth = 12 },
                new Shape { Kind = ShapeKind.Ellipse, Comment = "globe outline", X = Geo.C, Y = Geo.C, Rx = globeR, Ry = globeR, Stroke = white, StrokeWidth = 16 },

                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (white) arc", D = Geo.Arc(ringR, whiteFrom, whiteTo), Stroke = white, StrokeWidth = 20 },
                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (white) head", D = Geo.ArrowHead(ringR, whiteTo, 26, 22, 38), Fill = white, Stroke = white, StrokeWidth = 4 },
                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (accent) arc", D = Geo.Arc(ringR, accentFrom, accentTo), Stroke = accent, StrokeWidth = 20 },
                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (accent) head", D = Geo.ArrowHead(ringR, accentTo, 26, 22, 38), Fill = accent, Stroke = accent, StrokeWidth = 4 },
            ]);
    }

    /// <summary>
    /// Pixel-fitted artwork for exactly 16 px (1 px = 16 units). The tile fills the whole pixel grid,
    /// and the globe is a 1 px outline with a 1 px "#" grid whose lines sit on pixel centres so they stay crisp.
    /// </summary>
    public static Artwork Tiny()
    {
        const double globeR = 56, grid = 24, ringR = 98;
        const double whiteFrom = 210, whiteTo = 306, accentFrom = 30, accentTo = 126;

        var white = new SolidPaint(White);
        var accent = new SolidPaint(AccentDeep);
        var half = Math.Sqrt((globeR * globeR) - (grid * grid));

        return new Artwork(
            "browserflip-16",
            "BrowserFlip icon (pixel-fitted, 16 px)",
            [
                new Shape { Kind = ShapeKind.Rect, Comment = "tile", X = 0, Y = 0, W = 256, H = 256, Rx = 56, Fill = TileGradient(0, 256, TileMid) },

                new Shape { Kind = ShapeKind.Path, Comment = "globe grid (parallels)", D = Geo.Chord(globeR, -grid) + " " + Geo.Chord(globeR, grid), Stroke = white, StrokeWidth = 16, LineCap = "butt" },
                new Shape { Kind = ShapeKind.Path, Comment = "globe grid (meridians)", D = "M" + Geo.F(Geo.C - grid) + "," + Geo.F(Geo.C - half) + " V" + Geo.F(Geo.C + half) + " M" + Geo.F(Geo.C + grid) + "," + Geo.F(Geo.C - half) + " V" + Geo.F(Geo.C + half), Stroke = white, StrokeWidth = 16, LineCap = "butt" },
                new Shape { Kind = ShapeKind.Ellipse, Comment = "globe outline", X = Geo.C, Y = Geo.C, Rx = globeR, Ry = globeR, Stroke = white, StrokeWidth = 16 },

                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (white) arc", D = Geo.Arc(ringR, whiteFrom, whiteTo), Stroke = white, StrokeWidth = 24 },
                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (white) head", D = Geo.ArrowHead(ringR, whiteTo, 24, 22, 38), Fill = white, Stroke = white, StrokeWidth = 2 },
                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (accent) arc", D = Geo.Arc(ringR, accentFrom, accentTo), Stroke = accent, StrokeWidth = 24 },
                new Shape { Kind = ShapeKind.Path, Comment = "flip arrow (accent) head", D = Geo.ArrowHead(ringR, accentTo, 24, 22, 38), Fill = accent, Stroke = accent, StrokeWidth = 2 },
            ]);
    }
}
