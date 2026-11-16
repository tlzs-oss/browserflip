using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using BrowserFlip.App.Desktop;
using BrowserFlip.App.Desktop.Win32;
using Microsoft.Win32;

namespace BrowserFlip.Core.Tests.Desktop;

public sealed class Win32MonitorServiceTests
{
    [Fact]
    public void Struct_sizes_match_the_native_layout()
    {
        Assert.Equal(104, Marshal.SizeOf<NativeMethods.MONITORINFOEXW>());
        Assert.Equal(16, Marshal.SizeOf<NativeMethods.RECT>());
        Assert.Equal(8, Marshal.SizeOf<NativeMethods.POINT>());
    }

    [Fact]
    public void GetMonitors_returns_exactly_one_primary_with_sane_geometry()
    {
        using var service = new Win32MonitorService(TimeSpan.FromMilliseconds(50), listenForChanges: false);

        var monitors = service.GetMonitors();

        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.IsPrimary);
        foreach (var m in monitors)
        {
            Assert.False(m.Bounds.IsEmpty);
            Assert.False(m.WorkArea.IsEmpty);
            Assert.Equal(m.WorkArea, m.WorkArea.Intersect(m.Bounds));
            Assert.True(m.Scale >= 1.0, $"Scale {m.Scale} for {m.DeviceName}");
            Assert.False(string.IsNullOrEmpty(m.DeviceName));
        }
    }

    [Fact]
    public void FromPoint_primary_center_returns_primary()
    {
        using var service = new Win32MonitorService(TimeSpan.FromMilliseconds(50), listenForChanges: false);
        var primary = service.GetMonitors().Single(m => m.IsPrimary);

        var found = service.FromPoint(primary.Bounds.Left + (primary.Bounds.Width / 2), primary.Bounds.Top + (primary.Bounds.Height / 2));

        Assert.Equal(primary, found);
    }

    [Fact]
    public void FromPoint_far_away_returns_nearest_monitor()
    {
        using var service = new Win32MonitorService(TimeSpan.FromMilliseconds(50), listenForChanges: false);

        var found = service.FromPoint(-1_000_000, -1_000_000);

        Assert.Contains(found, service.GetMonitors());
    }

    [Fact]
    public void FromWindow_with_zero_handle_falls_back_to_a_monitor()
    {
        using var service = new Win32MonitorService(TimeSpan.FromMilliseconds(50), listenForChanges: false);

        Assert.Contains(service.FromWindow(IntPtr.Zero), service.GetMonitors());
    }

    [Fact]
    public void Environment_queries_do_not_throw()
    {
        using var service = new Win32MonitorService(TimeSpan.FromMilliseconds(50), listenForChanges: false);

        var cursor = service.GetCursorPosition();
        _ = service.IsMouseButtonDown();
        _ = service.IsFullScreenOrPresentationActive();

        if (cursor is { } c)
        {
            // Cursor is clamped to the virtual screen, so some monitor must be "nearest" (not necessarily containing it on odd layouts).
            Assert.Contains(service.FromPoint(c.X, c.Y), service.GetMonitors());
        }
    }

    [Fact]
    public void Burst_of_signals_is_debounced_into_one_event_on_the_creating_thread() => Sta.Run(() =>
    {
        using var service = new Win32MonitorService(TimeSpan.FromMilliseconds(60), listenForChanges: true);
        var uiThread = Environment.CurrentManagedThreadId;
        var count = 0;
        var raisedOn = -1;
        service.DisplayChanged += (_, _) =>
        {
            count++;
            raisedOn = Environment.CurrentManagedThreadId;
        };

        for (var i = 0; i < 5; i++)
        {
            service.SignalDisplayChange();
        }

        // Also from a background thread: must be marshalled to the UI dispatcher.
        Task.Run(service.SignalDisplayChange).Wait();

        Sta.PumpFor(TimeSpan.FromMilliseconds(400));

        Assert.Equal(1, count);
        Assert.Equal(uiThread, raisedOn);
    });

    [Fact]
    public void No_event_after_dispose_and_dispose_is_idempotent() => Sta.Run(() =>
    {
        var service = new Win32MonitorService(TimeSpan.FromMilliseconds(40), listenForChanges: true);
        var count = 0;
        service.DisplayChanged += (_, _) => count++;

        service.SignalDisplayChange();
        service.Dispose();
        service.Dispose();
        service.SignalDisplayChange();
        Sta.PumpFor(TimeSpan.FromMilliseconds(200));

        Assert.Equal(0, count);
    });

    [Fact]
    public void Dispose_from_another_thread_marshals_to_the_ui_thread() => Sta.Run(() =>
    {
        var service = new Win32MonitorService(TimeSpan.FromMilliseconds(40), listenForChanges: true);
        var dispose = Task.Run(service.Dispose);

        // The background Dispose needs this dispatcher to run the window teardown.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!dispose.IsCompleted && DateTime.UtcNow < deadline)
        {
            Sta.PumpFor(TimeSpan.FromMilliseconds(20));
        }

        Assert.True(dispose.IsCompletedSuccessfully);
    });
}

