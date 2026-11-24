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
    /// Returns true only when the user explicitly agreed. Anything else - dismissing the dialog,
    /// closing the window, no implementation being available - must return false.
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The fail-closed default: never confirms. Used when no UI is attached (tests, the self-test),
/// so an unattended run can never silently change a real system setting.
/// </summary>
public sealed class NeverConfirms : IUserConfirmation
{
    public static NeverConfirms Instance { get; } = new();

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
