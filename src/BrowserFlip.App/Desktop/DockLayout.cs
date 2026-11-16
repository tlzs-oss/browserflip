namespace BrowserFlip.App.Desktop;

/// <summary>Screen edge the compact bar is docked to.</summary>
public enum DockEdge
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>Orientation of the compact bar.</summary>
public enum BarOrientation
{
    Horizontal,
    Vertical,
}

/// <summary>
/// Pure placement math for the compact bar (physical pixels). No Win32 or WPF dependencies,
/// so every rule is unit-testable.
/// </summary>
public static class DockLayout
{
    /// <summary>Left/right docking uses a vertical bar, top/bottom a horizontal one.</summary>
    public static BarOrientation OrientationFor(DockEdge? edge, BarOrientation floating) => edge switch
    {
        DockEdge.Left or DockEdge.Right => BarOrientation.Vertical,
        DockEdge.Top or DockEdge.Bottom => BarOrientation.Horizontal,
        _ => floating,
    };

    /// <summary>
    /// Rectangle of a bar docked to <paramref name="edge"/> inside <paramref name="workArea"/>.
    /// </summary>
    /// <param name="workArea">Monitor work area (excludes the taskbar).</param>
    /// <param name="edge">Docking edge.</param>
    /// <param name="width">Bar width in pixels.</param>
    /// <param name="height">Bar height in pixels.</param>
    /// <param name="position">Center of the bar along the edge, 0..1 of the edge length.</param>
    /// <param name="margin">Gap between the bar and the edge.</param>
    public static PixelRect Docked(PixelRect workArea, DockEdge edge, int width, int height, double position, int margin)
    {
        position = double.IsFinite(position) ? Math.Clamp(position, 0, 1) : 0.5;
        width = Math.Min(Math.Max(width, 1), Math.Max(1, workArea.Width - (2 * margin)));
        height = Math.Min(Math.Max(height, 1), Math.Max(1, workArea.Height - (2 * margin)));

        int left, top;
        if (edge is DockEdge.Left or DockEdge.Right)
        {
            left = edge == DockEdge.Left ? workArea.Left + margin : workArea.Right - margin - width;
            top = Along(workArea.Top + margin, workArea.Bottom - margin, height, position);
        }
        else
        {
            top = edge == DockEdge.Top ? workArea.Top + margin : workArea.Bottom - margin - height;
            left = Along(workArea.Left + margin, workArea.Right - margin, width, position);
        }

        return PixelRect.FromSize(left, top, width, height);
    }

    /// <summary>
    /// The small handle left visible when the bar is collapsed: flush with the edge, centered on the
    /// expanded bar, <paramref name="thickness"/> deep and <paramref name="length"/> long.
    /// </summary>
    public static PixelRect Tab(PixelRect workArea, DockEdge edge, PixelRect expanded, int thickness, int length)
    {
        thickness = Math.Max(1, thickness);
        if (edge is DockEdge.Left or DockEdge.Right)
        {
            length = Math.Min(Math.Max(1, length), workArea.Height);
            var top = Math.Clamp(expanded.Top + ((expanded.Height - length) / 2), workArea.Top, workArea.Bottom - length);
            var left = edge == DockEdge.Left ? workArea.Left : workArea.Right - thickness;
            return PixelRect.FromSize(left, top, thickness, length);
        }

        length = Math.Min(Math.Max(1, length), workArea.Width);
        var x = Math.Clamp(expanded.Left + ((expanded.Width - length) / 2), workArea.Left, workArea.Right - length);
        var y = edge == DockEdge.Top ? workArea.Top : workArea.Bottom - thickness;
        return PixelRect.FromSize(x, y, length, thickness);
    }

    /// <summary>
    /// Returns the edge the bar should dock to when a drag ends, or null to let it float.
    /// The cursor decides first (the user "pushes" the bar to an edge): the nearest work-area edge
    /// within <paramref name="threshold"/> of the cursor wins, including a cursor that went past the
    /// edge (onto the taskbar). Otherwise the window decides: a window edge within the threshold counts,
    /// and so does a window pushed past an outer edge (<paramref name="isOuterEdge"/>: no other monitor
    /// beyond it). Past an edge shared with another monitor the distance counts normally, so a floating
    /// bar dropped across two monitors is not docked to the inner edge.
    /// </summary>
    public static DockEdge? SnapEdge(PixelRect window, PixelRect workArea, int threshold, (int X, int Y)? cursor = null, Func<DockEdge, bool>? isOuterEdge = null)
    {
        if (cursor is { } c && workArea.Inflate(threshold, threshold).Contains(c.X, c.Y))
        {
            var byCursor = new (DockEdge Edge, int Distance)[]
            {
                (DockEdge.Left, c.X - workArea.Left),
                (DockEdge.Right, workArea.Right - 1 - c.X),
                (DockEdge.Top, c.Y - workArea.Top),
                (DockEdge.Bottom, workArea.Bottom - 1 - c.Y),
            }.MinBy(d => d.Distance);
            if (byCursor.Distance <= threshold)
            {
                return byCursor.Edge;
            }
        }

        // Past an outer edge (overshoot) is distance 0, never "far away".
        int Distance(DockEdge edge, int inside) => inside >= 0 ? inside : (isOuterEdge?.Invoke(edge) ?? true) ? 0 : -inside;
        var distances = new (DockEdge Edge, int Distance)[]
        {
            (DockEdge.Left, Distance(DockEdge.Left, window.Left - workArea.Left)),
            (DockEdge.Right, Distance(DockEdge.Right, workArea.Right - window.Right)),
            (DockEdge.Top, Distance(DockEdge.Top, window.Top - workArea.Top)),
            (DockEdge.Bottom, Distance(DockEdge.Bottom, workArea.Bottom - window.Bottom)),
        };

        var best = distances.MinBy(d => d.Distance);
        return best.Distance <= threshold ? best.Edge : null;
    }

