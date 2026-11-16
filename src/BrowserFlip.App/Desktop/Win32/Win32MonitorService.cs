using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BrowserFlip.App.Desktop.Win32;

/// <summary>
/// <see cref="IMonitorService"/> backed by EnumDisplayMonitors / GetMonitorInfoW / GetDpiForMonitor.
/// Construct on the UI thread: <see cref="DisplayChanged"/> is raised on that thread's dispatcher,
/// debounced so a burst of signals (resolution change + work-area change + DPI change) yields one event.
/// Query methods are thread-agnostic.
/// </summary>
public sealed class Win32MonitorService : IMonitorService
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private readonly HwndSource? _window;
    private int _disposed;

    public Win32MonitorService()
        : this(DefaultDebounce, listenForChanges: true)
    {
    }

    /// <param name="debounce">Quiet period after the last signal before <see cref="DisplayChanged"/> fires.</param>
    /// <param name="listenForChanges">False skips SystemEvents and the hidden window (queries only).</param>
    internal Win32MonitorService(TimeSpan debounce, bool listenForChanges)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _debounce = new DispatcherTimer(debounce, DispatcherPriority.Normal, OnDebounceElapsed, _dispatcher);
        _debounce.Stop();

        if (!listenForChanges)
        {
            return;
        }

        _window = HiddenMessageWindow.Create("BrowserFlip.DisplayWatcher", WndProc);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler? DisplayChanged;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<IntPtr>();
        NativeMethods.MonitorEnumProc callback = (hMonitor, _, _, _) =>
        {
            handles.Add(hMonitor);
            return true;
        };

        _ = NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        var result = new List<MonitorInfo>(handles.Count);
        foreach (var handle in handles)
        {
            if (TryGetInfo(handle) is { } info)
            {
                result.Add(info);
            }
        }

        if (result.Count == 0)
        {
            result.Add(FallbackPrimary());
        }

        return result;
    }

    public MonitorInfo FromPoint(int x, int y)
    {
        var handle = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = x, Y = y }, NativeMethods.MonitorDefaultToNearest);
        return TryGetInfo(handle) ?? PrimaryOrFallback();
    }

    public MonitorInfo FromWindow(IntPtr hwnd)
    {
        var handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        return TryGetInfo(handle) ?? PrimaryOrFallback();
    }

    public (int X, int Y)? GetCursorPosition()
        => NativeMethods.GetCursorPos(out var pt) ? (pt.X, pt.Y) : null;

    public bool IsFullScreenOrPresentationActive()
    {
        var hr = NativeMethods.SHQueryUserNotificationState(out var state);
        return hr >= 0 && state is NativeMethods.QunsBusy or NativeMethods.QunsRunningD3DFullScreen or NativeMethods.QunsPresentationMode;
    }

    public bool IsMouseButtonDown()
        => IsKeyDown(NativeMethods.VkLButton) || IsKeyDown(NativeMethods.VkRButton) || IsKeyDown(NativeMethods.VkMButton);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // SystemEvents holds static references: always unsubscribe, from whatever thread we are on.
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        if (_dispatcher.CheckAccess())
        {
            DisposeUiResources();
        }
        else if (!_dispatcher.HasShutdownStarted)
        {
            _dispatcher.Invoke(DisposeUiResources);
        }

        // Otherwise the dispatcher thread is gone and the OS destroyed its windows with it.
    }

    /// <summary>Queues a (debounced) <see cref="DisplayChanged"/>. Safe from any thread.</summary>
    internal void SignalDisplayChange()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            if (!_dispatcher.HasShutdownStarted)
            {
                _ = _dispatcher.BeginInvoke(SignalDisplayChange);
            }

            return;
        }

        // Restart: fire once, ~debounce after the LAST signal.
        _debounce.Stop();
        _debounce.Start();
    }

    internal static MonitorInfo? TryGetInfo(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero)
        {
            return null;
        }

        var info = new NativeMethods.MONITORINFOEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
            szDevice = string.Empty,
        };

        if (!NativeMethods.GetMonitorInfoW(hMonitor, ref info))
        {
            return null;
        }

        return new MonitorInfo(
            info.szDevice ?? string.Empty,
            info.rcMonitor.ToPixelRect(),
            info.rcWork.ToPixelRect(),
            GetScale(hMonitor),
            (info.dwFlags & NativeMethods.MonitorInfoFPrimary) != 0);
    }

    private static double GetScale(IntPtr hMonitor)
    {
        try
        {
            var hr = NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _);
            return hr >= 0 && dpiX > 0 ? dpiX / (double)NativeMethods.DefaultDpi : 1.0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    private static bool IsKeyDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private MonitorInfo PrimaryOrFallback()
    {
        var all = GetMonitors();
        return all.FirstOrDefault(m => m.IsPrimary) ?? all[0];
    }

    private static MonitorInfo FallbackPrimary()
    {
        var primary = NativeMethods.MonitorFromPoint(default, NativeMethods.MonitorDefaultToPrimary);
        if (TryGetInfo(primary) is { } info)
        {
            return info;
        }

        var bounds = PixelRect.FromSize(0, 0, Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCxScreen)), Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCyScreen)));
        return new MonitorInfo(string.Empty, bounds, bounds, 1.0, true);
    }

    private void DisposeUiResources()
    {
        _debounce.Stop();
        _window?.Dispose();
    }

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounce.Stop();
        if (Volatile.Read(ref _disposed) == 0)
        {
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WmDisplayChange:
                SignalDisplayChange();
                break;
            case NativeMethods.WmSettingChange when (long)wParam == NativeMethods.SpiSetWorkArea:
                SignalDisplayChange();
                break;
        }

        // Never mark handled: WPF / DefWindowProc still see the messages.
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => SignalDisplayChange();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            SignalDisplayChange();
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.Window)
        {
            SignalDisplayChange();
        }
    }
}
