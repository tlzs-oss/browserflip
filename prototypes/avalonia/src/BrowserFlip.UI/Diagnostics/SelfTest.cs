using System.Globalization;
using Avalonia;
using Avalonia.Controls;
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
        string? trayToolTip = null)
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

        await CaptureWindowAsync(mainWindow, report).ConfigureAwait(true);
        await CaptureSettingsAsync(model, capabilities, report).ConfigureAwait(true);
        return report;
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
        MainViewModel model, IPlatformCapabilities capabilities, CapabilityReport report)
    {
        if (string.IsNullOrEmpty(OutputDirectory))
        {
            return;
        }

        try
        {
            var window = new Views.SettingsWindow
            {
                DataContext = new SettingsViewModel(model, capabilities),
            };
            window.Show();
            await Task.Delay(400).ConfigureAwait(true);

            var path = Path.Combine(OutputDirectory, "settings-window.png");
            var width = (int)Math.Max(1, window.Bounds.Width);
            var height = (int)Math.Max(1, window.Bounds.Height);
            using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
            {
                bitmap.Render(window);
                bitmap.Save(path, new PngBitmapEncoderOptions());
            }

            window.Close();
            report.Add(12, "Settings window renders with the capability table", CheckResult.PASS,
                $"{width}x{height}; {capabilities.All().Count} capabilities listed; {path}");
        }
#pragma warning disable CA1031 // A diagnostic capture must never fail the run.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report.Add(12, "Settings window renders with the capability table", CheckResult.FAIL, ex.Message);
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