    /// <summary>
    /// Area where the cursor expands a collapsed bar: the whole length of the expanded bar along the
    /// edge (plus <paramref name="alongMargin"/>), <paramref name="depth"/> pixels into the work area.
    /// When nothing stops the cursor at that edge (no taskbar there and possibly a neighbor monitor),
    /// the zone also extends <paramref name="overshoot"/> pixels past the edge so a cursor that slips
    /// onto the neighbor still counts; it never extends onto a taskbar.
    /// </summary>
    public static PixelRect TriggerZone(PixelRect workArea, PixelRect monitorBounds, DockEdge edge, PixelRect expanded, int depth, int alongMargin, int overshoot)
    {
        depth = Math.Max(1, depth);
        if (edge is DockEdge.Left or DockEdge.Right)
        {
            var top = Math.Max(workArea.Top, expanded.Top - alongMargin);
            var bottom = Math.Min(workArea.Bottom, expanded.Bottom + alongMargin);
            return edge == DockEdge.Left
                ? new PixelRect(workArea.Left - (workArea.Left == monitorBounds.Left ? overshoot : 0), top, workArea.Left + depth, bottom)
                : new PixelRect(workArea.Right - depth, top, workArea.Right + (workArea.Right == monitorBounds.Right ? overshoot : 0), bottom);
        }

        var left = Math.Max(workArea.Left, expanded.Left - alongMargin);
        var right = Math.Min(workArea.Right, expanded.Right + alongMargin);
        return edge == DockEdge.Top
            ? new PixelRect(left, workArea.Top - (workArea.Top == monitorBounds.Top ? overshoot : 0), right, workArea.Top + depth)
            : new PixelRect(left, workArea.Bottom - depth, right, workArea.Bottom + (workArea.Bottom == monitorBounds.Bottom ? overshoot : 0));
    }

    /// <summary>Thickness of the visible strip left by a collapsed bar: about 2 DIP, kept within 3-5 pixels.</summary>
    public static int HintThickness(double scale) => Math.Clamp((int)Math.Round(2 * scale), 3, 5);

    /// <summary>Depth of the expand trigger zone: about 10 DIP, kept within 12-20 pixels.</summary>
    public static int TriggerDepth(double scale) => Math.Clamp((int)Math.Round(10 * scale), 12, 20);

    /// <summary>Position (0..1) of the window's center along <paramref name="edge"/>.</summary>
    public static double PositionAlong(PixelRect window, PixelRect workArea, DockEdge edge)
    {
        var (start, length, center) = edge is DockEdge.Left or DockEdge.Right
            ? (workArea.Top, workArea.Height, window.Top + (window.Height / 2.0))
            : (workArea.Left, workArea.Width, window.Left + (window.Width / 2.0));
        return length <= 0 ? 0.5 : Math.Clamp((center - start) / length, 0, 1);
    }

    /// <summary>Moves (and if needed shrinks) <paramref name="window"/> so it lies fully inside <paramref name="area"/>.</summary>
    public static PixelRect ClampInto(PixelRect window, PixelRect area)
    {
        var width = Math.Min(window.Width, area.Width);
        var height = Math.Min(window.Height, area.Height);
        var left = Math.Clamp(window.Left, area.Left, area.Right - width);
        var top = Math.Clamp(window.Top, area.Top, area.Bottom - height);
        return PixelRect.FromSize(left, top, width, height);
    }

    /// <summary>
    /// Guarantees a saved window rectangle is usable: if less than <paramref name="minVisible"/> pixels
    /// (in both directions) overlap a monitor work area, it is moved onto the nearest / primary monitor.
    /// </summary>
    public static PixelRect EnsureVisible(PixelRect window, IReadOnlyList<MonitorInfo> monitors, int minVisible)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0 || window.IsEmpty)
        {
            return window;
        }

        var best = monitors
            .Select(m => (Monitor: m, Overlap: window.Intersect(m.WorkArea)))
            .OrderByDescending(x => x.Overlap.Area)
            .First();

        if (best.Overlap.Width >= Math.Min(minVisible, window.Width) && best.Overlap.Height >= Math.Min(minVisible, window.Height))
        {
            // Visible enough; still make sure the title area (top) is reachable.
            return window.Top < best.Monitor.WorkArea.Top
                ? window with { Top = best.Monitor.WorkArea.Top, Bottom = best.Monitor.WorkArea.Top + window.Height }
                : window;
        }

        var target = best.Overlap.Area > 0 ? best.Monitor : monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var work = target.WorkArea;
        var centered = PixelRect.FromSize(
            work.Left + ((work.Width - window.Width) / 2),
            work.Top + ((work.Height - window.Height) / 2),
            window.Width,
            window.Height);
        return ClampInto(centered, work);
    }

    private static int Along(int start, int end, int size, double position)
    {
        var available = end - start;
        if (size >= available)
        {
            return start;
        }

        var center = start + (available * position);
        return (int)Math.Round(Math.Clamp(center - (size / 2.0), start, end - size));
    }
}
