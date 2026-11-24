using Avalonia.Controls;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.UI.Views;

/// <summary>
/// A modal yes/no dialog. Cancel is the default action on Escape and on closing the window, so
/// every way of dismissing it that is not a deliberate click on the confirm button means "no".
/// </summary>
public partial class ConfirmationDialog : Window
{
    private bool _confirmed;

    public ConfirmationDialog()
    {
        InitializeComponent();

        var confirm = this.FindControl<Button>("ConfirmButton");
        if (confirm is not null)
        {
            confirm.Click += (_, _) =>
            {
                _confirmed = true;
                Close();
            };
        }

        var cancel = this.FindControl<Button>("CancelButton");
        if (cancel is not null)
        {
            cancel.Click += (_, _) => Close();
        }
    }

    /// <summary>Shows the dialog modally and returns true only when the user confirmed.</summary>
    public async Task<bool> AskAsync(Window owner, string title, string message)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Title = title;
        var text = this.FindControl<TextBlock>("MessageText");
        if (text is not null)
        {
            text.Text = message;
        }

        await ShowDialog(owner).ConfigureAwait(true);
        return _confirmed;
    }
}

/// <summary>
/// Shows <see cref="ConfirmationDialog"/> over the main window. Supplied to the view model by
/// the host so the view model itself stays free of UI types.
/// </summary>
public sealed class DialogUserConfirmation : IUserConfirmation
{
    private readonly Func<Window?> _owner;

    public DialogUserConfirmation(Func<Window?> owner) => _owner = owner;

    public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var owner = _owner();
        if (owner is null)
        {
            // No window to parent a modal dialog to, so we cannot have asked. Fail closed.
            return false;
        }

        return await new ConfirmationDialog().AskAsync(owner, title, message).ConfigureAwait(true);
    }
}
