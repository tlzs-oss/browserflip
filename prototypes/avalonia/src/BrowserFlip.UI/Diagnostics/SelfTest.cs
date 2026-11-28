using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using BrowserFlip.Core.Defaults;
using BrowserFlip.UI.Localization;
using BrowserFlip.UI.Platform;
using BrowserFlip.UI.ViewModels;

namespace BrowserFlip.UI.Diagnostics;

/// <summary>
/// Runs the R2 capability checks inside the live application: the real window is on screen, the
/// real platform adapters are in place and the real OS answers the queries. Requirement 6
/// (actually changing the default browser) is reported as SKIPPED — a real system change needs
/// the Owner to trigger it explicitly, so the automated path only proves the API is present.
/// </summary>
public static class SelfTest
{
    /// <summary>Set by the entry point when <c>--selftest</c> is passed; a directory for the output.</summary>
    public static string? OutputDirectory { get; set; }

    /// <summary>Set by <c>--switch-default &lt;id&gt;</c>: an explicit, Owner-triggered real change.</summary>
    public static string? SwitchTargetId { get; set; }

    /// <summary>
    /// Set by the entry point when <c>--settings-path</c> replaced the real per-user settings
    /// file. Diagnostics that persist a change may only run when this is true.
    /// </summary>
    public static bool SettingsAreIsolated { get; set; }

    /// <summary>
    /// Set by <c>--show-settings [language]</c>: open the settings window the way the user does,
    /// report what actually landed on screen, and then stay running so it can be looked at.
    /// <para>
    /// This exists because the Owner could not find the settings screen and an automated PASS is
    /// not an answer to that. It chooses the real status-area menu item rather than constructing
    /// a window, so what appears is what choosing the menu item appears.
    /// </para>
    /// </summary>
    public static bool ShowSettingsRequested { get; set; }

    /// <summary>The language to switch to first, or <c>null</c> to leave it alone.</summary>
    public static string? ShowSettingsLanguage { get; set; }

    /// <summary>
    /// Opens the settings window through the status-area menu item and prints what is on screen.
    /// Returns the window, or <c>null</c> when the item opened nothing.
    /// </summary>
    /// <summary>
    /// Whether a window of this kind is open, closing every one that is. The check opens them
    /// only to prove the native menu items are wired to this application, so it must not leave
    /// any behind for the checks that follow.
    /// </summary>
    private static bool CloseAll<T>(IClassicDesktopStyleApplicationLifetime? lifetime)
        where T : Window
    {
        var windows = lifetime?.Windows.OfType<T>().ToList() ?? [];
        foreach (var window in windows)
        {
            window.Close();
        }

        return windows.Count > 0;
    }

    /// <summary>
    /// Prints what the real macOS menu bar holds for each UI language in turn, switching the
    /// language live rather than restarting.
    /// <para>
    /// The Owner's third complaint about this menu was that it stayed in English after the UI was
    /// switched to Japanese. A startup-language check cannot show that is fixed, because the
    /// defect is in the switch: the labels are resource lookups made when the menu was built, so
    /// without a relabel the menu keeps whatever language it started in. This reads the titles
    /// back out of <c>[NSApp mainMenu]</c> after each switch, so what is printed is what macOS
    /// would draw.
    /// </para>
    /// </summary>
    public static async Task ReportApplicationMenuAsync(IStatusAreaProbe? probe, MainViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (probe is null || !OperatingSystem.IsMacOS())
        {
            Console.WriteLine("the application menu cannot be read back on this platform");
            return;
        }

        // Setting Language persists it, so cycling through the languages writes the settings file
        // once per language. Against the real per-user file that would rewrite the user's own
        // choice to prove a point about a menu, so it only runs when --settings-path has put an
        // isolated file in its place. Without one, the menu is read once and left alone.
        if (!SettingsAreIsolated)
        {
            var current = probe.InspectApplicationMenu();
            Console.WriteLine($"  [{model.Language ?? "follow the OS"}] " + string.Join(
                " | ", current.Items.Where(i => i.Title.Length > 0).Select(i => i.Title)));
            Console.WriteLine("  (not switching language: that would rewrite the real settings file. "
                + "Pass --settings-path to see every language.)");
            return;
        }

        var original = model.Language;
        try
        {
            foreach (var language in Strings.SupportedLanguages)
            {
                model.Language = language;

                // The relabel runs on this same dispatcher, and AppKit is told item by item, so
                // let it settle before reading the menu back.
                await Task.Delay(250).ConfigureAwait(true);

                var facts = probe.InspectApplicationMenu();
                Console.WriteLine($"  [{language}] " + (facts.Found
                    ? string.Join(" | ", facts.Items.Select(i => i.Title.Length == 0
                        ? "-"
                        : i.Title + (i.Shortcut.Length == 0 ? string.Empty : " " + i.Shortcut)))
                    : "NOT FOUND: " + facts.Detail));
            }
        }
        finally
        {
            model.Language = original;
            await Task.Delay(250).ConfigureAwait(true);
        }

        var restored = probe.InspectApplicationMenu();
        Console.WriteLine($"  [restored to {original ?? "follow the OS"}] " + string.Join(
            " | ", restored.Items.Where(i => i.Title.Length > 0).Select(i => i.Title)));
    }

