namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// Serialises every test that switches the UI language.
///
/// <see cref="BrowserFlip.UI.Localization.Strings.UseLanguage"/> writes
/// <see cref="System.Globalization.CultureInfo.DefaultThreadCurrentUICulture"/>, which is
/// process-wide state. xUnit runs test classes in parallel by default, so two classes
/// doing that at once race: one class sets "ja", the other sets "en" before the first
/// reads its resource, and the first sees English.
///
/// This was not theoretical. CI failed on
/// A_platform_error_is_shown_as_a_labelled_detail_not_as_the_message because the message
/// came back in English despite UseLanguage("ja"). It passed on a developer machine whose
/// own UI culture is Japanese, which hid the race: there the fallback value was Japanese
/// anyway. Putting every culture-mutating class in one collection stops them overlapping.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialUiCulture
{
    public const string Name = "ui-culture";
}
