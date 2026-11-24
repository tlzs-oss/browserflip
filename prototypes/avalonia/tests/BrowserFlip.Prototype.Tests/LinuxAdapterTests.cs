using System.Globalization;
using BrowserFlip.Core.Detection;
using BrowserFlip.Platform.Linux;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// Tests for the Linux adapter. Everything here is pure logic over injected fakes — desktop-entry
/// text, a fake command runner in place of <c>xdg-settings</c>, an injected environment lookup —
/// so the whole suite runs and passes on macOS and Windows. No process is ever spawned and the
/// only files touched are inside a temporary directory each test creates and deletes.
/// <para>
/// What these tests cannot cover is whether a real Linux desktop behaves as the adapter assumes:
/// that needs a Linux machine and is listed in <c>docs/linux-feasibility.md</c> section 4.
/// </para>
/// </summary>
public class LinuxAdapterTests
{
    private const string Home = "/home/someone";

    /// <summary>A realistic entry: localised names, field codes, a second group, a trailing ';'.</summary>
    private const string FirefoxDesktop = """
        [Desktop Entry]
        Version=1.0
        Name=Firefox Web Browser
        Name[ja]=Firefox ウェブ・ブラウザー
        Name[pt_BR]=Navegador Web Firefox
        Comment=Browse the World Wide Web
        Exec=/usr/lib/firefox/firefox %u
        TryExec=/usr/lib/firefox/firefox
        Icon=firefox
        Terminal=false
        Type=Application
        MimeType=text/html;text/xml;x-scheme-handler/http;x-scheme-handler/https;
        Categories=Network;WebBrowser;

        [Desktop Action new-private-window]
        Name=Open a New Private Window
        Exec=/usr/lib/firefox/firefox --private-window %u
        Icon=private-browsing
        """;

    // ---------------------------------------------------------------- desktop entry parsing

    [Fact]
    public void The_keys_BrowserFlip_needs_are_read_from_the_desktop_entry_group()
    {
        var entry = DesktopEntry.Parse(FirefoxDesktop);

        Assert.NotNull(entry);
        Assert.Equal("Firefox Web Browser", entry.Name);
        Assert.Equal("/usr/lib/firefox/firefox %u", entry.Exec);
        Assert.Equal("/usr/lib/firefox/firefox", entry.TryExec);
        Assert.Equal("firefox", entry.Icon);
        Assert.False(entry.NoDisplay);
        Assert.False(entry.Hidden);
        Assert.Equal(
            new[] { "text/html", "text/xml", "x-scheme-handler/http", "x-scheme-handler/https" },
            entry.MimeTypes.ToArray());
    }

    [Fact]
    public void Only_the_desktop_entry_group_is_read()
    {
        var entry = DesktopEntry.Parse(FirefoxDesktop);

        // The [Desktop Action] group has its own Name/Exec/Icon; using those would mislabel the
        // browser as "Open a New Private Window".
        Assert.NotNull(entry);
        Assert.Equal("Firefox Web Browser", entry.Name);
        Assert.Equal("firefox", entry.Icon);
    }

    [Fact]
    public void Content_without_a_desktop_entry_group_is_not_a_desktop_entry()
    {
        Assert.Null(DesktopEntry.Parse("[Desktop Action foo]\nName=Nope\nExec=/bin/nope"));
        Assert.Null(DesktopEntry.Parse(string.Empty));
        Assert.Null(DesktopEntry.Parse(null));
        Assert.Null(DesktopEntry.Parse("just some text"));
    }

    [Fact]
    public void Comments_blank_lines_and_space_around_the_equals_sign_are_ignored()
    {
        var entry = DesktopEntry.Parse(
            "# a comment\n\n[Desktop Entry]\n# another\n  Name  =  Chromium  \nExec =/usr/bin/chromium %U\n");

        Assert.NotNull(entry);
        Assert.Equal("Chromium", entry.Name);
        Assert.Equal("/usr/bin/chromium", entry.ExecutableName);
    }

