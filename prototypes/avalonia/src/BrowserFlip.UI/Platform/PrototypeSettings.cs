namespace BrowserFlip.UI.Platform;

/// <summary>The settings the prototype persists (deliberately a small subset of the product's).</summary>
public sealed record PrototypeSettings
{
    /// <summary>UI language: <c>en</c>, <c>ja</c>, <c>zh-CN</c>, or <c>null</c> to follow the OS.</summary>
    public string? Language { get; init; }

    /// <summary>Whether the main window stays above other windows.</summary>
    public bool Topmost { get; init; }

    /// <summary>Whether the floating toolbar is shown.</summary>
    public bool ShowFloatingToolbar { get; init; }

    /// <summary>Last floating-toolbar position, in desktop coordinates.</summary>
    public double ToolbarX { get; init; } = 120;

    public double ToolbarY { get; init; } = 120;

    /// <summary>How many times the prototype has been started (proves persistence across runs).</summary>
    public int LaunchCount { get; init; }

    public static PrototypeSettings Default { get; } = new();
}
