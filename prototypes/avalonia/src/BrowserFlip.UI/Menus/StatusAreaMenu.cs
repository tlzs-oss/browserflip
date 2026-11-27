using BrowserFlip.UI.Localization;

namespace BrowserFlip.UI.Menus;

/// <summary>
/// One entry of the status-area menu: the resource key its label came from, the label itself and
/// what choosing it does. A separator carries no action.
/// </summary>
/// <param name="Key">The resource key, so a test can prove the label was looked up and not typed.</param>
/// <param name="Label">The label as the user will read it, in the current UI culture.</param>
/// <param name="Invoke">What choosing it does; <c>null</c> for a separator.</param>
public sealed record StatusMenuEntry(string Key, string Label, Action? Invoke)
{
    /// <summary>The one separator the menu uses.</summary>
    public static StatusMenuEntry Separator { get; } = new("-", "-", null);

    /// <summary>Whether this entry is the separator rather than a command.</summary>
    public bool IsSeparator => Invoke is null;
}

/// <summary>
/// The definition of the status-area (menu-bar / notification-area / StatusNotifierItem) menu.
/// <para>
/// It is a list rather than inline construction so that the menu has exactly one definition, and
/// so that its labels and its bindings can be tested without a display. The Owner's macOS
/// acceptance found the menu unusable (MAC-D6) and the automated run could not see it, partly
/// because nothing testable existed: the items, their translations and their handlers were all
/// written inline at the point where the native menu was built. One of them - the exit item -
/// was still an English literal, which the live menu bar showed next to five translated items.
/// </para>
/// </summary>
public static class StatusAreaMenu
{
    /// <summary>
    /// The menu in display order. Every label comes from the resources; nothing here is a
    /// literal the user can read.
    /// </summary>
    public static IReadOnlyList<StatusMenuEntry> Build(
        Action showMainWindow,
        Action refresh,
        Action toggleToolbar,
        Action showSettings,
        Action quit)
    {
        ArgumentNullException.ThrowIfNull(showMainWindow);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(toggleToolbar);
        ArgumentNullException.ThrowIfNull(showSettings);
        ArgumentNullException.ThrowIfNull(quit);

        return
        [
            new StatusMenuEntry("AppTitle", Strings.Get("AppTitle"), showMainWindow),
            new StatusMenuEntry("Refresh", Strings.Get("Refresh"), refresh),
            new StatusMenuEntry("Menu_ShowToolbar", Strings.Get("Menu_ShowToolbar"), toggleToolbar),
            new StatusMenuEntry("Menu_Settings", Strings.Get("Menu_Settings"), showSettings),
            StatusMenuEntry.Separator,
            new StatusMenuEntry("Menu_Exit", Strings.Get("Menu_Exit"), quit),
        ];
    }

    /// <summary>
    /// The resource keys of the application menu bar, for the platforms that can show one. Kept
    /// beside the status-area menu so one test covers the translations of both.
    /// </summary>
    public static IReadOnlyList<string> ApplicationMenuKeys { get; } =
        ["MenuBar_File", "MenuBar_View", "MenuBar_Settings", "MenuBar_RefreshBrowsers", "Menu_Settings", "Menu_ShowToolbar"];
}