    [Theory]
    [InlineData("ja", "Firefox ウェブ・ブラウザー")]
    [InlineData("ja-JP", "Firefox ウェブ・ブラウザー")]
    [InlineData("pt-BR", "Navegador Web Firefox")]
    [InlineData("pt-PT", "Firefox Web Browser")]
    [InlineData("en-US", "Firefox Web Browser")]
    [InlineData("zh-CN", "Firefox Web Browser")]
    public void The_most_specific_localised_name_wins_and_falls_back_to_the_plain_name(string culture, string expected)
    {
        var entry = DesktopEntry.Parse(FirefoxDesktop);

        Assert.NotNull(entry);
        Assert.Equal(expected, entry.NameFor(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void A_region_specific_locale_key_is_matched_by_its_posix_spelling()
    {
        // The file spells it Name[pt_BR]; .NET spells the culture pt-BR.
        var entry = DesktopEntry.Parse("[Desktop Entry]\nName=Plain\nName[pt_BR]=Brasil\n");

        Assert.NotNull(entry);
        Assert.Equal("Brasil", entry.NameFor(CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Equal("Plain", entry.NameFor(CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void Escape_sequences_in_values_are_resolved()
    {
        var entry = DesktopEntry.Parse(@"[Desktop Entry]
Name=Two\sWords
Comment=Line\nBreak
Icon=back\\slash
");

        Assert.NotNull(entry);
        Assert.Equal("Two Words", entry.Name);
        Assert.Equal(@"back\slash", entry.Icon);
    }

    [Fact]
    public void A_semicolon_inside_a_mime_type_list_can_be_escaped()
    {
        var entry = DesktopEntry.Parse("[Desktop Entry]\nMimeType=x-scheme-handler/https;odd\\;type;\n");

        Assert.NotNull(entry);
        Assert.Equal(new[] { "x-scheme-handler/https", "odd;type" }, entry.MimeTypes.ToArray());
    }

    [Theory]
    [InlineData("NoDisplay=true", true, false)]
    [InlineData("Hidden=true", false, true)]
    [InlineData("NoDisplay=false", false, false)]
    [InlineData("NoDisplay=1", false, false)]
    [InlineData("NoDisplay=TRUE", false, false)]
    public void Only_the_literal_true_suppresses_an_entry(string line, bool noDisplay, bool hidden)
    {
        // The spec defines the values "true" and "false" exactly; anything else is invalid, and
        // guessing would hide a browser the user can see in their own menu.
        var entry = DesktopEntry.Parse($"[Desktop Entry]\nName=X\n{line}\n");

        Assert.NotNull(entry);
        Assert.Equal(noDisplay, entry.NoDisplay);
        Assert.Equal(hidden, entry.Hidden);
        Assert.Equal(noDisplay || hidden, entry.IsSuppressed);
    }

    [Fact]
    public void A_duplicate_key_keeps_the_first_value()
    {
        // Duplicate keys are invalid; being deterministic beats depending on read order.
        var entry = DesktopEntry.Parse("[Desktop Entry]\nName=First\nName=Second\n");

        Assert.NotNull(entry);
        Assert.Equal("First", entry.Name);
    }

    [Theory]
    [InlineData("/usr/bin/firefox %u", "/usr/bin/firefox")]
    [InlineData("/usr/bin/firefox %U", "/usr/bin/firefox")]
    [InlineData("/usr/bin/gedit %f", "/usr/bin/gedit")]
    [InlineData("/usr/bin/gedit %F", "/usr/bin/gedit")]
    [InlineData("firefox", "firefox")]
    [InlineData("  /usr/bin/firefox   %u  ", "/usr/bin/firefox")]
    [InlineData("\"/opt/My Browser/bin/browser\" %U", "/opt/My Browser/bin/browser")]
    [InlineData("\"/opt/Quote\\\"d/browser\" %u", "/opt/Quote\"d/browser")]
    [InlineData("\"/opt/back\\\\slash/browser\"", "/opt/back\\slash/browser")]
    [InlineData("env GDK_BACKEND=x11 /usr/bin/firefox %u", "/usr/bin/firefox")]
    [InlineData("flatpak run --branch=stable org.mozilla.firefox %U", "flatpak")]
    [InlineData("/usr/bin/foo %i %c %k %d", "/usr/bin/foo")]
    [InlineData("/usr/bin/100%%sure %u", "/usr/bin/100%sure")]
    [InlineData("%u", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void The_binary_is_parsed_out_of_Exec_with_quotes_and_field_codes_removed(string exec, string? expected)
        => Assert.Equal(expected, DesktopEntry.ParseExecutable(exec));

    [Fact]
    public void Exec_is_tokenised_the_way_the_spec_quotes_arguments()
    {
        Assert.Equal(
            new[] { "/usr/bin/browser", "--profile=My Profile", "%u" },
            DesktopEntry.TokenizeExec("/usr/bin/browser \"--profile=My Profile\" %u").ToArray());
    }

    [Theory]
    [InlineData("Name", "Name", null)]
    [InlineData("Name[ja]", "Name", "ja")]
    [InlineData("Name[pt_BR]", "Name", "pt_BR")]
    [InlineData("Name[sr@latin]", "Name", "sr@latin")]
    [InlineData("Name[]", "Name[]", null)]
    [InlineData("[weird", "[weird", null)]
    public void A_key_is_split_into_its_name_and_its_locale(string key, string expectedKey, string? expectedLocale)
    {
        var (actualKey, actualLocale) = DesktopEntry.SplitKey(key);
        Assert.Equal(expectedKey, actualKey);
        Assert.Equal(expectedLocale, actualLocale);
    }

    [Fact]
    public void A_desktop_file_id_folds_sub_directories_into_the_name()
    {
        // The spec's rule: applications/kde4/konqbrowser.desktop is "kde4-konqbrowser.desktop",
        // and that is the token xdg-settings takes.
        Assert.Equal(
            "kde4-konqbrowser.desktop",
            DesktopEntry.DesktopFileId("/usr/share/applications", "/usr/share/applications/kde4/konqbrowser.desktop"));
        Assert.Equal(
            "firefox.desktop",
            DesktopEntry.DesktopFileId("/usr/share/applications", "/usr/share/applications/firefox.desktop"));
        Assert.Equal(
            "firefox.desktop",
            DesktopEntry.DesktopFileId("/usr/share/applications/", "/usr/share/applications/firefox.desktop"));
    }

    [Fact]
    public void A_crlf_desktop_file_parses_the_same_as_an_lf_one()
    {
        var entry = DesktopEntry.Parse("[Desktop Entry]\r\nName=Windows Written\r\nExec=/usr/bin/x %u\r\n");

        Assert.NotNull(entry);
        Assert.Equal("Windows Written", entry.Name);
        Assert.Equal("/usr/bin/x", entry.ExecutableName);
    }

    // ---------------------------------------------------------------- catalogue

    [Fact]
    public void The_xdg_search_path_defaults_to_the_spec_when_the_environment_is_empty()
    {
        var directories = LinuxBrowserCatalog.ApplicationDirectories(_ => null, Home);

        Assert.Equal(
            new[]
            {
                "/home/someone/.local/share/applications",
                "/usr/local/share/applications",
                "/usr/share/applications",
            },
            directories.ToArray());
    }

    [Fact]
    public void The_xdg_search_path_honours_the_environment_most_specific_first()
    {
        var env = Environment(("XDG_DATA_HOME", "/home/someone/data"), ("XDG_DATA_DIRS", "/opt/share:/usr/share:/opt/share"));

        // The repeated entry is dropped: a distribution cannot shadow itself.
        Assert.Equal(
            new[] { "/home/someone/data/applications", "/opt/share/applications", "/usr/share/applications" },
            LinuxBrowserCatalog.ApplicationDirectories(env, Home).ToArray());
    }

    [Theory]
    [InlineData("x-scheme-handler/https;", true)]
    [InlineData("x-scheme-handler/http;", true)]
    [InlineData("text/html;x-scheme-handler/HTTPS;", true)]
    [InlineData("text/html;", false)]
    [InlineData("x-scheme-handler/mailto;", false)]
    [InlineData("", false)]
    public void A_browser_is_an_entry_that_handles_a_web_scheme(string mimeType, bool expected)
    {
        var entry = DesktopEntry.Parse($"[Desktop Entry]\nName=X\nMimeType={mimeType}\n");

        Assert.NotNull(entry);
        Assert.Equal(expected, LinuxBrowserCatalog.HandlesWebUrls(entry));
    }

    [Fact]
    public void The_desktop_file_id_becomes_the_id_and_both_protocol_handlers()
    {
        var info = Map("firefox.desktop", FirefoxDesktop, "/usr/share/applications");

        Assert.Equal("firefox.desktop", info.Id);
        Assert.Equal("firefox.desktop", info.HttpProgId);
        Assert.Equal("firefox.desktop", info.HttpsProgId);
        Assert.Equal("firefox.desktop", info.RegisteredApplicationName);
        Assert.True(info.OwnsProgId("firefox.desktop"));
        Assert.Equal("Firefox Web Browser", info.DisplayName);
        Assert.Equal("/usr/lib/firefox/firefox", info.ExecutablePath);
        Assert.Equal("firefox", info.IconPath);
        Assert.Equal(BrowserHealth.Ok, info.Health);
        Assert.Equal(RegistrationScope.Machine, info.Scope);
    }

    [Fact]
    public void The_display_name_is_localised_and_falls_back_to_the_id()
    {
        var japanese = Map("firefox.desktop", FirefoxDesktop, "/usr/share/applications", culture: CultureInfo.GetCultureInfo("ja"));
        Assert.Equal("Firefox ウェブ・ブラウザー", japanese.DisplayName);

        // An entry with no Name key must still appear in the list rather than vanish.
        var nameless = Map("odd.desktop", "[Desktop Entry]\nExec=/usr/bin/odd %u\nMimeType=x-scheme-handler/https;\n", "/usr/share/applications");
        Assert.Equal("odd", nameless.DisplayName);
    }

    [Fact]
    public void A_browser_whose_binary_cannot_be_resolved_is_reported_as_missing()
    {
        var info = Map("gone.desktop", "[Desktop Entry]\nName=Gone\nExec=gone-browser %u\n", "/usr/share/applications", resolve: _ => null);

        Assert.Equal(BrowserHealth.ExecutableMissing, info.Health);
        // The raw binary is still reported so the UI can say what the entry asked for.
        Assert.Equal("gone-browser", info.ExecutablePath);
    }

    [Fact]
    public void TryExec_decides_health_when_it_is_present()
    {
        // TryExec exists exactly to answer "is this entry usable?": a wrapper script in Exec may
        // be present while the browser it launches is not.
        var text = "[Desktop Entry]\nName=Wrapped\nExec=/usr/bin/wrapper %u\nTryExec=/opt/real/browser\n";
        var info = Map("wrapped.desktop", text, "/usr/share/applications", resolve: binary => binary == "/usr/bin/wrapper" ? binary : null);

        Assert.Equal(BrowserHealth.ExecutableMissing, info.Health);
        Assert.Equal("/usr/bin/wrapper", info.ExecutablePath);
    }

    [Theory]
    [InlineData("/home/someone/.local/share/applications", RegistrationScope.User)]
    [InlineData("/usr/share/applications", RegistrationScope.Machine)]
    [InlineData("/home/someone-else/.local/share/applications", RegistrationScope.Machine)]
    public void An_entry_in_the_home_directory_is_a_per_user_registration(string directory, RegistrationScope expected)
    {
        // Regression guard, as on macOS: a prefix test without a trailing separator would
        // classify /home/someone-else/... as living inside /home/someone.
        var info = Map("firefox.desktop", FirefoxDesktop, directory);

        Assert.Equal(expected, info.Scope);
        Assert.Equal(expected, info.RegisteredApplicationScope);
    }

    [Fact]
    public void A_user_entry_shadows_the_system_entry_with_the_same_id_and_the_list_is_sorted()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var home = Path.Combine(root, "home");
            var userApplications = Path.Combine(home, ".local", "share", "applications");
            var systemApplications = Path.Combine(root, "usr", "share", "applications");
            Directory.CreateDirectory(userApplications);
            Directory.CreateDirectory(systemApplications);

            File.WriteAllText(Path.Combine(userApplications, "firefox.desktop"), Entry("Firefox Nightly", "/opt/nightly/firefox"));
            File.WriteAllText(Path.Combine(systemApplications, "firefox.desktop"), Entry("Firefox", "/usr/bin/firefox"));
            File.WriteAllText(Path.Combine(systemApplications, "chromium.desktop"), Entry("Chromium", "/usr/bin/chromium"));
            File.WriteAllText(Path.Combine(systemApplications, "gedit.desktop"), Entry("Text Editor", "/usr/bin/gedit", mimeType: "text/plain;"));

            var browsers = new LinuxBrowserCatalog([userApplications, systemApplications], home, binary => binary).GetBrowsers();

            Assert.Equal(new[] { "Chromium", "Firefox Nightly" }, browsers.Select(b => b.DisplayName).ToArray());
            var firefox = browsers.Single(b => b.Id == "firefox.desktop");
            Assert.Equal("/opt/nightly/firefox", firefox.ExecutablePath);
            Assert.Equal(RegistrationScope.User, firefox.Scope);
            Assert.Equal(RegistrationScope.Machine, browsers.Single(b => b.Id == "chromium.desktop").Scope);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_suppressed_entry_is_skipped_and_still_shadows_the_system_one()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var home = Path.Combine(root, "home");
            var userApplications = Path.Combine(home, ".local", "share", "applications");
            var systemApplications = Path.Combine(root, "usr", "share", "applications");
            Directory.CreateDirectory(userApplications);
            Directory.CreateDirectory(systemApplications);

            // The user has masked the system entry, which is how NoDisplay/Hidden are used.
            File.WriteAllText(
                Path.Combine(userApplications, "firefox.desktop"),
                Entry("Firefox", "/usr/bin/firefox", extra: "NoDisplay=true\n"));
            File.WriteAllText(Path.Combine(systemApplications, "firefox.desktop"), Entry("Firefox", "/usr/bin/firefox"));
            File.WriteAllText(Path.Combine(systemApplications, "hidden.desktop"), Entry("Hidden", "/usr/bin/hidden", extra: "Hidden=true\n"));
            File.WriteAllText(Path.Combine(systemApplications, "epiphany.desktop"), Entry("Web", "/usr/bin/epiphany"));

            var browsers = new LinuxBrowserCatalog([userApplications, systemApplications], home, binary => binary).GetBrowsers();

            Assert.Equal(new[] { "Web" }, browsers.Select(b => b.DisplayName).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_nested_entry_keeps_its_folded_id_and_an_unparsable_file_is_skipped()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var applications = Path.Combine(root, "applications");
            Directory.CreateDirectory(Path.Combine(applications, "kde4"));

            File.WriteAllText(Path.Combine(applications, "kde4", "konqbrowser.desktop"), Entry("Konqueror", "/usr/bin/konqueror"));
            File.WriteAllText(Path.Combine(applications, "broken.desktop"), "not a desktop file at all");

            var browsers = new LinuxBrowserCatalog([applications], "/home/nobody", binary => binary).GetBrowsers();

            Assert.Equal(new[] { "kde4-konqbrowser.desktop" }, browsers.Select(b => b.Id).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_applications_directory_is_not_an_error()
        => Assert.Empty(new LinuxBrowserCatalog(
            [Path.Combine(Path.GetTempPath(), "browserflip-does-not-exist-" + Guid.NewGuid().ToString("N"))],
            Home,
            binary => binary).GetBrowsers());

    // ---------------------------------------------------------------- xdg-settings wrapper

    [Fact]
    public void The_default_browser_is_read_from_xdg_settings()
    {
        var tool = new FakeXdgTool { DefaultWebBrowser = "firefox.desktop" };

        Assert.Equal("firefox.desktop", new XdgSettings(tool).GetDefaultWebBrowser());
        Assert.Equal<string[]>(
            ["xdg-settings get default-web-browser", "xdg-settings check default-web-browser firefox.desktop"],
            [.. tool.Invocations]);
    }

    [Fact]
    public void A_handler_that_is_merely_registered_is_not_reported_as_the_default()
    {
        // Measured on a fresh Ubuntu arm64 install with no mimeapps.list: `get` walks
        // mimeinfo.cache and returns the first REGISTERED handler, while `check` answers "no".
        // Reporting that as the default asserts a choice the user never made.
        var tool = new FakeXdgTool { DefaultWebBrowser = "org.gnome.Epiphany.desktop", CheckAnswer = "no" };

        Assert.Null(new XdgSettings(tool).GetDefaultWebBrowser());
    }

    [Fact]
    public void A_check_that_cannot_run_does_not_hide_a_real_default()
    {
        // Fail open on the confirmation only: an unusual or missing `check` must not make a
        // genuinely configured default vanish.
        var tool = new FakeXdgTool { DefaultWebBrowser = "firefox.desktop", CheckFails = true };

        Assert.Equal("firefox.desktop", new XdgSettings(tool).GetDefaultWebBrowser());
    }

    [Theory]
    [InlineData(0, "firefox.desktop\n", "firefox.desktop")]
    [InlineData(0, "  firefox.desktop  \n\n", "firefox.desktop")]
    [InlineData(0, "\nchromium.desktop\n", "chromium.desktop")]
    [InlineData(0, "", null)]
    [InlineData(0, "   \n", null)]
    [InlineData(1, "firefox.desktop\n", null)]
    public void A_single_value_is_parsed_only_from_a_successful_run(int exitCode, string stdout, string? expected)
        => Assert.Equal(expected, XdgSettings.ParseSingleValue(new CommandResult(exitCode, stdout, string.Empty)));

    [Fact]
    public void A_timed_out_or_unlaunchable_tool_yields_no_value()
    {
        Assert.Null(XdgSettings.ParseSingleValue(new CommandResult(-1, "firefox.desktop", string.Empty, TimedOut: true)));
        Assert.Null(XdgSettings.ParseSingleValue(new CommandResult(-1, string.Empty, string.Empty, Error: "No such file")));
    }

    [Fact]
    public void Setting_the_default_browser_passes_the_desktop_file_id_as_its_own_argument()
    {
        var tool = new FakeXdgTool();

        var result = new XdgSettings(tool).SetDefaultWebBrowser("firefox.desktop");

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "xdg-settings set default-web-browser firefox.desktop" }, tool.Invocations.ToArray());
    }

    [Fact]
    public void A_scheme_handler_is_queried_through_xdg_mime()
    {
        var tool = new FakeXdgTool { MimeHandler = "thunderbird.desktop" };

        Assert.Equal("thunderbird.desktop", new XdgSettings(tool).QueryMimeHandler("x-scheme-handler/mailto"));
        Assert.Equal(new[] { "xdg-mime query default x-scheme-handler/mailto" }, tool.Invocations.ToArray());
    }

    // ---------------------------------------------------------------- association query

    [Fact]
    public void The_current_default_is_reported_with_the_name_and_binary_from_its_desktop_entry()
    {
        var tool = new FakeXdgTool { DefaultWebBrowser = "firefox.desktop" };
        var query = new LinuxAssociationQuery(new XdgSettings(tool), Catalogue(LinuxBrowser("firefox.desktop", "Firefox", "/usr/bin/firefox")));

        var association = query.QueryProtocol("https");

        Assert.NotNull(association);
        Assert.Equal("firefox.desktop", association.ProgId);
        Assert.Equal("Firefox", association.FriendlyAppName);
        Assert.Equal("/usr/bin/firefox", association.ExecutablePath);
    }

    [Fact]
    public void A_bare_id_is_normalised_so_it_compares_with_a_browsers_prog_id()
    {
        var tool = new FakeXdgTool { DefaultWebBrowser = "firefox" };
        var query = new LinuxAssociationQuery(new XdgSettings(tool), Catalogue(LinuxBrowser("firefox.desktop", "Firefox", "/usr/bin/firefox")));

        Assert.Equal("firefox.desktop", query.QueryProtocol("http")?.ProgId);
    }

    [Fact]
    public void A_default_that_is_not_in_the_catalogue_is_still_reported_by_id()
    {
        // The user's default may be a browser that has since been uninstalled. Reporting the id
        // with no name is honest; dropping the association would make BrowserFlip say "none".
        var tool = new FakeXdgTool { DefaultWebBrowser = "ghost.desktop" };
        var query = new LinuxAssociationQuery(new XdgSettings(tool), Catalogue());

        var association = query.QueryProtocol("https");

        Assert.NotNull(association);
        Assert.Equal("ghost.desktop", association.ProgId);
        Assert.Null(association.FriendlyAppName);
        Assert.Null(association.ExecutablePath);
    }

    [Fact]
    public void No_default_at_all_is_reported_as_no_association()
    {
        var tool = new FakeXdgTool { DefaultWebBrowser = null, MimeHandler = null };

        Assert.Null(new LinuxAssociationQuery(new XdgSettings(tool), Catalogue()).QueryProtocol("https"));
    }

    [Fact]
    public void A_non_web_scheme_falls_back_to_xdg_mime()
    {
        var tool = new FakeXdgTool { DefaultWebBrowser = "firefox.desktop", MimeHandler = "thunderbird.desktop" };

        var association = new LinuxAssociationQuery(new XdgSettings(tool), Catalogue()).QueryProtocol("mailto");

        Assert.Equal("thunderbird.desktop", association?.ProgId);
        // xdg-settings has no property for mailto, so it must not be consulted for it.
        Assert.Equal(new[] { "xdg-mime query default x-scheme-handler/mailto" }, tool.Invocations.ToArray());
    }

    [Fact]
    public void A_web_scheme_falls_back_to_xdg_mime_when_xdg_settings_has_no_answer()
    {
        var tool = new FakeXdgTool { DefaultWebBrowser = null, MimeHandler = "chromium.desktop" };

        Assert.Equal("chromium.desktop", new LinuxAssociationQuery(new XdgSettings(tool), Catalogue()).QueryProtocol("https")?.ProgId);
        Assert.Equal(
            new[] { "xdg-settings get default-web-browser", "xdg-mime query default x-scheme-handler/https" },
            tool.Invocations.ToArray());
    }

    // ---------------------------------------------------------------- switching (consent)

    [Fact]
    public async Task Without_the_users_confirmation_nothing_is_run_at_all()
    {
        // This is the whole point of the Linux switcher: xdg-settings applies the change
        // immediately, with no OS prompt and no undo, so an unconfirmed request must be a no-op.
        var tool = new FakeXdgTool { DefaultWebBrowser = "chromium.desktop" };
        var switcher = new LinuxDefaultBrowserSwitcher(new XdgSettings(tool)) { UserHasConfirmed = false };

        var result = await switcher.RequestSwitchAsync(LinuxBrowser("firefox.desktop", "Firefox", "/usr/bin/firefox"), TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Declined, result.Outcome);
        Assert.False(result.Accepted);
        Assert.NotNull(result.Error);
        Assert.Empty(tool.Invocations);
        Assert.Equal("chromium.desktop", tool.DefaultWebBrowser);
    }

    [Fact]
    public void The_linux_mechanism_is_a_command_line_tool()
        => Assert.Equal(
            SwitchMechanism.CommandLineTool,
            new LinuxDefaultBrowserSwitcher(new XdgSettings(new FakeXdgTool())) { UserHasConfirmed = true }.Mechanism);

    [Fact]
    public async Task A_confirmed_change_that_the_system_confirms_is_reported_as_applied()
    {
        var tool = new FakeXdgTool { DefaultWebBrowser = "chromium.desktop" };
        var switcher = new LinuxDefaultBrowserSwitcher(new XdgSettings(tool)) { UserHasConfirmed = true };

        var result = await switcher.RequestSwitchAsync(LinuxBrowser("firefox.desktop", "Firefox", "/usr/bin/firefox"), TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Applied, result.Outcome);
        Assert.True(result.Accepted);
        Assert.Null(result.Error);
        Assert.Equal("firefox.desktop", tool.DefaultWebBrowser);
        // The change is read back rather than assumed.
        Assert.Equal(
            new[]
            {
                "xdg-settings set default-web-browser firefox.desktop",
                "xdg-settings get default-web-browser",
                // The read-back now confirms with `check`, so a merely-registered handler is
                // not mistaken for a configured default.
                "xdg-settings check default-web-browser firefox.desktop",
            },
            tool.Invocations.ToArray());
    }

    [Fact]
    public async Task A_change_the_system_does_not_confirm_is_only_reported_as_requested()
    {
        // Happens inside a Flatpak sandbox and on desktops with a second source of truth.
        var tool = new FakeXdgTool { DefaultWebBrowser = "chromium.desktop", SetTakesEffect = false };
        var switcher = new LinuxDefaultBrowserSwitcher(new XdgSettings(tool)) { UserHasConfirmed = true };

        var result = await switcher.RequestSwitchAsync(LinuxBrowser("firefox.desktop", "Firefox", "/usr/bin/firefox"), TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Requested, result.Outcome);
        Assert.Contains("chromium.desktop", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failing_tool_is_reported_as_a_failure_with_what_it_printed()
    {
        var tool = new FakeXdgTool { SetSucceeds = false, StandardErrorText = "xdg-settings: unknown desktop environment" };
        var switcher = new LinuxDefaultBrowserSwitcher(new XdgSettings(tool)) { UserHasConfirmed = true };

        var result = await switcher.RequestSwitchAsync(LinuxBrowser("firefox.desktop", "Firefox", "/usr/bin/firefox"), TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Failed, result.Outcome);
        Assert.Equal("xdg-settings: unknown desktop environment", result.Error);
    }

    [Fact]
    public async Task A_missing_xdg_settings_is_reported_as_unsupported()
    {
        var tool = new FakeXdgTool { ToolInstalled = false };
        var switcher = new LinuxDefaultBrowserSwitcher(new XdgSettings(tool)) { UserHasConfirmed = true };

        var result = await switcher.RequestSwitchAsync(LinuxBrowser("firefox.desktop", "Firefox", "/usr/bin/firefox"), TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.Unsupported, result.Outcome);
        Assert.Contains("xdg-utils", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_uninstalled_browser_is_never_set_as_the_default()
    {
        var tool = new FakeXdgTool { DefaultWebBrowser = "chromium.desktop" };
        var switcher = new LinuxDefaultBrowserSwitcher(new XdgSettings(tool)) { UserHasConfirmed = true };
        var gone = LinuxBrowser("gone.desktop", "Gone", "/usr/bin/gone") with { Health = BrowserHealth.ExecutableMissing };

        var result = await switcher.RequestSwitchAsync(gone, TestContext.Current.CancellationToken);

        Assert.Equal(SwitchOutcome.BrowserUnavailable, result.Outcome);
        Assert.Empty(tool.Invocations);
    }

    [Theory]
    [InlineData("firefox.desktop", true)]
    [InlineData("firefox", true)]
    [InlineData("chromium.desktop", false)]
    [InlineData("", false)]
    public void A_switch_target_matches_by_desktop_file_id(string current, bool expected)
        => Assert.Equal(expected, LinuxDefaultBrowserSwitcher.Matches(current, LinuxBrowser("firefox.desktop", "Firefox", "/usr/bin/firefox")));

    // ---------------------------------------------------------------- capabilities

    [Theory]
    [InlineData("wayland", "GNOME", CapabilityState.Unsupported, CapabilityState.Unsupported, CapabilityState.Unavailable)]
    [InlineData("wayland", "KDE", CapabilityState.Unsupported, CapabilityState.Unsupported, CapabilityState.Available)]
    [InlineData("x11", "GNOME", CapabilityState.Available, CapabilityState.Available, CapabilityState.Unavailable)]
    [InlineData("x11", "KDE", CapabilityState.Available, CapabilityState.Available, CapabilityState.Available)]
    [InlineData("wayland", "ubuntu:GNOME", CapabilityState.Unsupported, CapabilityState.Unsupported, CapabilityState.Unavailable)]
    [InlineData("x11", "XFCE", CapabilityState.Available, CapabilityState.Available, CapabilityState.Available)]
    public void The_capability_matrix_follows_the_session_type_and_the_desktop(
        string sessionType,
        string desktop,
        CapabilityState alwaysOnTop,
        CapabilityState windowPositioning,
        CapabilityState trayIcon)
    {
        var capabilities = Capabilities(sessionType, desktop);

        Assert.Equal(alwaysOnTop, capabilities.AlwaysOnTop.State);
        Assert.Equal(windowPositioning, capabilities.WindowPositioning.State);
        Assert.Equal(trayIcon, capabilities.TrayIcon.State);
        Assert.Equal(CapabilityState.Available, capabilities.DefaultBrowserSwitch.State);

        // Every degraded capability must carry a reason: the UI shows it instead of just
        // disabling a control silently.
        Assert.All(
            capabilities.All(),
            entry => Assert.True(entry.Capability.IsAvailable || !string.IsNullOrWhiteSpace(entry.Capability.Reason), entry.Name));
    }

    [Theory]
    [InlineData("KDE")]
    [InlineData("XFCE")]
    [InlineData(null)]
    public void Without_a_session_bus_there_can_be_no_tray_icon(string? desktop)
    {
        // Measured on linux/arm64: with no DBUS_SESSION_BUS_ADDRESS the app exports no
        // StatusNotifierItem at all, yet Avalonia's SetIcons neither throws nor logs. Claiming
        // the capability made the self-test report a tray icon that provably did not exist.
        var capabilities = Capabilities("x11", desktop, sessionBus: false);

        Assert.Equal(CapabilityState.Unavailable, capabilities.TrayIcon.State);
        Assert.Contains("D-Bus", capabilities.TrayIcon.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_running_watcher_beats_the_gnome_heuristic()
    {
        // Measured on a real Wayland session: with XDG_CURRENT_DESKTOP=ubuntu:GNOME, a session
        // bus and a live StatusNotifierWatcher, the app refused to create a tray item - yet the
        // tray would have worked. gnome-shell-extension-appindicator is a dependency of
        // ubuntu-desktop, so the desktop-name heuristic misfires for most Ubuntu GNOME users.
        var env = Environment(
            ("XDG_SESSION_TYPE", "wayland"),
            ("XDG_CURRENT_DESKTOP", "ubuntu:GNOME"),
            ("DBUS_SESSION_BUS_ADDRESS", "unix:path=/run/user/1000/bus"));

        Assert.Equal(CapabilityState.Available, LinuxCapabilities.DetectTray(env, isGnome: true, () => true).State);

        var refused = LinuxCapabilities.DetectTray(env, isGnome: true, () => false);
        Assert.Equal(CapabilityState.Unavailable, refused.State);
        Assert.Contains("AppIndicator", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void No_session_bus_beats_everything_including_a_watcher()
    {
        // Without a bus there is nowhere to export the item, whatever else is true.
        var env = Environment(("XDG_SESSION_TYPE", "x11"), ("XDG_CURRENT_DESKTOP", "KDE"));
        var result = LinuxCapabilities.DetectTray(env, isGnome: false, () => true);

        Assert.Equal(CapabilityState.Unavailable, result.State);
        Assert.Contains("D-Bus", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_bus_socket_counts_even_without_the_environment_variable()
    {
        // A login session can expose the socket without advertising the address.
        var withRuntimeDir = new LinuxCapabilities(
            Environment(("XDG_SESSION_TYPE", "x11"), ("XDG_CURRENT_DESKTOP", "KDE"), ("XDG_RUNTIME_DIR", "/definitely/not/here")),
            () => true);
        Assert.Equal(CapabilityState.Unavailable, withRuntimeDir.TrayIcon.State);

        Assert.False(LinuxCapabilities.HasSessionBus(_ => null));
        Assert.True(LinuxCapabilities.HasSessionBus(n => n == "DBUS_SESSION_BUS_ADDRESS" ? "unix:path=/x" : null));
    }

    [Fact]
    public void The_wayland_reasons_name_the_actual_protocol_limitation()
    {
        var wayland = Capabilities("wayland", "GNOME");

        Assert.Contains("xdg-shell", wayland.AlwaysOnTop.Reason, StringComparison.Ordinal);
        Assert.Contains("coordinates", wayland.WindowPositioning.Reason, StringComparison.Ordinal);
        Assert.Contains("AppIndicator", wayland.TrayIcon.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Linux_never_lets_the_os_confirm_the_change()
    {
        // The flag the UI uses to decide that it must show its own confirmation dialogue.
        Assert.False(Capabilities("x11", "KDE").OsConfirmsDefaultBrowserChange);
        Assert.False(Capabilities("wayland", "GNOME").OsConfirmsDefaultBrowserChange);
    }

    [Fact]
    public void Switching_is_unavailable_when_xdg_settings_is_not_installed()
    {
        var capabilities = Capabilities("x11", "KDE", switchToolAvailable: false);

        Assert.Equal(CapabilityState.Unavailable, capabilities.DefaultBrowserSwitch.State);
        Assert.Contains("xdg-utils", capabilities.DefaultBrowserSwitch.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wayland", "GNOME", "Linux (GNOME, Wayland)")]
    [InlineData("x11", "KDE", "Linux (KDE, X11)")]
    [InlineData("wayland", "ubuntu:GNOME", "Linux (ubuntu/GNOME, Wayland)")]
    [InlineData(null, null, "Linux (unknown desktop, unknown session)")]
    public void The_description_names_the_desktop_and_the_session(string? sessionType, string? desktop, string expected)
        => Assert.Equal(expected, Capabilities(sessionType, desktop).Description);

    [Fact]
    public void An_unidentifiable_session_is_reported_as_unavailable_not_as_working()
    {
        var capabilities = Capabilities(sessionType: null, desktop: "KDE");

        Assert.Equal(LinuxSessionType.Unknown, capabilities.Session);
        Assert.Equal(CapabilityState.Unavailable, capabilities.AlwaysOnTop.State);
        Assert.Equal(CapabilityState.Unavailable, capabilities.WindowPositioning.State);
    }

    [Theory]
    [InlineData("wayland", null, null, "Wayland")]
    [InlineData("x11", null, null, "X11")]
    [InlineData("X11", null, null, "X11")]
    [InlineData("tty", null, null, "Unknown")]
    [InlineData(null, "wayland-0", null, "Wayland")]
    [InlineData(null, null, ":0", "X11")]
    [InlineData(null, "wayland-0", ":0", "Wayland")]
    [InlineData(null, null, null, "Unknown")]
    public void The_session_type_falls_back_to_the_display_variables(string? sessionType, string? waylandDisplay, string? display, string expected)
    {
        // Some login managers and most containers leave XDG_SESSION_TYPE unset. The expectation
        // is a string because LinuxSessionType is internal and a public test signature cannot
        // name it.
        var env = Environment(("XDG_SESSION_TYPE", sessionType), ("WAYLAND_DISPLAY", waylandDisplay), ("DISPLAY", display));

        Assert.Equal(expected, LinuxCapabilities.DetectSession(env).ToString());
    }

    [Theory]
    [InlineData("GNOME", true)]
    [InlineData("ubuntu:GNOME", true)]
    [InlineData("GNOME-Classic:GNOME", true)]
    [InlineData("gnome", true)]
    [InlineData("KDE", false)]
    [InlineData("XFCE", false)]
    [InlineData(null, false)]
    public void GNOME_is_detected_anywhere_in_the_desktop_list(string? desktop, bool expected)
        => Assert.Equal(expected, LinuxCapabilities.IsGnomeDesktop(desktop));

    [Fact]
    public void Every_capability_is_enumerated_for_the_diagnostics_screen()
        => Assert.Equal(4, Capabilities("wayland", "GNOME").All().Count);

    // ---------------------------------------------------------------- icons

    [Theory]
    [InlineData("/usr/share/icons/hicolor/48x48/apps/firefox.png", 48)]
    [InlineData("/usr/share/icons/hicolor/256x256/apps/firefox.png", 256)]
    [InlineData("/usr/share/icons/hicolor/48x48@2x/apps/firefox.png", 48)]
    [InlineData("/usr/share/icons/hicolor/scalable/apps/firefox.svg", 0)]
    [InlineData("/usr/share/pixmaps/firefox.png", 0)]
    [InlineData("/usr/share/icons/hicolor/16x32/apps/firefox.png", 0)]
    public void The_icon_size_is_read_from_the_theme_directory(string path, int expected)
        => Assert.Equal(expected, LinuxAppIconProvider.SizeFromPath(path));

    [Fact]
    public void The_smallest_icon_at_least_as_big_as_requested_wins()
    {
        string[] candidates =
        [
            "/icons/hicolor/16x16/apps/x.png",
            "/icons/hicolor/48x48/apps/x.png",
            "/icons/hicolor/128x128/apps/x.png",
            "/icons/hicolor/256x256/apps/x.png",
        ];

        Assert.Equal("/icons/hicolor/48x48/apps/x.png", LinuxAppIconProvider.ChooseIcon(candidates, 32));
        Assert.Equal("/icons/hicolor/48x48/apps/x.png", LinuxAppIconProvider.ChooseIcon(candidates, 48));
        Assert.Equal("/icons/hicolor/128x128/apps/x.png", LinuxAppIconProvider.ChooseIcon(candidates, 64));
    }

    [Fact]
    public void When_nothing_is_big_enough_the_largest_icon_wins()
    {
        string[] candidates = ["/icons/hicolor/16x16/apps/x.png", "/icons/hicolor/32x32/apps/x.png"];

        Assert.Equal("/icons/hicolor/32x32/apps/x.png", LinuxAppIconProvider.ChooseIcon(candidates, 256));
    }

    [Fact]
    public void An_svg_is_only_chosen_when_there_is_no_png_at_all()
    {
        Assert.Equal(
            "/icons/hicolor/48x48/apps/x.png",
            LinuxAppIconProvider.ChooseIcon(["/icons/hicolor/scalable/apps/x.svg", "/icons/hicolor/48x48/apps/x.png"], 64));
        Assert.Equal(
            "/icons/hicolor/scalable/apps/x.svg",
            LinuxAppIconProvider.ChooseIcon(["/icons/hicolor/scalable/apps/x.svg"], 64));
        Assert.Null(LinuxAppIconProvider.ChooseIcon([], 64));
    }

    [Fact]
    public void The_icon_search_path_follows_the_icon_theme_specification()
    {
        var env = Environment(("XDG_DATA_HOME", "/home/someone/data"), ("XDG_DATA_DIRS", "/usr/share"));

        Assert.Equal(
            new[] { "/home/someone/data/icons", "/home/someone/.icons", "/usr/share/icons", "/usr/share/pixmaps" },
            LinuxAppIconProvider.IconSearchDirectories(env, Home).ToArray());
    }

    [Fact]
    public void An_absolute_icon_path_is_used_directly_and_only_png_bytes_are_returned()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var png = Path.Combine(root, "icon.png");
            var lying = Path.Combine(root, "lying.png");
            var svg = Path.Combine(root, "icon.svg");
            File.WriteAllBytes(png, PngBytes(0x01));
            File.WriteAllText(lying, "<svg>not a png at all</svg>");
            File.WriteAllText(svg, "<svg/>");

            var provider = new LinuxAppIconProvider([root]);

            Assert.Equal(PngBytes(0x01), provider.GetIconPng(png));
            // The contract is "PNG bytes or null": mislabelled content must not reach a decoder.
            Assert.Null(provider.GetIconPng(lying));
            Assert.Null(provider.GetIconPng(svg));
            Assert.Null(provider.GetIconPng(Path.Combine(root, "absent.png")));
            Assert.Null(provider.GetIconPng(null));
            Assert.Null(provider.GetIconPng("   "));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_themed_icon_name_is_resolved_to_the_best_sized_png()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var small = Path.Combine(root, "icons", "hicolor", "22x22", "apps");
            var large = Path.Combine(root, "icons", "hicolor", "128x128", "apps");
            Directory.CreateDirectory(small);
            Directory.CreateDirectory(large);
            File.WriteAllBytes(Path.Combine(small, "firefox.png"), PngBytes(0x22));
            File.WriteAllBytes(Path.Combine(large, "firefox.png"), PngBytes(0x80));

            var provider = new LinuxAppIconProvider([Path.Combine(root, "icons")]);

            Assert.Equal(PngBytes(0x80), provider.GetIconPng("firefox", 64));
            Assert.Equal(PngBytes(0x22), provider.GetIconPng("firefox", 16));
            Assert.Null(provider.GetIconPng("no-such-icon", 64));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_themed_icon_that_exists_only_as_svg_is_a_known_limitation()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var scalable = Path.Combine(root, "icons", "hicolor", "scalable", "apps");
            Directory.CreateDirectory(scalable);
            File.WriteAllText(Path.Combine(scalable, "epiphany.svg"), "<svg/>");

            // Documented limitation: rasterising SVG would mean taking an SVG renderer
            // dependency, so the UI keeps its placeholder instead of being handed junk bytes.
            Assert.Null(new LinuxAppIconProvider([Path.Combine(root, "icons")]).GetIconPng("epiphany", 64));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Stands in for <c>xdg-settings</c> and <c>xdg-mime</c>: it records every invocation and
    /// keeps the "current default" in memory, so the read-back the switcher performs is modelled
    /// without a single process being started.
    /// </summary>
    private sealed class FakeXdgTool : ICommandRunner
    {
        public string? DefaultWebBrowser { get; set; }

        public string? MimeHandler { get; set; }

        public bool SetSucceeds { get; set; } = true;

        /// <summary>False models a tool that reports success without the change taking effect.</summary>
        public bool SetTakesEffect { get; set; } = true;

        public bool ToolInstalled { get; set; } = true;

        /// <summary>What `xdg-settings check` prints: "yes" confirms, "no" denies.</summary>
        public string CheckAnswer { get; set; } = "yes";

        /// <summary>Models a `check` subcommand that errors out rather than answering.</summary>
        public bool CheckFails { get; set; }

        public string StandardErrorText { get; set; } = string.Empty;

        public List<string> Invocations { get; } = [];

        public CommandResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Invocations.Add(fileName + " " + string.Join(' ', arguments));

            if (!ToolInstalled)
            {
                return new CommandResult(-1, string.Empty, string.Empty, Error: "No such file or directory") { ToolMissing = true };
            }

            return arguments switch
            {
                ["check", "default-web-browser", _] => CheckFails
                    ? new CommandResult(2, string.Empty, "xdg-settings: unknown command")
                    : new CommandResult(0, CheckAnswer + "\n", string.Empty),
                ["get", "default-web-browser"] => DefaultWebBrowser is null
                    ? new CommandResult(1, string.Empty, "xdg-settings: no default browser")
                    : new CommandResult(0, DefaultWebBrowser + "\n", string.Empty),
                ["set", "default-web-browser", var id] => Set(id),
                ["query", "default", _] => MimeHandler is null
                    ? new CommandResult(1, string.Empty, "xdg-mime: no handler")
                    : new CommandResult(0, MimeHandler + "\n", string.Empty),
                _ => new CommandResult(127, string.Empty, "unexpected invocation"),
            };
        }

        private CommandResult Set(string desktopFileId)
        {
            if (!SetSucceeds)
            {
                return new CommandResult(1, string.Empty, StandardErrorText);
            }

            if (SetTakesEffect)
            {
                DefaultWebBrowser = desktopFileId;
            }

            return new CommandResult(0, string.Empty, string.Empty);
        }
    }

    private static Func<string, string?> Environment(params (string Key, string? Value)[] values)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            map[key] = value;
        }

        return key => map.TryGetValue(key, out var value) ? value : null;
    }

    private static LinuxCapabilities Capabilities(
        string? sessionType, string? desktop, bool switchToolAvailable = true, bool sessionBus = true)
        => new(
            Environment(
                ("XDG_SESSION_TYPE", sessionType),
                ("XDG_CURRENT_DESKTOP", desktop),
                // A tray icon is a D-Bus StatusNotifierItem, so the matrix assumes a normal
                // desktop session that has a bus. The no-bus case has its own tests.
                ("DBUS_SESSION_BUS_ADDRESS", sessionBus ? "unix:path=/run/user/1000/bus" : null)),
            () => switchToolAvailable);

    private static BrowserInfo Map(
        string desktopFileId,
        string content,
        string applicationsDirectory,
        Func<string, string?>? resolve = null,
        CultureInfo? culture = null)
    {
        var entry = DesktopEntry.Parse(content);
        Assert.NotNull(entry);
        return LinuxBrowserCatalog.ToBrowserInfo(
            desktopFileId,
            entry,
            applicationsDirectory,
            Home,
            resolve ?? (binary => binary),
            culture ?? CultureInfo.InvariantCulture);
    }

    /// <summary>A browser shaped the way the Linux catalogue produces them (desktop id as ProgID).</summary>
    private static BrowserInfo LinuxBrowser(string desktopFileId, string name, string executablePath) => new()
    {
        Id = desktopFileId,
        DisplayName = name,
        ExecutablePath = executablePath,
        IconPath = "web-browser",
        HttpProgId = desktopFileId,
        HttpsProgId = desktopFileId,
        RegisteredApplicationName = desktopFileId,
        RegisteredApplicationScope = RegistrationScope.Machine,
        Scope = RegistrationScope.Machine,
        Health = BrowserHealth.Ok,
    };

    private static FakeCatalog Catalogue(params BrowserInfo[] browsers)
    {
        var catalogue = new FakeCatalog();
        foreach (var browser in browsers)
        {
            catalogue.Add(browser);
        }

        return catalogue;
    }

    private static string Entry(string name, string exec, string mimeType = "x-scheme-handler/https;x-scheme-handler/http;", string extra = "")
        => $"[Desktop Entry]\nType=Application\nName={name}\nExec={exec} %u\nIcon=web-browser\nMimeType={mimeType}\n{extra}";

    private static byte[] PngBytes(byte marker) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker];

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "browserflip-linux-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
