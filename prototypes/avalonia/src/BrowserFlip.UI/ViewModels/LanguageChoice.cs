using System.ComponentModel;

namespace BrowserFlip.UI.ViewModels;

/// <summary>
/// One entry in the language picker. "Follow the OS" is stored as a null language code, which a
/// combo box cannot select, so it needs an object of its own rather than a bare string.
/// <para>
/// It raises <see cref="PropertyChanged"/> for <see cref="Display"/> because its own label is
/// translated, so it changes when the user changes the UI language - including the language they
/// are changing it with. Without that notification the picker kept whatever text it had rendered
/// first: the Owner's acceptance screenshot showed a Japanese window whose "follow the system"
/// entry still read the Chinese 跟随系统 from an earlier selection.
/// </para>
/// </summary>
public sealed class LanguageChoice : INotifyPropertyChanged
{
    public LanguageChoice(string? code) => Code = code;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The culture name, or <c>null</c> for "follow the OS".</summary>
    public string? Code { get; }

    /// <summary>What the picker shows. Re-read on each access so it follows the UI language.</summary>
    public string Display => Code ?? Localization.Strings.Get("Language_Auto");

    /// <summary>
    /// Tells the view to read <see cref="Display"/> again. Called when the UI language changes;
    /// the entry cannot know that by itself, and the view has no other reason to re-read it.
    /// </summary>
    public void NotifyLanguageChanged()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));

    public override string ToString() => Display;
}