    public static async Task<Window?> ShowSettingsThroughTheMenuAsync(
        MainViewModel model, NativeMenu? trayMenu, IReadOnlyList<Menus.StatusMenuEntry>? trayEntries)
    {
        ArgumentNullException.ThrowIfNull(model);

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var index = trayEntries is null
            ? -1
            : trayEntries.ToList().FindIndex(e => string.Equals(e.Key, "Menu_Settings", StringComparison.Ordinal));
        var item = trayMenu is not null && index >= 0 && index < trayMenu.Items.Count
            ? trayMenu.Items[index] as NativeMenuItem
            : null;

        if (lifetime is null || item is null)
        {
            Console.WriteLine("there is no status-area menu on this platform, so there is no item to choose");
            return null;
        }

        Console.WriteLine($"choosing the status-area menu item \"{item.Header}\" ...");
        ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();

        // Activation is asynchronous, so measuring in the same dispatcher callback that asked
        // for it reports active=False whatever happens. An earlier version of this did exactly
        // that and the reading was worthless.
        await Task.Delay(1200).ConfigureAwait(true);

        var settings = lifetime.Windows.OfType<Views.SettingsWindow>().FirstOrDefault();
        var main = lifetime.MainWindow;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  main window     visible={main?.IsVisible} topmost={main?.Topmost} active={main?.IsActive} "
            + $"pos={main?.Position.X},{main?.Position.Y} size={main?.Bounds.Width:F0}x{main?.Bounds.Height:F0}"));
        if (settings is null)
        {
            Console.WriteLine("  settings window DID NOT OPEN; open windows: "
                + string.Join(", ", lifetime.Windows.Select(w => w.GetType().Name)));
            return null;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  settings window visible={settings.IsVisible} topmost={settings.Topmost} active={settings.IsActive} "
            + $"pos={settings.Position.X},{settings.Position.Y} size={settings.Bounds.Width:F0}x{settings.Bounds.Height:F0} "
            + $"title=\"{settings.Title}\""));

        // What the language picker is actually showing, read off the live window's own view
        // model: the Owner's screenshot had a Japanese window whose "follow the system" entry
        // still read Chinese.
        if (settings.DataContext is SettingsViewModel vm)
        {
            Console.WriteLine("  language picker now reads: "
                + string.Join(" | ", vm.LanguageChoices.Select(c => c.Display)));
            Console.WriteLine($"  selected: \"{vm.SelectedLanguage.Display}\"  (stored language: {model.Language ?? "follow the OS"})");
        }

        // The question the Owner's report raises: if the main window is kept above everything and
        // the settings window is not, the settings window opens behind it and nothing appears to
        // have happened.
        if (main?.Topmost == true && !settings.Topmost)
        {
            Console.WriteLine("  *** the main window is always-on-top and the settings window is NOT:"
                + " the settings window is below it ***");
        }

        // The rest of the Owner's regression list, exercised on the real windows rather than
        // asserted from the code: choosing the item again must not open a second window, closing
        // it must release it, and choosing it afterwards must open it again.
        ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
        await Task.Delay(400).ConfigureAwait(true);
        var afterSecondChoice = lifetime.Windows.OfType<Views.SettingsWindow>().Count();
        Console.WriteLine($"  choosing it again -> {afterSecondChoice} settings window(s) open"
            + (afterSecondChoice == 1 ? " (not duplicated)" : " *** DUPLICATED ***"));

        settings.Close();
        await Task.Delay(400).ConfigureAwait(true);
        Console.WriteLine($"  after closing    -> {lifetime.Windows.OfType<Views.SettingsWindow>().Count()} settings window(s) open");

        ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
        await Task.Delay(1200).ConfigureAwait(true);
        var reopened = lifetime.Windows.OfType<Views.SettingsWindow>().FirstOrDefault();
        Console.WriteLine(reopened is null
            ? "  reopening        -> *** FAILED to reopen ***"
            : string.Create(CultureInfo.InvariantCulture,
                $"  reopening        -> open again, visible={reopened.IsVisible} topmost={reopened.Topmost} active={reopened.IsActive}"));

        // And the button the Owner approved, on the main window, reaching the same single window.
        if (main is Views.MainWindow mainTyped)
        {
            var button = mainTyped.FindControl<Button>("SettingsButton");
            Console.WriteLine($"  main window's settings button reads \"{button?.Content}\" "
                + $"(main window title \"{mainTyped.Title}\")");

            var before = lifetime.Windows.OfType<Views.SettingsWindow>().Count();
            mainTyped.RequestSettingsForAcceptance();
            await Task.Delay(400).ConfigureAwait(true);
            var after = lifetime.Windows.OfType<Views.SettingsWindow>().Count();
            Console.WriteLine($"  main-window \"settings\" button -> {after} settings window(s) open"
                + (after == before && after == 1 ? " (same single window)" : " *** unexpected ***"));
        }

        Console.Out.Flush();
        return reopened ?? settings;
    }

    /// <summary>
    /// True only for the unattended, Owner-triggered real change: an automated self-test run
    /// that also names a browser to switch to.
    /// <para>
    /// <b>Both</b> flags are required on purpose. <c>--switch-default</c> on its own must not
    /// turn an ordinary interactive session into one where BrowserFlip stops asking - the host
    /// uses this to decide whether command-line consent stands in for the dialog, and widening
    /// it to the GUI would quietly remove the consent step the product promises.
    /// </para>
    /// </summary>
    public static bool OwnerTriggeredSwitch
        => !string.IsNullOrEmpty(OutputDirectory) && !string.IsNullOrEmpty(SwitchTargetId);

    public static async Task<CapabilityReport> RunAsync(
        MainViewModel model,
        Window mainWindow,
        Window? toolbar,
        bool trayIconCreated,
        IDefaultBrowserSwitcher switcher,
        IAppIconProvider icons,
        ISettingsStore store,
        IPlatformCapabilities capabilities,
        IStatusAreaProbe? statusArea = null,
        NativeMenu? trayMenu = null,
        string? trayToolTip = null,
        IReadOnlyList<Menus.StatusMenuEntry>? trayEntries = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(capabilities);

        var report = new CapabilityReport
        {
            Platform = capabilities.Description,
            OsVersion = Environment.OSVersion.VersionString,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            AvaloniaVersion = typeof(Application).Assembly.GetName().Version?.ToString() ?? "unknown",
            Timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };

        // 1. Avalonia builds and runs on this OS and architecture.
        report.Add(1, "Avalonia builds and runs on this platform", CheckResult.PASS,
            $"process is live; Avalonia {report.AvaloniaVersion}; {report.Architecture}; {capabilities.Description}");

        // 2. A native desktop window is actually open and sized by the platform.
        var open = mainWindow.IsVisible && mainWindow.Bounds.Width > 0 && mainWindow.Bounds.Height > 0;
        report.Add(2, "Native desktop window displayed", open ? CheckResult.PASS : CheckResult.FAIL,
            $"IsVisible={mainWindow.IsVisible} bounds={mainWindow.Bounds.Width:F0}x{mainWindow.Bounds.Height:F0} "
            + $"handle={(mainWindow.TryGetPlatformHandle()?.Handle.ToString("X", CultureInfo.InvariantCulture) ?? "none")}");

        // 3. Status-area item with a menu. Avalonia maps TrayIcon onto NSStatusItem (macOS),
        //    the notification area (Windows) and StatusNotifierItem (Linux). Where the platform
        //    declares it unsupported - GNOME without the AppIndicator extension, for example -
        //    not having one is the correct outcome, not a failure.
        var tray = capabilities.TrayIcon;
        var menuTitles = trayMenu is null
            ? []
            : trayMenu.Items.Select(i => i is NativeMenuItem item ? item.Header ?? "(no header)" : "(separator)").ToList();
        if (!tray.IsAvailable)
        {
            report.Add(3, "Status-area / menu-bar icon and menu",
                tray.State == CapabilityState.Unsupported ? CheckResult.UNSUPPORTED : CheckResult.SKIPPED,
                tray.Reason ?? "not available in this session");
        }
        else if (statusArea is null)
        {
            // No way to read the item back on this platform, so say exactly that instead of
            // letting "BrowserFlip asked for an icon" read as "the shell is showing one".
            report.Add(3, "Status-area / menu-bar icon and menu",
                trayIconCreated ? CheckResult.PASS : CheckResult.FAIL,
                trayIconCreated
                    ? $"TrayIcon created with a {menuTitles.Count}-item NativeMenu and handed to the platform. "
                      + "This platform cannot read the item back from the shell, so visible rendering and click "
                      + "behaviour are NOT verified by this check."
                    : "TrayIcon was not created");
        }
        else
        {
            // Measured, not inferred: the item is read back out of the live status area.
            var facts = statusArea.Inspect(trayToolTip ?? string.Empty);
            var ok = trayIconCreated && facts.ItemFound && facts.ItemVisible && facts.HasImage && menuTitles.Count > 0;
            report.Add(3, "Status-area / menu-bar icon and menu",
                ok ? CheckResult.PASS : CheckResult.FAIL,
                $"{facts.Detail}; menu carries {menuTitles.Count} item(s): " + string.Join(" | ", menuTitles));
        }

        // 4. Installed browsers, read from the platform's own registry of handlers.
        //    A machine with no browser installed (a bare CI runner) is an environment fact, not a
        //    defect, so it is reported as SKIPPED with the count rather than as a failure.
        var browsers = model.Browsers;
        report.Add(4, "Installed browser list",
            browsers.Count > 0 ? CheckResult.PASS : CheckResult.SKIPPED,
            browsers.Count > 0
                ? $"{browsers.Count} found: " + string.Join(", ", browsers.Take(8).Select(b => b.DisplayName))
                : "no web browser is installed on this machine, so there was nothing to enumerate");

        // 5. Current default browser.
        var current = browsers.FirstOrDefault(b => b.IsDefault);
        report.Add(5, "Current default browser",
            model.Status != DefaultStatus.None ? CheckResult.PASS
                : browsers.Count == 0 ? CheckResult.SKIPPED : CheckResult.FAIL,
            $"status={model.Status}; default={current?.DisplayName ?? "(not one of the detected browsers)"}; "
            + $"text=\"{model.CurrentDefaultText}\"");

        // 6. The officially supported switch API. Not invoked unless the Owner asked for it.
        if (switcher.Mechanism == SwitchMechanism.Unsupported)
        {
            report.Add(6, "Default-browser switch API", CheckResult.UNSUPPORTED,
                "NSWorkspace setDefaultApplicationAtURL:… is unavailable (macOS 12+ required)");
        }
        else if (string.IsNullOrEmpty(SwitchTargetId))
        {
            // The wording has to follow the platform: saying "NSWorkspace" on Linux, as an earlier
            // version did, is simply false.
            var how = switcher.Mechanism switch
            {
                SwitchMechanism.SystemConsentPrompt => "public NSWorkspace API; macOS shows its own consent prompt",
                SwitchMechanism.OpensSystemSettings => "opens the Windows Settings page; the user confirms there",
                SwitchMechanism.CommandLineTool =>
                    "xdg-settings; the OS does NOT confirm, so BrowserFlip must ask the user first",
                _ => "no supported mechanism",
            };
            report.Add(6, "Default-browser switch API", CheckResult.SKIPPED,
                $"available via {switcher.Mechanism} ({how}). "
                + "Not invoked: a real system change requires --switch-default <id>, triggered by the Owner.");
        }
        else
        {
            var target = browsers.FirstOrDefault(b =>
                string.Equals(b.Identifier, SwitchTargetId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                report.Add(6, "Default-browser switch API", CheckResult.FAIL,
                    $"no installed browser has identifier '{SwitchTargetId}'");
            }
            else if (target.IsDefault)
            {
                // Nothing would happen, so a PASS here would be meaningless.
                report.Add(6, "Default-browser switch API (Owner-triggered, real change)", CheckResult.SKIPPED,
                    $"{SwitchTargetId} is already the default; pick a different browser to exercise a real change.");
            }
            else
            {
                // Where consent came from is recorded BEFORE the call, from the object the view
                // model will actually ask. On a platform whose OS does not prompt, this run is
                // consented by the command line and no dialog is shown; the report has to say
                // so rather than let the PASS read as though someone clicked "yes".
                var consentSource = model.ConsentSource;
                await model.SetDefaultAsync(target).ConfigureAwait(true);

                // The OS consent alert is answered by a human, on human time: 44 s and 474 s
                // were both measured on this Mac, and the alert outlives this process. The view
                // model leaves a background watch running for exactly that, so the self-test
                // waits for the real answer instead of grading its own impatience.
                await model.PendingChange.ConfigureAwait(true);

                model.Refresh();
                var nowDefault = model.Browsers.FirstOrDefault(b => b.IsDefault);
                var applied = nowDefault is not null
                              && string.Equals(nowDefault.Identifier, SwitchTargetId, StringComparison.OrdinalIgnoreCase);

                // Three outcomes, not two. "Not applied yet" is PENDING: the request was
                // accepted, the dialog has not been answered, and nothing has been refused.
                // Reporting that as FAIL is what made an earlier run print the opposite of
                // what macOS then did.
                var last = model.LastSwitchResult;
                var result = applied ? CheckResult.PASS
                    : last?.Outcome is SwitchOutcome.Declined or SwitchOutcome.Failed
                      or SwitchOutcome.BrowserUnavailable ? CheckResult.FAIL
                    : CheckResult.PENDING;
                report.Add(6, "Default-browser switch API (Owner-triggered, real change)",
                    result,
                    $"requested {SwitchTargetId}; switcher outcome={last?.Outcome.ToString() ?? "none"}; "
                    + $"OS read back \"{last?.ObservedDefault ?? "not read"}\"; "
                    + $"detail=\"{last?.Error ?? "-"}\"; "
                    + $"now default={nowDefault?.Identifier ?? "none"}; "
                    + $"consent came from {consentSource}; \"{model.StatusMessage}\"");
            }
        }

        // 7. Browser icons, from the platform's icon provider. With no browser installed there
        //    is nothing to render, which is an environment fact rather than a defect.
        var withIcons = browsers.Count(b => b.IconPng is { Length: > 0 });
        // Zero icons is not necessarily a defect. On Linux the provider returns null for SVG by
        // design, and SVG-only is now the norm for GTK applications - GNOME Web ships no PNG at
        // all. Grading that FAIL would fail the run for a documented limitation.
        var iconResult = browsers.Count == 0 ? CheckResult.SKIPPED
            : withIcons > 0 ? CheckResult.PASS
            : OperatingSystem.IsLinux() ? CheckResult.UNSUPPORTED
            : CheckResult.FAIL;
        report.Add(7, "Browser icons", iconResult,
            browsers.Count == 0
                ? "no browsers were found, so no icon could be loaded"
                : withIcons == 0 && OperatingSystem.IsLinux()
                    ? $"0/{browsers.Count} decoded: every candidate icon is SVG, and there is no SVG rasteriser"
                    : $"{withIcons}/{browsers.Count} decoded as PNG; sizes: "
                      + string.Join(", ", browsers.Where(b => b.IconPng is not null).Take(4).Select(b => $"{b.IconPng!.Length}B")));

        // 8. en / ja / zh-CN, read from the product's own .resx files.
        const string probeKey = "Refresh";
        var translations = new List<string>();
        var original = model.Language;
        foreach (var language in Strings.SupportedLanguages)
        {
            Strings.UseLanguage(language);
            translations.Add(Strings.Get(probeKey));
        }

        Strings.UseLanguage(original);
        // Compare the TRANSLATIONS, not the language tags: tagging each sample would make the
        // set distinct by construction and the check could never fail. A translation equal to
        // the resource key means the resource did not resolve at all.
        var distinct = translations.Distinct(StringComparer.Ordinal).Count();
        var allResolved = translations.TrueForAll(t => !string.IsNullOrWhiteSpace(t) && t != probeKey);
        report.Add(8, "English / Japanese / Simplified Chinese",
            distinct == Strings.SupportedLanguages.Count && allResolved ? CheckResult.PASS : CheckResult.FAIL,
            string.Join("  ", Strings.SupportedLanguages.Zip(translations, (l, t) => $"{l}=\"{t}\"")));

        // 9. Topmost window and floating toolbar. Wayland gives a client no way to ask to stay
        //    on top or to place itself, so there the honest answer is UNSUPPORTED, not FAIL.
        var onTop = capabilities.AlwaysOnTop;
        var positioning = capabilities.WindowPositioning;
        if (!onTop.IsAvailable || !positioning.IsAvailable)
        {
            var reason = !onTop.IsAvailable ? onTop.Reason : positioning.Reason;
            report.Add(9, "Topmost window and floating toolbar", CheckResult.UNSUPPORTED,
                reason ?? "not supported by this windowing system");
        }
        else
        {
            mainWindow.Topmost = true;
            var toolbarShown = toolbar is not null && toolbar.IsVisible;
            report.Add(9, "Topmost window and floating toolbar",
                mainWindow.Topmost && toolbarShown ? CheckResult.PASS : CheckResult.FAIL,
                $"main.Topmost={mainWindow.Topmost}; toolbar visible={toolbarShown}, topmost={toolbar?.Topmost}, "
                + $"decorations={toolbar?.WindowDecorations}, size={toolbar?.Bounds.Width:F0}x{toolbar?.Bounds.Height:F0}");
            mainWindow.Topmost = model.Topmost;
        }

        // 10. Settings persistence: write a probe, re-read it from disk, then put the user's own
        // settings back. The self-test must not leave the real configuration modified.
        var originalSettings = store.Load();
        var probe = 1234 + (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 1000);
        var saved = store.Save(new PrototypeSettings { Language = "ja", Topmost = true, LaunchCount = probe });
        var reloaded = store.Load();
        var restored = store.Save(originalSettings);
        var roundTripped = saved && restored
                           && reloaded.LaunchCount == probe && reloaded.Language == "ja" && reloaded.Topmost;
        report.Add(10, "Settings persistence", roundTripped ? CheckResult.PASS : CheckResult.FAIL,
            $"{store.Location}: wrote LaunchCount={probe}, read back {reloaded.LaunchCount}, "
            + $"Language={reloaded.Language}; original settings restored={restored}");

        // 11. The platform's own declaration, so the report explains the results above.
        report.Add(11, "Declared platform capabilities", CheckResult.PASS,
            string.Join("; ", capabilities.All().Select(c =>
                $"{c.Name}={c.Capability.State}{(c.Capability.Reason is null ? "" : $" ({c.Capability.Reason})")}"))
            + $"; OS confirms default-browser change={capabilities.OsConfirmsDefaultBrowserChange}");

        // 14. The macOS application menu, read back out of the live menu bar.
        AddApplicationMenuCheck(report, statusArea);

        await CaptureWindowAsync(mainWindow, report).ConfigureAwait(true);
        await CaptureSettingsAsync(model, capabilities, report, trayMenu, trayEntries).ConfigureAwait(true);
        return report;
    }


    /// <summary>
    /// Check 14: the application menu macOS is really showing.
    /// <para>
    /// The Owner reported this menu as wrong three times - Avalonia's "About Avalonia", no
    /// settings item, English text in a Japanese UI - while every automated run passed, because
    /// nothing read the menu back. The managed <c>NativeMenu</c> could not have caught it either:
    /// the defect was that Avalonia accepted the managed menu and never exported it, so the model
    /// looked right and the menu bar was wrong. This check reads <c>[NSApp mainMenu]</c>.
    /// </para>
    /// <para>
    /// It then chooses the About item through AppKit's own
    /// <c>-[NSMenu performActionForItemAtIndex:]</c> and confirms BrowserFlip's own About window
    /// opened, which is the only evidence that the native item is wired to this application and
    /// not merely present with the right text.
    /// </para>
    /// </summary>
    private static void AddApplicationMenuCheck(CapabilityReport report, IStatusAreaProbe? statusArea)
    {
        const string Name = "Application menu (macOS menu bar)";

        if (!OperatingSystem.IsMacOS())
        {
            report.Add(14, Name, CheckResult.UNSUPPORTED,
                "only macOS has an application-level menu bar; on Windows and Linux the same "
                + "commands are the window's own menu and the status-area menu.");
            return;
        }

        if (statusArea is null)
        {
            report.Add(14, Name, CheckResult.SKIPPED, "no probe was supplied, so the menu cannot be read back.");
            return;
        }

        var facts = statusArea.InspectApplicationMenu();
        if (!facts.Found)
        {
            report.Add(14, Name, CheckResult.FAIL, facts.Detail);
            return;
        }

        var applicationName = Application.Current?.Name ?? "BrowserFlip";
        var titles = facts.Items.Where(i => i.Title.Length > 0).Select(i => i.Title).ToList();

        var expected = new List<string>
        {
            Menus.MacApplicationMenu.Label(Menus.MacApplicationMenu.AboutKey, applicationName),
            Menus.MacApplicationMenu.Label(Menus.MacApplicationMenu.SettingsKey, applicationName),
        };
        expected.AddRange(Menus.MacApplicationMenu.StandardItems(applicationName)
            .Select(s => Menus.MacApplicationMenu.Label(s.Key, applicationName)));

        var missing = expected.Where(e => !titles.Contains(e, StringComparer.Ordinal)).ToList();

        // An item still carrying the English header Avalonia gave it, in a UI that is not in
        // English, is the third defect the Owner reported. Only counted where the localised
        // label actually differs, so this stays silent on an English UI.
        var notTranslated = Menus.MacApplicationMenu.StandardItems(applicationName)
            .Where(s => titles.Contains(s.EnglishHeader, StringComparer.Ordinal)
                && !string.Equals(
                    s.EnglishHeader,
                    Menus.MacApplicationMenu.Label(s.Key, applicationName),
                    StringComparison.Ordinal))
            .Select(s => s.EnglishHeader)
            .ToList();

        // "About Avalonia" is the exact item the Owner saw. The product's menu must not name the
        // toolkit at all, so any Avalonia-titled item here is a failure.
        var toolkitNamed = titles.Where(x => x.Contains("Avalonia", StringComparison.Ordinal)).ToList();

        var settingsLabel = Menus.MacApplicationMenu.Label(Menus.MacApplicationMenu.SettingsKey, applicationName);
        var settingsShortcut = facts.Items.FirstOrDefault(i =>
            string.Equals(i.Title, settingsLabel, StringComparison.Ordinal))?.Shortcut ?? string.Empty;

        // Both of BrowserFlip's own items are chosen through AppKit, and the window each is
        // supposed to open is looked for. Reading the labels back is not enough: the whole defect
        // was a menu that looked right and was not connected to this application. The settings
        // item needs this as much as About does - it is a third route to the settings window, and
        // the other two are covered by their own checks.
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var aboutLabel = Menus.MacApplicationMenu.Label(Menus.MacApplicationMenu.AboutKey, applicationName);
        var aboutInvoked = statusArea.TryInvokeApplicationMenuItem(aboutLabel, out var aboutDetail);
        var aboutOpened = CloseAll<Views.AboutWindow>(lifetime);
        var settingsInvoked = statusArea.TryInvokeApplicationMenuItem(settingsLabel, out var settingsDetail);
        var settingsOpened = CloseAll<Views.SettingsWindow>(lifetime);

        var ok = missing.Count == 0
            && notTranslated.Count == 0
            && toolkitNamed.Count == 0
            && settingsShortcut == "\u2318,"
            && aboutInvoked
            && aboutOpened
            && settingsInvoked
            && settingsOpened;

        var evidence = new System.Text.StringBuilder(facts.Detail);
        evidence.Append(CultureInfo.InvariantCulture,
            $"; UI culture={CultureInfo.CurrentUICulture.Name}; settings shortcut=\"{settingsShortcut}\"");
        if (missing.Count > 0)
        {
            evidence.Append("; MISSING: ").Append(string.Join(" | ", missing));
        }

        if (notTranslated.Count > 0)
        {
            evidence.Append("; NOT TRANSLATED: ").Append(string.Join(" | ", notTranslated));
        }

        if (toolkitNamed.Count > 0)
        {
            evidence.Append("; NAMES THE TOOLKIT: ").Append(string.Join(" | ", toolkitNamed));
        }

        evidence.Append(CultureInfo.InvariantCulture, $"; chose \"{aboutLabel}\": {aboutDetail} ")
            .Append(aboutOpened
                ? "BrowserFlip's own About window opened."
                : "NO About window opened, so the native item is not wired to this application.");
        evidence.Append(CultureInfo.InvariantCulture, $" chose \"{settingsLabel}\": {settingsDetail} ")
            .Append(settingsOpened
                ? "the settings window opened."
                : "NO settings window opened, so the native item is not wired to this application.");

        report.Add(14, Name, ok ? CheckResult.PASS : CheckResult.FAIL, evidence.ToString());
    }
    /// <summary>
    /// Delivers a real click to the status-area item, records in the report whether the menu
    /// actually opened, rewrites the report files, and ends the process.
    /// <para>
    /// This is the check that the Owner's menu-bar acceptance needed and the report did not have:
    /// the icon was present and the menu never opened (MAC-D6), and nothing in the automated run
    /// could tell. The evidence is the platform's own notification that the menu is opening, so a
    /// menu that is attached but unreachable fails here.
    /// </para>
    /// <para>
    /// It runs last, after the other checks, and finishes on a background thread: an open native
    /// menu runs its own modal tracking loop on the UI thread, so nothing scheduled there could
    /// be relied on to write the result or to shut the application down. Because of that it also
    /// owns the final write of both report files - appending a line after the summary, as this
    /// first did, left the JSON report and the RESULT line claiming a clean run while the menu
    /// was broken.
    /// </para>
    /// </summary>
    public static void VerifyMenuOpensThenExit(
        IStatusAreaProbe probe, NativeMenu trayMenu, string trayToolTip, CapabilityReport report, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(trayMenu);
        ArgumentNullException.ThrowIfNull(report);

        var opened = 0;
        var closed = 0;
        void OnOpening(object? sender, EventArgs e) => Interlocked.Exchange(ref opened, 1);
        void OnClosed(object? sender, EventArgs e) => Interlocked.Exchange(ref closed, 1);
        trayMenu.Opening += OnOpening;
        trayMenu.Closed += OnClosed;

        var posted = probe.TryClick(trayToolTip, out var clickDetail);

        _ = Task.Run(async () =>
        {
            try
            {
                if (posted)
                {
                    // The platform raises "opening" synchronously while it builds the menu, so a
                    // short wait is enough; it is generous rather than tight because a slow
                    // machine must not turn a working menu into a failure.
                    for (var waited = 0; waited < 50 && Volatile.Read(ref opened) == 0; waited++)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }

                var fired = Volatile.Read(ref opened) == 1;
                // Not being able to deliver the click is not the same as delivering it and
                // getting nothing. macOS hides status items when the menu bar is crowded, and
                // grading that as a product defect would fail a working build; check 3 already
                // FAILs when the item cannot be found, so nothing is hidden by saying so here.
                var result = !posted ? CheckResult.SKIPPED : fired ? CheckResult.PASS : CheckResult.FAIL;
                report.Add(13, "Status-area menu opens on a real click", result,
                    $"{clickDetail}; NativeMenu.Opening raised={fired}; NativeMenu.Closed raised={Volatile.Read(ref closed) == 1}");

                trayMenu.Opening -= OnOpening;
                trayMenu.Closed -= OnClosed;

                Directory.CreateDirectory(outputDirectory);
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, "capability-report.json"), report.ToJson())
                    .ConfigureAwait(false);
                var table = report.ToTable();
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, "capability-report.txt"), table)
                    .ConfigureAwait(false);
                Console.WriteLine(table);
                Console.Out.Flush();
            }
