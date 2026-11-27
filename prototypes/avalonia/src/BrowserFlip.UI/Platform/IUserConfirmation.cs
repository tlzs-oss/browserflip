namespace BrowserFlip.UI.Platform;

/// <summary>
/// Asks the user to confirm an action that cannot be undone.
/// </summary>
/// <remarks>
/// This exists for one reason. On Windows and macOS the operating system itself asks before the
/// default browser changes, so BrowserFlip must not ask twice. On Linux <c>xdg-settings</c>
/// applies the change immediately, with no prompt and no undo. The product rule - BrowserFlip
/// never changes the default browser without the user agreeing - therefore has to be honoured by
/// BrowserFlip itself on exactly the platforms where the OS does not honour it.
/// <para>
/// <see cref="IPlatformCapabilities.OsConfirmsDefaultBrowserChange"/> decides which case applies,
/// so the rule is driven by the platform's own declaration rather than by an OS check in the UI.
/// </para>
/// </remarks>
public interface IUserConfirmation
{
    /// <summary>
    /// Where an affirmative answer comes from, in words, for the diagnostics report. It must
    /// never read as though a person answered a dialog when no dialog was shown.
    /// </summary>
    string ConsentSource { get; }

    /// <summary>
    /// Returns true only when the user explicitly agreed. Anything else - dismissing the dialog,
    /// closing the window, no implementation being available - must return false.
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The fail-closed default: never confirms. Used when no way of asking is attached, so an
/// unattended run can never silently change a real system setting.
/// </summary>
public sealed class NeverConfirms : IUserConfirmation
{
    public static NeverConfirms Instance { get; } = new();

    public string ConsentSource => "nothing; no way of asking the user is attached";

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

/// <summary>
/// Consent carried by the command line, for the Owner-triggered
/// <c>--selftest &lt;dir&gt; --switch-default &lt;id&gt;</c> run and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The flag is itself the deliberate act: it can only be typed by whoever started the process,
/// and it names one specific browser. In that run there is, by construction, nobody present to
/// answer a dialog.
/// </para>
/// <para>
/// This exists because of a measured defect, not as a convenience. On Ubuntu 26.04.1 GNOME
/// Wayland, <c>--selftest … --switch-default …</c> opened a modal confirmation dialog over the
/// main window that nothing could ever answer; the run hung indefinitely and wrote no report at
/// all. Failing closed would have been correct, hanging was not.
/// </para>
/// <para>
/// It is <b>not</b> the default and it does not widen the consent model. <see cref="NeverConfirms"/>
/// is still what the view model falls back to, ordinary GUI use still goes through the real
/// dialog, and the host only selects this when <i>both</i> flags are present.
/// </para>
/// </remarks>
public sealed class CommandLineConsent : IUserConfirmation
{
    public static CommandLineConsent Instance { get; } = new();

    public string ConsentSource => "the --switch-default command-line flag; NO dialog was shown";

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
