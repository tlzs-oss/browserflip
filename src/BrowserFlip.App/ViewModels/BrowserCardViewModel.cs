using System.Windows.Media;
using BrowserFlip.App.Localization;
using BrowserFlip.Core.Defaults;
using BrowserFlip.Core.Detection;

namespace BrowserFlip.App.ViewModels;

/// <summary>One browser card in the 2-column list.</summary>
public sealed class BrowserCardViewModel : ObservableObject
{
    private BrowserDefaultRole _role;

    public BrowserCardViewModel(BrowserInfo browser, ImageSource? icon, BrowserDefaultRole role)
    {
        Browser = browser;
        Icon = icon;
        _role = role;
    }

    public BrowserInfo Browser { get; }

    public string DisplayName => Browser.DisplayName;

    public ImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    public bool IsAvailable => Browser.Health != BrowserHealth.ExecutableMissing;

    public BrowserDefaultRole Role
    {
        get => _role;
        set
        {
            if (SetProperty(ref _role, value))
            {
                Relocalize();
            }
        }
    }

    public bool IsDefault => Role == BrowserDefaultRole.Both;

    public bool HasBadge => Role != BrowserDefaultRole.NotDefault;

    public bool IsPartialDefault => Role is BrowserDefaultRole.HttpOnly or BrowserDefaultRole.HttpsOnly;

    public string BadgeText => Role switch
    {
        BrowserDefaultRole.Both => Loc.Instance["Badge_Default"],
        BrowserDefaultRole.HttpOnly => Loc.Instance["Badge_HttpOnly"],
        BrowserDefaultRole.HttpsOnly => Loc.Instance["Badge_HttpsOnly"],
        _ => string.Empty,
    };

    public string HintText => Browser.Health switch
    {
        BrowserHealth.ExecutableMissing => Loc.Instance["Card_Missing"],
        _ when IsDefault => Loc.Instance["Card_HintDefault"],
        // Not in RegisteredApplications: Settings can only open the top-level Default apps page.
        BrowserHealth.NotInRegisteredApplications => Loc.Instance["Card_HintTopPage"],
        _ => Loc.Instance["Card_Hint"],
    };

    /// <summary>Name announced by screen readers.</summary>
    public string AutomationName => HasBadge ? $"{DisplayName}, {BadgeText}. {HintText}" : $"{DisplayName}. {HintText}";

    public void Relocalize()
    {
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(HasBadge));
        OnPropertyChanged(nameof(IsPartialDefault));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(AutomationName));
    }
}
