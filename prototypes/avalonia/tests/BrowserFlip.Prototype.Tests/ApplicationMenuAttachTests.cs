using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BrowserFlip.UI;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// Where the macOS application menu is attached, which is the whole reason the previous attempt
/// did not work.
/// <para>
/// <c>AppBuilder.SetupUnsafe</c> runs <c>Application.Initialize()</c>, then the <c>AfterSetup</c>
/// callback in which Avalonia's macOS backend exports the application menu, and only then
/// <c>OnFrameworkInitializationCompleted</c>. The export reads
/// <c>NativeMenu.GetMenu(Application.Current)</c> exactly once: a menu attached after it has run
/// is never exported, and Avalonia's own "About Avalonia" default stays in the menu bar. The
/// previous round attached it from <c>OnFrameworkInitializationCompleted</c> - one step too late
/// - and concluded from the result that macOS could not have an application menu at all.
/// </para>
/// <para>
/// So this asserts the one thing that goes wrong invisibly: that <c>Initialize()</c> on its own,
/// with nothing else called, has already attached the menu. Moving the attach back into
/// <c>OnFrameworkInitializationCompleted</c> fails here. That the backend then exports it is a
/// macOS runtime fact and is measured on the device by self-test check 14.
/// </para>
/// </summary>
public class ApplicationMenuAttachTests
{
    [AvaloniaFact]
    public void Initialize_alone_attaches_the_application_menu_on_macos()
    {
        var app = new App();

        app.Initialize();

        var menu = NativeMenu.GetMenu(app);
        if (!OperatingSystem.IsMacOS())
        {
            // Windows and Linux have no application-level native menu, and an
            // Application-attached NativeMenu there is read by nothing; theirs is the window
            // menu, built in OnFrameworkInitializationCompleted.
            Assert.Null(menu);
            return;
        }

        Assert.NotNull(menu);
        var commands = menu.Items
            .Where(i => i is NativeMenuItem and not NativeMenuItemSeparator)
            .Cast<NativeMenuItem>()
            .ToList();
        Assert.Equal(2, commands.Count);
        Assert.All(commands, item => Assert.False(string.IsNullOrWhiteSpace(item.Header)));
        // Avalonia appends the standard items to this menu when it exports it, so what is here
        // now is BrowserFlip's own contribution and nothing else.
        Assert.DoesNotContain(commands, item =>
            item.Header!.Contains("Avalonia", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_products_name_is_set_before_the_backend_reads_it()
    {
        var app = new App();

        app.Initialize();

        // Avalonia's SetupApplicationName() and its "Hide <name>" item both read this during the
        // same AfterSetup callback. Left unset, macOS calls the application "Avalonia
        // Application" in the menu bar and in the crash reporter - measured.
        Assert.Equal("BrowserFlip", app.Name);
    }
}
