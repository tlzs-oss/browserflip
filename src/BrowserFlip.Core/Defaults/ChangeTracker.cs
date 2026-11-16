using BrowserFlip.Core.Detection;

namespace BrowserFlip.Core.Defaults;

/// <summary>Result of evaluating a pending default-browser change.</summary>
public enum ChangeOutcome
{
    /// <summary>Nothing changed yet; keep waiting.</summary>
    Pending,

    /// <summary>The target browser is now the default for both http and https.</summary>
    AppliedToTarget,

    /// <summary>The target browser now handles only one of http/https.</summary>
    PartiallyApplied,

    /// <summary>The defaults changed, but to a different application.</summary>
    ChangedToOther,

    /// <summary>The user came back without changing anything (cancelled or closed Settings).</summary>
    NoChange,
}

/// <summary>
/// Tracks a change the user was asked to make in Windows Settings. Windows always requires the user
/// to confirm the change themselves, so BrowserFlip only observes the result.
/// </summary>
public sealed class ChangeTracker
{
    private readonly DefaultBrowserState _before;

    public ChangeTracker(BrowserInfo target, DefaultBrowserState before)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        _before = before ?? throw new ArgumentNullException(nameof(before));
    }

    public BrowserInfo Target { get; }

    /// <summary>Evaluates <paramref name="current"/>.</summary>
    /// <param name="current">The latest state.</param>
    /// <param name="userReturned">True when the user has returned to BrowserFlip (window re-activated) or the wait timed out.</param>
    public ChangeOutcome Evaluate(DefaultBrowserState current, bool userReturned)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.SameAssociationsAs(_before) && _before.RoleOf(Target) != BrowserDefaultRole.Both)
        {
            // Nothing changed since Settings was opened (even if the target was already a partial default).
            return userReturned ? ChangeOutcome.NoChange : ChangeOutcome.Pending;
        }

        switch (current.RoleOf(Target))
        {
            case BrowserDefaultRole.Both:
                return ChangeOutcome.AppliedToTarget;
            case BrowserDefaultRole.HttpOnly or BrowserDefaultRole.HttpsOnly:
                // Windows may update http and https a moment apart; only report partial once the user is back.
                return userReturned ? ChangeOutcome.PartiallyApplied : ChangeOutcome.Pending;
        }

        if (!current.SameAssociationsAs(_before))
        {
            return userReturned ? ChangeOutcome.ChangedToOther : ChangeOutcome.Pending;
        }

        return userReturned ? ChangeOutcome.NoChange : ChangeOutcome.Pending;
    }
}