#pragma warning disable CA1031 // The process must still end, with a code that says it went wrong.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Console.Error.WriteLine("the menu-click check could not be completed: " + ex);
                Environment.Exit(2);
            }

            // Environment.Exit, not the application lifetime: if the menu really did open, the
            // UI thread is inside the platform's modal menu loop and will not run anything.
            Environment.Exit(report.ExitCode);
        });
    }

    /// <summary>
    /// Renders the real settings window, with the real capability table for this platform.
    /// Opening it through the menu cannot be automated without an Accessibility grant, so the
    /// self-test constructs and renders the same window the menu would open.
    /// </summary>
    private static async Task CaptureSettingsAsync(
        MainViewModel model, IPlatformCapabilities capabilities, CapabilityReport report,
        NativeMenu? trayMenu, IReadOnlyList<Menus.StatusMenuEntry>? trayEntries)
    {
        if (string.IsNullOrEmpty(OutputDirectory))
        {
            return;
        }

        const string Name = "Settings window opens from the status-area menu";
        try
        {
            // Opened the way the user opens it - by choosing the item in the status-area menu -
            // rather than by constructing the window here. The Owner's acceptance could not
            // confirm the settings screen, and "the self-test built a SettingsWindow" says
            // nothing about whether the menu item reaches one. The other two routes - the main
            // window's button and, on macOS, the application menu's "Settings..." - are covered
            // by their own checks, and all three must reach the same single window.
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var index = trayEntries is null
                ? -1
                : trayEntries.ToList().FindIndex(e => string.Equals(e.Key, "Menu_Settings", StringComparison.Ordinal));
            var item = trayMenu is not null && index >= 0 && index < trayMenu.Items.Count
                ? trayMenu.Items[index] as NativeMenuItem
                : null;

            if (lifetime is null)
            {
                report.Add(12, Name, CheckResult.SKIPPED, "there is no desktop lifetime to open a window from");
                return;
            }

            // Where there is no status area - a Linux session with no StatusNotifierWatcher, for
            // instance - the menu item does not exist, but the main window's button does, and it
            // is the same route to the same window. Falling back keeps the check meaningful on
            // exactly the session the capability model exists to describe; before this it
            // degraded to SKIPPED and produced no settings screenshot at all.
            string route;
            if (item is not null)
            {
                route = $"the status-area menu item \"{item.Header}\"";
                ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            }
            else if (lifetime.MainWindow is Views.MainWindow fallback)
            {
                route = "the main window's settings button (no status area in this session)";
                fallback.RequestSettingsForAcceptance();
            }
            else
            {
                report.Add(12, Name, CheckResult.SKIPPED,
                    "there is neither a status-area menu nor a main window to open the settings window from");
                return;
            }

            await Task.Delay(400).ConfigureAwait(true);

            var window = lifetime.Windows.OfType<Views.SettingsWindow>().FirstOrDefault();
            if (window is null)
            {
                report.Add(12, Name, CheckResult.FAIL,
                    $"choosing {route} opened no settings window; "
                    + $"open windows: {string.Join(", ", lifetime.Windows.Select(w => w.GetType().Name))}");
                return;
            }

            var path = Path.Combine(OutputDirectory, "settings-window.png");
            var width = (int)Math.Max(1, window.Bounds.Width);
            var height = (int)Math.Max(1, window.Bounds.Height);
            using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
            {
                bitmap.Render(window);
                bitmap.Save(path, new PngBitmapEncoderOptions());
            }

            // The capability table is what the window exists to show, so an empty one is a
            // failure even though the window opened.
            var listed = (window.DataContext as SettingsViewModel)?.Capabilities.Count ?? 0;
            report.Add(12, Name, listed > 0 ? CheckResult.PASS : CheckResult.FAIL,
                $"choosing {route} opened {window.GetType().Name} \"{window.Title}\", "
                + $"{width}x{height}, visible={window.IsVisible}; {listed} capabilities listed; {path}");
            window.Close();
        }
#pragma warning disable CA1031 // A diagnostic capture must never fail the run.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report.Add(12, Name, CheckResult.FAIL, ex.Message);
        }
    }

    /// <summary>
    /// Renders the live window to PNG from inside the process. macOS screen recording needs a
    /// TCC grant that a build agent does not have, so the application captures itself instead.
    /// </summary>
    private static async Task CaptureWindowAsync(Window window, CapabilityReport report)
    {
        if (string.IsNullOrEmpty(OutputDirectory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(OutputDirectory);
            var path = Path.Combine(OutputDirectory, "prototype-window.png");
            var width = (int)Math.Max(1, window.Bounds.Width);
            var height = (int)Math.Max(1, window.Bounds.Height);

            // Two frames: the first lets the compositor finish the initial layout pass.
            for (var pass = 0; pass < 2; pass++)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(path, new PngBitmapEncoderOptions());
                if (pass == 0)
                {
                    await Task.Delay(400).ConfigureAwait(true);
                }
            }

            report.RenderedWindowPng = path;
        }
#pragma warning disable CA1031 // The report must still be written when rendering is unavailable.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report.RenderedWindowPng = "render failed: " + ex.Message;
        }
    }
}
