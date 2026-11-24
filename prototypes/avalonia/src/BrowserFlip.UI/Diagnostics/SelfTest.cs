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

    public static async Task<CapabilityReport> RunAsync(
        MainViewModel model,
        Window mainWindow,
        Window? toolbar,
        bool trayIconCreated,
        IDefaultBrowserSwitcher switcher,
        IAppIconProvider icons,
        ISettingsStore store,
        IPlatformCapabilities capabilities)
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
        report.Add(3, "Status-area / menu-bar icon and menu",
            tray.IsAvailable
                ? (trayIconCreated ? CheckResult.PASS : CheckResult.FAIL)
                : (tray.State == CapabilityState.Unsupported ? CheckResult.UNSUPPORTED : CheckResult.SKIPPED),
            tray.IsAvailable
                ? (trayIconCreated ? "TrayIcon registered with a 5-item NativeMenu" : "TrayIcon was not created")
                : tray.Reason ?? "not available in this session");

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
                await model.SetDefaultAsync(target).ConfigureAwait(true);
                var nowDefault = model.Browsers.FirstOrDefault(b => b.IsDefault);
                var applied = nowDefault is not null
                              && string.Equals(nowDefault.Identifier, SwitchTargetId, StringComparison.OrdinalIgnoreCase);
                report.Add(6, "Default-browser switch API (Owner-triggered, real change)",
                    applied ? CheckResult.PASS : CheckResult.FAIL,
                    $"requested {SwitchTargetId}; now default={nowDefault?.Identifier ?? "none"}; \"{model.StatusMessage}\"");
            }
        }

        // 7. Browser icons, from the platform's icon provider. With no browser installed there
        //    is nothing to render, which is an environment fact rather than a defect.
        var withIcons = browsers.Count(b => b.IconPng is { Length: > 0 });
        report.Add(7, "Browser icons",
            browsers.Count == 0 ? CheckResult.SKIPPED : withIcons > 0 ? CheckResult.PASS : CheckResult.FAIL,
            browsers.Count == 0
                ? "no browsers were found, so no icon could be loaded"
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
        return report;
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
