using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserFlip.App.Desktop;

namespace BrowserFlip.App.Services;

/// <summary>Theme preference.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Which surface is shown.</summary>
public enum WindowMode
{
    Normal,
    Compact,
}

/// <summary>
/// Layout of the main window (v0.3 multi-view). Classic is the v0.2 window and the default for new users and for
/// settings files written before v0.3.
/// </summary>
public enum MainView
{
    Classic,
    List,
    Card,
    Compact,
    Panel,
}

/// <summary>A main-window size in DIP, remembered per view.</summary>
public readonly record struct ViewSize(double Width, double Height);

/// <summary>Size of the compact bar.</summary>
public enum BarSize
{
    Small,
    Medium,
}

/// <summary>
/// Per-user preferences stored in %LOCALAPPDATA%\BrowserFlip\settings.json.
/// Every field is read independently: a missing or invalid value falls back to its default
/// without discarding the others, so v0.1 files (language only) load unchanged.
/// </summary>
public sealed class UserSettings
{
    /// <summary>Default location of the settings file.</summary>
    public static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrowserFlip", "settings.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Where this instance is saved; <c>null</c> disables saving (used by tests).</summary>
    public string? FilePath { get; private set; }

    /// <summary>UI language ("en", "ja", "zh-CN") or empty to follow Windows.</summary>
    public string Language { get; set; } = string.Empty;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public WindowMode Mode { get; set; } = WindowMode.Normal;

    /// <summary>Always on top (applies to both surfaces).</summary>
    public bool Topmost { get; set; }

    /// <summary>
    /// Whether the compact bar is docked to a screen edge (default: right edge of the primary monitor).
    /// Set by dragging: near an edge docks, away from the edges floats.
    /// </summary>
    public bool DockEnabled { get; set; } = true;

    public DockEdge DockEdge { get; set; } = DockEdge.Right;

    /// <summary>Center of the docked bar along its edge (0..1).</summary>
    public double DockPosition { get; set; } = 0.5;

    /// <summary>Device name of the monitor the bar is docked on (e.g. \\.\DISPLAY2); empty = primary.</summary>
    public string DockMonitor { get; set; } = string.Empty;

    public BarSize BarSize { get; set; } = BarSize.Small;

    /// <summary>Orientation of the compact bar while floating (not docked).</summary>
    public BarOrientation FloatingOrientation { get; set; } = BarOrientation.Horizontal;

    /// <summary>
    /// Main window bounds in physical pixels, or null for the default position. Only the position is
    /// applied in pixels; the size is restored in DIP (<c>size / MainScale</c>) so DPI changes stay consistent.
    /// </summary>
    public PixelRect? MainBounds { get; set; }

    /// <summary>DPI scale of the monitor the main window was on when <see cref="MainBounds"/> was saved.</summary>
    public double MainScale { get; set; } = 1.0;

    /// <summary>
    /// BF-07: true = the user resized the main window (its width is kept), false = the width was chosen by the app
    /// (it follows the header), null = saved by a version before BF-07 (a width too small for the header is widened once).
    /// </summary>
    public bool? MainWidthByUser { get; set; }

    /// <summary>Floating compact bar position in physical pixels, or null.</summary>
    public PixelRect? CompactBounds { get; set; }

    /// <summary>
    /// Main window layout (v0.3). Saved whenever the user switches; a missing or unknown value falls back to Classic.
    /// </summary>
    public MainView PreferredView { get; set; } = MainView.Classic;

    /// <summary>
    /// Main window size (DIP) per non-classic view. The position is shared by every view (<see cref="MainBounds"/>);
    /// the classic view keeps its own v0.2 size rules in <see cref="MainBounds"/>.
    /// </summary>
    public Dictionary<MainView, ViewSize> ViewSizes { get; } = [];

    /// <summary>Creates in-memory settings that are never written to disk.</summary>
    public static UserSettings InMemory() => new();

    public static UserSettings Load(string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        var settings = new UserSettings { FilePath = path };
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject json)
            {
                settings.Apply(json);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or ArgumentException or InvalidOperationException)
        {
            // Corrupt or unreadable (incl. duplicate keys, which JsonObject rejects): keep defaults.
        }