public sealed class Win32PixelRectTests
{
    [Fact]
    public void Size_and_area()
    {
        var r = PixelRect.FromSize(10, 20, 30, 40);

        Assert.Equal(new PixelRect(10, 20, 40, 60), r);
        Assert.Equal(30, r.Width);
        Assert.Equal(40, r.Height);
        Assert.Equal(1200, r.Area);
        Assert.False(r.IsEmpty);
    }

    [Fact]
    public void Empty_and_inverted_rects_have_zero_area()
    {
        Assert.True(default(PixelRect).IsEmpty);
        Assert.Equal(0, new PixelRect(10, 10, 5, 50).Area);
        Assert.Equal(0, new PixelRect(0, 0, 10, 0).Area);
    }

    [Fact]
    public void Area_does_not_overflow()
    {
        Assert.Equal(4_000_000_000L, new PixelRect(0, 0, 100_000, 40_000).Area);
    }

    [Fact]
    public void Contains_is_inclusive_left_top_exclusive_right_bottom()
    {
        var r = new PixelRect(0, 0, 10, 10);

        Assert.True(r.Contains(0, 0));
        Assert.True(r.Contains(9, 9));
        Assert.False(r.Contains(10, 5));
        Assert.False(r.Contains(5, 10));
        Assert.False(r.Contains(-1, 5));
    }

    [Fact]
    public void Intersect_overlapping_disjoint_and_touching()
    {
        var a = new PixelRect(0, 0, 100, 100);

        Assert.Equal(new PixelRect(50, 60, 100, 100), a.Intersect(new PixelRect(50, 60, 200, 200)));
        Assert.Equal(default, a.Intersect(new PixelRect(200, 200, 300, 300)));
        Assert.Equal(default, a.Intersect(new PixelRect(100, 0, 200, 100))); // edges touch only
        Assert.Equal(a, a.Intersect(a));
    }

    [Fact]
    public void Intersect_with_negative_virtual_screen_coordinates()
    {
        var left = new PixelRect(-1920, 0, 0, 1080);

        Assert.Equal(new PixelRect(-100, 100, 0, 200), left.Intersect(new PixelRect(-100, 100, 100, 200)));
    }

    [Fact]
    public void Inflate_grows_and_shrinks()
    {
        var r = new PixelRect(10, 10, 20, 20);

        Assert.Equal(new PixelRect(5, 8, 25, 22), r.Inflate(5, 2));
        Assert.True(r.Inflate(-5, -5).IsEmpty);
    }
}

/// <summary>Runs test bodies on a dedicated STA thread with a WPF dispatcher (HwndSource / DispatcherTimer need one).</summary>
internal static class Sta
{
    private static readonly Lazy<bool> SystemEventsOnOwnThread = new(() =>
    {
        // SystemEvents binds its hidden window to the first subscribing STA thread. Initialise it from an MTA
        // thread first so it gets its own long-lived thread instead of a short-lived test thread.
        Task.Run(() => SystemEvents.InvokeOnEventsThread(static () => { })).Wait();
        return true;
    });

    internal static void Run(Action body)
    {
        _ = SystemEventsOnOwnThread.Value;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
#pragma warning disable CA1031 // Re-thrown on the test thread.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA test body timed out.");
        failure?.Throw();
    }

    /// <summary>Pumps the current dispatcher for the given time.</summary>
    internal static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(duration, DispatcherPriority.Background, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
