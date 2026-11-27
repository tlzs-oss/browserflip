namespace BrowserFlip.UI.Platform;

/// <summary>
/// What the operating system itself says about the status-area item, read back from the live
/// system rather than inferred from the fact that BrowserFlip asked for one.
/// </summary>
/// <param name="ItemFound">An item belonging to this process is really in the status area.</param>
/// <param name="ItemVisible">It is visible, not hidden or collapsed.</param>
/// <param name="HasImage">It carries an image, so it is not a blank placeholder.</param>
/// <param name="ToolTip">The tooltip the OS holds for it, used to tell it from other items.</param>
/// <param name="Detail">Everything measured, for the evidence line in the report.</param>
public sealed record StatusAreaFacts(
    bool ItemFound,
    bool ItemVisible,
    bool HasImage,
    string? ToolTip,
    string Detail)
{
    /// <summary>Nothing could be measured on this platform.</summary>
    public static StatusAreaFacts NotProbed(string reason) => new(false, false, false, null, reason);
}

/// <summary>
/// Reads the real status-area item back from the platform, and can deliver a real click to it.
/// <para>
/// This interface exists because "we created a <c>TrayIcon</c> and handed it to the toolkit" is
/// not evidence that anything is in the menu bar, and it is certainly not evidence that clicking
/// it does anything - the Owner's menu-bar acceptance failed on exactly that gap (MAC-D6). An
/// implementation must therefore use the platform's own documented API, never BrowserFlip's own
/// intent, and must not need any permission the application does not already hold.
/// </para>
/// </summary>
public interface IStatusAreaProbe
{
    /// <summary>Measures the live item. <paramref name="expectedToolTip"/> identifies it.</summary>
    StatusAreaFacts Inspect(string expectedToolTip);

    /// <summary>
    /// Delivers a genuine primary click to the item through the platform's own event queue, so
    /// the whole routing path is exercised, and reports what it did. Returns <c>false</c> when
    /// the item could not be found; <paramref name="detail"/> always explains the outcome.
    /// </summary>
    bool TryClick(string expectedToolTip, out string detail);
}
