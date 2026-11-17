using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BrowserFlip.App.Services;

/// <summary>
/// One BrowserFlip process per settings file and Windows session: a second start signals the running
/// instance to show itself and exits, so there are never two notification-area icons.
/// Instances started with a different <c>--settings</c> file (tests) are independent.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSignal;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle showSignal)
    {
        _mutex = mutex;
        _showSignal = showSignal;
    }

    /// <summary>Raised on a thread-pool thread when another start asked this instance to show itself.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>
    /// Becomes the instance for <paramref name="settingsPath"/>, or returns <c>null</c> after signalling
    /// the instance that already runs.
    /// </summary>
    public static SingleInstance? TryAcquire(string settingsPath)
    {
        var name = NameFor(settingsPath);
        Mutex mutex;
        EventWaitHandle signal;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out createdNew);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return null; // owned by an instance at another integrity level (e.g. run as administrator): it runs
        }

        try
        {
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Show");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            if (createdNew)
            {
                mutex.ReleaseMutex();
            }

            mutex.Dispose();
            return null;
        }

        if (!createdNew)
        {
            // Let the running instance take the foreground (otherwise Windows only flashes its taskbar button).
            _ = Desktop.Win32.NativeMethods.AllowSetForegroundWindow(Desktop.Win32.NativeMethods.AsfwAny);
            _ = signal.Set();
            signal.Dispose();
            mutex.Dispose();
            return null;
        }

        var instance = new SingleInstance(mutex, signal);
        instance._registration = ThreadPool.RegisterWaitForSingleObject(
            signal, (_, _) => instance.ShowRequested?.Invoke(instance, EventArgs.Empty), null, Timeout.Infinite, executeOnlyOnce: false);
        return instance;
    }

    /// <summary>Object name derived from the settings file path (case-insensitive).</summary>
    internal static string NameFor(string settingsPath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(settingsPath).ToUpperInvariant()));
        return $"BrowserFlip.{Convert.ToHexString(bytes, 0, 12)}";
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread (released already): nothing to do.
        }

        _mutex.Dispose();
        _showSignal.Dispose();
    }
}
