using Avalonia.Controls;
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
    /// The application menu bar, for the platforms that can show one, as top-level entries each
    /// holding one child. Built from the same kind of definition as the status-area menu so that
    /// one test covers the labels of both, and so that adding an item cannot quietly escape the
    /// translation check the way a hand-maintained key list could.
    /// </summary>
    /// <remarks>
    /// Not used on macOS: Avalonia exports the application menu before the Application object
    /// exists, so a later NativeMenu.SetMenu(Application, ...) is ignored there - measured, see
    /// docs/macos-menu-bar.md.
    /// </remarks>
    public static IReadOnlyList<(StatusMenuEntry Parent, StatusMenuEntry Child)> BuildApplicationMenu(
        Action refresh,
        Action toggleToolbar,
        Action showSettings)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(toggleToolbar);
        ArgumentNullException.ThrowIfNull(showSettings);

        return
        [
            (Header("MenuBar_File"), new StatusMenuEntry("MenuBar_RefreshBrowsers", Plain("MenuBar_RefreshBrowsers"), refresh)),
            (Header("MenuBar_View"), new StatusMenuEntry("Menu_ShowToolbar", Plain("Menu_ShowToolbar"), toggleToolbar)),
            (Header("MenuBar_Settings"), new StatusMenuEntry("Menu_Settings", Plain("Menu_Settings"), showSettings)),
        ];
    }

    /// <summary>
    /// Turns a definition into the real <see cref="NativeMenu"/> the platform is given. It lives
    /// here, rather than inline where the tray icon is built, so that a test can assert the order,
    /// the separator's position and the handler wiring of the menu that is actually handed over -
    /// otherwise "one definition, no drift" would hold only up to the list and not past it.
    /// </summary>
    public static NativeMenu ToNativeMenu(IReadOnlyList<StatusMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var menu = new NativeMenu();
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            var item = new NativeMenuItem(entry.Label);
            var invoke = entry.Invoke!;
            item.Click += (_, _) => invoke();
            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>A top-level header, which opens its child menu rather than doing something itself.</summary>
    private static StatusMenuEntry Header(string key) => new(key, Plain(key), () => { });

    /// <summary>
    /// A label with the access-key marker removed. The product's menu-bar resources carry them
    /// ("_File", "ファイル(_F)"), and they are stripped for every native menu item rather than
    /// for some of them: before this, the three headers were English literals with no marker
    /// while their children were stripped, and localising the headers without stripping would
    /// have put a literal underscore on screen beside an already-stripped child.
    /// </summary>
    public static string Plain(string key)
        => Strings.Get(key).Replace("_", string.Empty, StringComparison.Ordinal);
}