        return settings;
    }

    public void Save()
    {
        if (FilePath is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, ToJson().ToJsonString(WriteOptions));
            File.Move(tmp, FilePath, overwrite: true); // replace in one step: no half-written file on crash
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are best-effort; failing to save must not break the app.
        }
    }

    internal JsonObject ToJson() => new()
    {
        [nameof(Language)] = Language,
        [nameof(Theme)] = Theme.ToString(),
        [nameof(Mode)] = Mode.ToString(),
        [nameof(Topmost)] = Topmost,
        [nameof(DockEnabled)] = DockEnabled,
        [nameof(DockEdge)] = DockEdge.ToString(),
        [nameof(DockPosition)] = DockPosition,
        [nameof(DockMonitor)] = DockMonitor,
        [nameof(BarSize)] = BarSize.ToString(),
        [nameof(FloatingOrientation)] = FloatingOrientation.ToString(),
        [nameof(MainBounds)] = RectToJson(MainBounds),
        [nameof(MainScale)] = MainScale,
        [nameof(MainWidthByUser)] = MainWidthByUser,
        [nameof(CompactBounds)] = RectToJson(CompactBounds),
        [nameof(PreferredView)] = PreferredView.ToString(),
        [nameof(ViewSizes)] = ViewSizesToJson(),
    };

    internal void Apply(JsonObject json)
    {
        Language = Str(json, nameof(Language)) is { Length: <= 16 } lang ? lang : string.Empty;
        Theme = EnumOr(json, nameof(Theme), Theme);
        Mode = EnumOr(json, nameof(Mode), Mode);
        Topmost = BoolOr(json, nameof(Topmost), Topmost);
        DockEnabled = BoolOr(json, nameof(DockEnabled), DockEnabled);
        DockEdge = EnumOr(json, nameof(DockEdge), DockEdge);
        DockPosition = Num(json, nameof(DockPosition)) is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : 0.5;
        DockMonitor = Str(json, nameof(DockMonitor)) is { Length: <= 64 } m ? m : string.Empty;
        BarSize = EnumOr(json, nameof(BarSize), BarSize);
        FloatingOrientation = EnumOr(json, nameof(FloatingOrientation), FloatingOrientation);
        MainBounds = RectFromJson(json[nameof(MainBounds)]);
        MainScale = Num(json, nameof(MainScale)) is { } sc && double.IsFinite(sc) && sc is >= 0.5 and <= 5 ? sc : 1.0;
        CompactBounds = RectFromJson(json[nameof(CompactBounds)]);
        MainWidthByUser = json[nameof(MainWidthByUser)] is JsonValue w && w.TryGetValue<bool>(out var byUser) ? byUser : null;
        PreferredView = EnumOr(json, nameof(PreferredView), MainView.Classic);
        ViewSizes.Clear();
        if (json[nameof(ViewSizes)] is JsonObject sizes)
        {
            foreach (var (name, node) in sizes)
            {
                if (!int.TryParse(name, out _) && Enum.TryParse<MainView>(name, ignoreCase: true, out var view) && Enum.IsDefined(view)
                    && view != MainView.Classic && node is JsonObject size
                    && Num(size, "Width") is { } width && Num(size, "Height") is { } height
                    && IsSaneLength(width) && IsSaneLength(height))
                {
                    ViewSizes[view] = new ViewSize(width, height);
                }
            }
        }
    }

    private JsonObject ViewSizesToJson()
    {
        var json = new JsonObject();
        foreach (var (view, size) in ViewSizes.OrderBy(p => p.Key))
        {
            json[view.ToString()] = new JsonObject { ["Width"] = Math.Round(size.Width, 1), ["Height"] = Math.Round(size.Height, 1) };
        }

        return json;
    }

    private static bool IsSaneLength(double dip) => double.IsFinite(dip) && dip is >= 100 and <= 20_000;

    private static string? Str(JsonObject json, string name)
        => json[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Num(JsonObject json, string name)
        => json[name] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static bool BoolOr(JsonObject json, string name, bool fallback)
        => json[name] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    private static T EnumOr<T>(JsonObject json, string name, T fallback)
        where T : struct, Enum
        => Str(json, name) is { } s && !int.TryParse(s, out _) && Enum.TryParse<T>(s, ignoreCase: true, out var e) && Enum.IsDefined(e) ? e : fallback;

    private static JsonObject? RectToJson(PixelRect? r)
        => r is { } v ? new JsonObject { ["Left"] = v.Left, ["Top"] = v.Top, ["Width"] = v.Width, ["Height"] = v.Height } : null;

    private static PixelRect? RectFromJson(JsonNode? node)
    {
        if (node is not JsonObject o)
        {
            return null;
        }

        const int limit = 100_000; // sanity bound for virtual-screen coordinates
        if (Int(o, "Left") is { } l && Int(o, "Top") is { } t && Int(o, "Width") is { } w && Int(o, "Height") is { } h
            && w is > 0 and < limit && h is > 0 and < limit && Math.Abs(l) < limit && Math.Abs(t) < limit)
        {
            return PixelRect.FromSize(l, t, w, h);
        }

        return null;
    }

    private static int? Int(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
}
