using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;
using BrowserFlip.UI.Localization;

namespace BrowserFlip.UI.ViewModels;

/// <summary>One row in the browser list. Holds no UI-framework type: the icon stays raw PNG bytes.</summary>
public sealed class BrowserItemViewModel : ObservableObject
{
    private BrowserDefaultRole _role;
    private bool _busy;

    public BrowserItemViewModel(BrowserInfo browser, byte[]? iconPng)
    {
        Browser = browser ?? throw new ArgumentNullException(nameof(browser));
        IconPng = iconPng;
    }

    public BrowserInfo Browser { get; }

    public byte[]? IconPng { get; }

    public string DisplayName => Browser.DisplayName;

    /// <summary>Bundle identifier on macOS, ProgID on Windows. Shown as the secondary line.</summary>
    public string Identifier => Browser.HttpsProgId ?? Browser.Id;

    public bool IsAvailable => Browser.Health != BrowserHealth.ExecutableMissing;

    public BrowserDefaultRole Role
    {
        get => _role;
        set
        {
            if (Set(ref _role, value))
            {
                Raise(nameof(IsDefault));
                Raise(nameof(IsPartialDefault));
                Raise(nameof(PartialBadgeLabel));
                Raise(nameof(CanSetDefault));
            }
        }
    }

    public bool IsDefault => Role == BrowserDefaultRole.Both;

    public bool IsPartialDefault => Role is BrowserDefaultRole.HttpOnly or BrowserDefaultRole.HttpsOnly;

    /// <summary>True while a switch request for this row is in flight.</summary>
    public bool Busy
    {
        get => _busy;
        set
        {
            if (Set(ref _busy, value))
            {
                Raise(nameof(CanSetDefault));
            }
        }
    }

    public bool CanSetDefault => IsAvailable && !IsDefault && !Busy;

    // Localised text for the row. It lives here rather than on the parent because a DataTemplate
    // binds against the item, and an ancestor binding for a plain label is needless indirection.
    // The rows are rebuilt whenever the language changes, so these are read fresh each time.

    /// <summary>Caption of the per-row action button.</summary>
    public string SetDefaultLabel => Strings.Get("List_SetDefault");

    /// <summary>Badge shown on the browser that is the default for both http and https.</summary>
    public string DefaultBadgeLabel => Strings.Get("Badge_Default");

    /// <summary>
    /// Badge shown when a browser handles only one of http / https. It names the protocol it
    /// does handle, which is both more useful than the word "partial" and needs no translation -
    /// the product's resources have no badge-sized string for this case.
    /// </summary>
    public string PartialBadgeLabel => Role switch
    {
        BrowserDefaultRole.HttpOnly => "HTTP",
        BrowserDefaultRole.HttpsOnly => "HTTPS",
        _ => string.Empty,
    };
}
