namespace BrowserFlip.App.Desktop;

/// <summary>
/// Follows one Windows move/size loop of the main window (WM_ENTERSIZEMOVE ... WM_EXITSIZEMOVE) and tells
/// whether it was a user <b>move</b> (WM_MOVING seen, never WM_SIZING). Only such a move may dock the window
/// to an edge (BF-01): a resize, a loop without movement, or a window that is merely shown near an edge never
/// does. Pure logic, unit-tested.
/// </summary>
internal sealed class MainWindowMoveTracker
{
    private bool _active;
    private bool _moved;
    private bool _sized;
    private PixelRect _start;

    public bool IsActive => _active;

    /// <summary>WM_ENTERSIZEMOVE with the window rectangle at that moment.</summary>
    public void Enter(PixelRect start)
    {
        _active = true;
        _moved = false;
        _sized = false;
        _start = start;
    }

    /// <summary>WM_MOVING. Returns true when it belongs to an active loop (preview may update).</summary>
    public bool Moving()
    {
        if (!_active)
        {
            return false;
        }

        _moved = true;
        return true;
    }

    /// <summary>WM_SIZING.</summary>
    public void Sizing() => _sized = _active || _sized;

    /// <summary>WM_EXITSIZEMOVE: the rectangle before the drag when this was a move (not a resize), else null.</summary>
    public PixelRect? Exit()
    {
        var result = _active && _moved && !_sized && !_start.IsEmpty ? _start : (PixelRect?)null;
        _active = false;
        _moved = false;
        _sized = false;
        return result;
    }
}
