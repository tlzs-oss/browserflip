namespace BrowserFlip.App.Desktop;

/// <summary>State of an auto-hiding docked bar.</summary>
public enum AutoHideState
{
    /// <summary>Bar fully visible.</summary>
    Expanded,

    /// <summary>Cursor left the bar; collapses when the delay elapses.</summary>
    PendingCollapse,

    /// <summary>Only the small tab is visible.</summary>
    Collapsed,

    /// <summary>Cursor is on the tab; expands after a short dwell (prevents accidental expansion).</summary>
    PendingExpand,
}

/// <summary>One observation of the environment, supplied by the window coordinator on each tick.</summary>
/// <param name="Now">Current time.</param>
/// <param name="CursorOverBar">Cursor is over the expanded bar (including a small hysteresis margin).</param>
/// <param name="CursorOverTab">Cursor is over / next to the collapsed tab.</param>
/// <param name="Suppressed">
/// Collapsing is not allowed right now: mouse button held over the bar, dragging, the context menu
/// is open, or the bar is being used with the keyboard.
/// </param>
public readonly record struct AutoHideInput(DateTimeOffset Now, bool CursorOverBar, bool CursorOverTab, bool Suppressed);

/// <summary>
/// Pure state machine for "auto show / hide" of the docked compact bar. It has no timers and no
/// window access: the coordinator feeds it observations and applies the resulting state.
/// Keeping it separate from window placement prevents feedback loops between the two.
/// </summary>
public sealed class AutoHideController
{
    public static readonly TimeSpan DefaultCollapseDelay = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan DefaultExpandDwell = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan DefaultInitialIdle = TimeSpan.FromSeconds(1.5);

    private DateTimeOffset _since;
    private DateTimeOffset _holdUntil;
    private bool _enabled;

    public TimeSpan CollapseDelay { get; init; } = DefaultCollapseDelay;

    public TimeSpan ExpandDwell { get; init; } = DefaultExpandDwell;

    /// <summary>After enabling or a forced expand, the bar stays open at least this long.</summary>
    public TimeSpan InitialIdle { get; init; } = DefaultInitialIdle;

    public AutoHideState State { get; private set; } = AutoHideState.Expanded;

    /// <summary>True when the bar is visually collapsed to its tab.</summary>
    public bool IsCollapsed => State is AutoHideState.Collapsed or AutoHideState.PendingExpand;

    public bool Enabled => _enabled;

    /// <summary>Enables or disables auto-hide. Disabling always expands the bar.</summary>
    public void SetEnabled(bool enabled, DateTimeOffset now)
    {
        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        Expand(now, InitialIdle);
    }

    /// <summary>Expands immediately (tray "show", keyboard focus, tab click) and holds briefly.</summary>
    public void ForceExpand(DateTimeOffset now) => Expand(now, InitialIdle);

    /// <summary>Advances the state machine and returns the new state.</summary>
    public AutoHideState Update(AutoHideInput input)
    {
        if (!_enabled)
        {
            State = AutoHideState.Expanded;
            return State;
        }

        switch (State)
        {
            case AutoHideState.Expanded:
                if (!input.Suppressed && !input.CursorOverBar && input.Now >= _holdUntil)
                {
                    Transition(AutoHideState.PendingCollapse, input.Now);
                }

                break;

            case AutoHideState.PendingCollapse:
                if (input.Suppressed || input.CursorOverBar)
                {
                    Transition(AutoHideState.Expanded, input.Now);
                }
                else if (input.Now - _since >= CollapseDelay)
                {
                    Transition(AutoHideState.Collapsed, input.Now);
                }

                break;

            case AutoHideState.Collapsed:
                if (input.CursorOverTab)
                {
                    Transition(AutoHideState.PendingExpand, input.Now);
                }

                break;

            case AutoHideState.PendingExpand:
                if (!input.CursorOverTab)
                {
                    Transition(AutoHideState.Collapsed, input.Now);
                }
                else if (input.Now - _since >= ExpandDwell)
                {
                    // Once opened by hovering, collapse only after the cursor leaves (no extra hold).
                    Expand(input.Now, TimeSpan.Zero);
                }

                break;
        }

        return State;
    }

    private void Expand(DateTimeOffset now, TimeSpan hold)
    {
        Transition(AutoHideState.Expanded, now);
        _holdUntil = now + hold;
    }

    private void Transition(AutoHideState state, DateTimeOffset now)
    {
        State = state;
        _since = now;
    }
}
