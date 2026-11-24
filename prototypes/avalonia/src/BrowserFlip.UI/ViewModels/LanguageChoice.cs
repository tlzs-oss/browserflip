namespace BrowserFlip.UI.ViewModels;

/// <summary>
/// One entry in the language picker. "Follow the OS" is stored as a null language code, which a
/// combo box cannot select, so it needs an object of its own rather than a bare string.
/// </summary>
public sealed class LanguageChoice
{
    /// <summary>The entry meaning "use whatever the operating system is set to".</summary>
    public static LanguageChoice FollowSystem { get; } = new(null);

    public LanguageChoice(string? code) => Code = code;

    /// <summary>The culture name, or <c>null</c> for "follow the OS".</summary>
    public string? Code { get; }

    /// <summary>What the picker shows. Re-read on each access so it follows the UI language.</summary>
    public string Display => Code ?? Localization.Strings.Get("Language_Auto");

    public override string ToString() => Display;
}
