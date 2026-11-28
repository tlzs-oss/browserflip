using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BrowserFlip.UI.Localization;
using BrowserFlip.UI.ViewModels;
using BrowserFlip.UI.Views;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// Regression tests for the language picker's own labels.
/// <para>
/// The Owner's acceptance screenshot showed a Japanese window whose "follow the system" entry
/// read the Chinese 跟随系统. The entry's label is translated and the view binds to it through
/// <c>DisplayMemberBinding</c>, but <see cref="LanguageChoice"/> raised no change notification,
/// so the binding was never re-read and the text stayed in whichever language it was first drawn
/// in - which, for the entry you change the language with, is the language you just left.
/// </para>
/// <para>
/// These mutate the process-wide UI culture, so they join the serialised collection.
/// </para>
/// </summary>
[Collection(SerialUiCulture.Name)]
public class LanguagePickerTests
{
    private static MainViewModel Model()
    {
        var associations = new FakeAssociations().With("http", "com.apple.Safari").With("https", "com.apple.Safari");
        var catalog = new FakeCatalog().Add(Browsers.Mac("com.apple.Safari", "Safari"));
        var model = new MainViewModel(
            catalog, _ => new FakeSwitcher(associations), new FakeIcons(),
            new InMemorySettingsStore(), new BrowserFlip.Core.Defaults.DefaultBrowserService(associations),
            new BrowserFlip.UI.Platform.FullCapabilities("test platform", osConfirms: true));
        model.Initialize();
        return model;
    }

    [Fact]
    public void Changing_the_language_relabels_the_follow_the_system_entry()
    {
        try
        {
            var model = Model();
            var followSystem = model.LanguageChoices.Single(c => c.Code is null);

            model.Language = "zh-CN";
            var chinese = followSystem.Display;
            model.Language = "ja";
            var japanese = followSystem.Display;

            // This is the Owner's sequence: pick Chinese, then pick Japanese. The entry must not
            // still be reading 跟随系统 once the window is Japanese.
            Assert.NotEqual(chinese, japanese);
            Assert.Equal("システムに合わせる", japanese);
            Assert.Equal("跟随系统", chinese);
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [Fact]
    public void Changing_the_language_notifies_every_entry_so_a_binding_is_re_read()
    {
        try
        {
            var model = Model();
            var notified = new List<string?>();
            foreach (var choice in model.LanguageChoices)
            {
                var captured = choice;
                ((INotifyPropertyChanged)choice).PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LanguageChoice.Display))
                    {
                        notified.Add(captured.Code);
                    }
                };
            }

            model.Language = "zh-CN";

            // Every entry, not just the translated one: a binding that is never told to re-read
            // is exactly as stale as no binding at all, which is what the defect was.
            Assert.Equal(model.LanguageChoices.Count, notified.Count);
            Assert.Contains(null, notified);
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [Fact]
    public void The_entries_are_not_shared_between_view_models()
    {
        try
        {
            var first = Model();
            var second = Model();

            // A process-wide entry would be notified by, and on behalf of, every view model that
            // ever existed, which is why the static singleton was dropped.
            Assert.NotSame(first.LanguageChoices[0], second.LanguageChoices[0]);
            Assert.Null(first.LanguageChoices[0].Code);
            Assert.Null(second.LanguageChoices[0].Code);
        }
        finally
        {
            Strings.UseLanguage(null);
        }
    }

    [AvaloniaTheory]
    [InlineData(typeof(MainWindow))]
    public void The_picker_binds_its_label_rather_than_falling_back_to_ToString(Type windowType)
    {
        var window = (Window)Activator.CreateInstance(windowType)!;
        window.DataContext = Model();
        window.Show();

        var box = window.FindControl<ComboBox>("LanguageBox")!;

        // Without DisplayMemberBinding the combo box renders each entry with ToString(), which
        // is read once and no notification can ever refresh - the original defect. Avalonia
        // leaves ItemTemplate null and uses DisplayMemberBinding internally, so this is the
        // property to assert; the rendered text itself is only reachable once containers are
        // realised, and the view-model tests above cover the refresh itself.
        Assert.NotNull(box.DisplayMemberBinding);
        Assert.Null(box.ItemTemplate);
    }
}
