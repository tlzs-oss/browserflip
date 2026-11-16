using System.ComponentModel;

namespace BrowserFlip.App.Desktop.Win32;

/// <summary>
/// <see cref="IWindowPlacement"/> using GetWindowRect / SetWindowPos (physical pixels, same coordinate space both ways).
/// Note: on Windows 10/11 the rectangle includes the invisible DWM resize borders of framed windows;
/// borderless (WindowStyle=None) windows have none, so bounds equal the visible area.
/// </summary>
public sealed class Win32WindowPlacement : IWindowPlacement
{
    private const uint MoveFlags = NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder;

    /// <exception cref="ArgumentException"><paramref name="hwnd"/> is zero.</exception>
    /// <exception cref="Win32Exception">GetWindowRect failed (e.g. the window was destroyed).</exception>
    public PixelRect GetBounds(IntPtr hwnd)
    {
        ThrowIfZero(hwnd);
        return NativeMethods.GetWindowRect(hwnd, out var rect) ? rect.ToPixelRect() : throw new Win32Exception();
    }

    /// <exception cref="ArgumentException"><paramref name="hwnd"/> is zero.</exception>
    /// <exception cref="Win32Exception">SetWindowPos failed.</exception>
    public void SetBounds(IntPtr hwnd, PixelRect bounds)
    {
        ThrowIfZero(hwnd);
        if (!NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height), MoveFlags))
        {
            throw new Win32Exception();
        }
    }

    /// <exception cref="ArgumentException"><paramref name="hwnd"/> is zero.</exception>
    /// <exception cref="Win32Exception">SetWindowPos failed.</exception>
    public void Move(IntPtr hwnd, int left, int top)
    {
        ThrowIfZero(hwnd);
        if (!NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0, MoveFlags | SwpNoSize))
        {
            throw new Win32Exception();
        }
    }

    private const uint SwpNoSize = 0x0001;

    private static void ThrowIfZero(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(hwnd));
        }
    }
}
