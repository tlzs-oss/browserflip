using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;

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
}
