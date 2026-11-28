using Avalonia.Controls;
using Avalonia.Input;
using BrowserFlip.UI.Localization;

namespace BrowserFlip.UI.Menus;

/// <summary>
/// The macOS application menu - the one under the product's name at the left of the system menu
/// bar - and the live items whose labels a language change has to re-read.
/// </summary>
/// <param name="Menu">The menu handed to Avalonia, which hands it to AppKit.</param>
/// <param name="Items">Every item whose header is a resource lookup, with the key it came from.</param>
public sealed record MacApplicationMenuDefinition(
    NativeMenu Menu,
    IReadOnlyList<(NativeMenuItem Item, string Key)> Items);

/// <summary>
/// BrowserFlip's macOS application menu.
/// <para>
/// The Owner's acceptance found three defects in it: it offered Avalonia's <c>About Avalonia</c>
/// instead of the product's own About, it had no settings item, and it stayed in English when the
/// UI was switched to Japanese. An earlier round recorded these as an upstream limitation that
/// could not be worked around. <b>That conclusion was wrong</b>, and this type is the correction;
/// the measurement that overturned it is in <c>docs/macos-menu-bar.md</c>.
/// </para>
/// <para>
/// Two things about Avalonia 12.1.4 shape everything here, both read out of its source rather
/// than guessed:
/// </para>
/// <list type="number">
///   <item>
///   The application menu must be attached to the <see cref="Avalonia.Application"/> from
///   <c>Initialize()</c>. <c>AppBuilder.SetupUnsafe</c> runs the windowing subsystem, then creates
///   the Application and calls <c>Initialize()</c>, and only then runs the <c>AfterSetup</c>
///   callback in which the macOS backend exports the menu. An attach made later - in
///   <c>OnFrameworkInitializationCompleted</c>, where the previous round made it - arrives after
///   the backend has already read <c>NativeMenu.GetMenu(Application.Current)</c>, found nothing,
///   installed its own <c>About Avalonia</c> default and bound the native menu to that.
///   </item>
///   <item>
///   Avalonia then <i>appends</i> the standard macOS items - Services, Hide, Hide Others, Show
///   All, Quit - to whichever menu it exported, which is this one. They are English literals in
///   its own source. Rather than switch that off and reimplement them (which would need its
///   internal <c>INativeApplicationCommands</c> and its internal Services-submenu property),
///   BrowserFlip lets Avalonia build and wire them and then relabels them, so the behaviour, the
///   Services submenu and the key equivalents stay Avalonia's and only the text becomes ours.
///   </item>
/// </list>
/// </summary>
public static class MacApplicationMenu
{
    /// <summary>The product's own About item. The shipping product's resource, not a new string.</summary>
    public const string AboutKey = "About_Title";

    /// <summary>The settings item the Owner found missing.</summary>
    public const string SettingsKey = "Menu_Settings";

    /// <summary>The keys whose values take the application name as <c>{0}</c>.</summary>
    private const string HideAppKey = "Menu_HideApp";

    /// <summary>
    /// macOS says Quit where Windows says Exit, so the application menu cannot reuse the
    /// product's <c>Menu_Exit</c> ("Exit BrowserFlip"). This is the prototype's own key; the
    /// shipping product's resources are not touched. In Japanese and Chinese the two read the
    /// same, and only English differs.
    /// </summary>
    private const string QuitAppKey = "Menu_QuitApp";

    /// <summary>
    /// The items Avalonia appends, keyed by the exact English header its source gives them, with
    /// the resource key BrowserFlip relabels each one to.
    /// <para>
    /// Matching on the header rather than on the position is deliberate: an upstream change to
    /// the order would silently relabel the wrong items, whereas an upstream change to a label
    /// leaves that one item in English - visible, still working, and caught by the self-test.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string EnglishHeader, string Key)> StandardItems(string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        return
        [
            ("Services", "Menu_Services"),
            // Avalonia composes this one from Application.Name.
            ("Hide " + applicationName, HideAppKey),
            ("Hide Others", "Menu_HideOthers"),
            ("Show All", "Menu_ShowAll"),
            ("Quit", QuitAppKey),
        ];
    }

    /// <summary>
    /// The label for <paramref name="key"/> in the current UI culture, with the access-key marker
    /// stripped because a native menu renders it literally.
    /// </summary>
    public static string Label(string key, string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        return key is HideAppKey or QuitAppKey
            ? Strings.Format(key, applicationName).Replace("_", string.Empty, StringComparison.Ordinal)
            : StatusAreaMenu.Plain(key);
    }

    /// <summary>
    /// Builds the two items BrowserFlip contributes, in macOS order: About first, then Settings.
    /// Everything below them is Avalonia's, appended when the menu is exported.
    /// </summary>
    public static MacApplicationMenuDefinition Create(
        Action showAbout,
        Action showSettings,
        string applicationName)
    {
        ArgumentNullException.ThrowIfNull(showAbout);
        ArgumentNullException.ThrowIfNull(showSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        var about = new NativeMenuItem(Label(AboutKey, applicationName));
        about.Click += (_, _) => showAbout();

        var settings = new NativeMenuItem(Label(SettingsKey, applicationName))
        {
            // The macOS standard for a settings item, which the Owner asked for by name. AppKit
            // itself uses it, so anything else would be wrong on this platform.
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
        };
        settings.Click += (_, _) => showSettings();

        var menu = new NativeMenu();
        menu.Add(about);
        menu.Add(settings);

        return new MacApplicationMenuDefinition(menu, [(about, AboutKey), (settings, SettingsKey)]);
    }

    /// <summary>
    /// Finds the items Avalonia appended to <paramref name="menu"/> and pairs each with the
    /// resource key it should read from. Run once, before the first relabel: it identifies them
    /// by the English header Avalonia gave them, which a relabel replaces.
    /// <para>
    /// It also corrects one upstream defect while it is there. Avalonia gives Hide Others the
    /// gesture <c>⌥⌘Q</c>; the macOS standard is <c>⌥⌘H</c>, and <c>⌥⌘Q</c> is a keystroke away
    /// from Quit's <c>⌘Q</c>. The Owner asked for standard macOS shortcuts, so this is set to the
    /// standard one.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(NativeMenuItem Item, string Key)> Adopt(NativeMenu menu, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        var expected = StandardItems(applicationName)
            .ToDictionary(x => x.EnglishHeader, x => x.Key, StringComparer.Ordinal);
        var adopted = new List<(NativeMenuItem, string)>();

        foreach (var element in menu.Items)
        {
            // NativeMenuItemSeparator derives from NativeMenuItem, so it has to be excluded by
            // type rather than by filtering on the element type.
            if (element is NativeMenuItem item and not NativeMenuItemSeparator
                && item.Header is { } header
                && expected.TryGetValue(header, out var key))
            {
                adopted.Add((item, key));

                if (key == "Menu_HideOthers")
                {
                    item.Gesture = new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt);
                }
            }
        }

        return adopted;
    }

    /// <summary>
    /// Re-reads every label from the resources. The items stay the same objects, so AppKit sees
    /// a title change on items it already holds - Avalonia observes <c>NativeMenuItem.Header</c>
    /// and pushes it through - rather than a menu replacement, which is the upstream crash of
    /// AvaloniaUI/Avalonia#22287.
    /// </summary>
    public static void Relabel(
        IReadOnlyList<(NativeMenuItem Item, string Key)> items,
        string applicationName)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        foreach (var (item, key) in items)
        {
            item.Header = Label(key, applicationName);
        }
    }
}
