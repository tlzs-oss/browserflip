using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BrowserFlip.Platform.MacOS.Interop;

/// <summary>
/// Minimal Objective-C runtime interop, in the same <c>DllImport</c> style the Windows
/// implementation uses (and with <c>AllowUnsafeBlocks=false</c>, like the rest of BrowserFlip).
/// Only documented, public AppKit / Foundation APIs are reached through it: BrowserFlip uses
/// no Apple private API and no undocumented selector.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ObjC
{
    private const string Libobjc = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const string AppKitPath = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const int RtldLazy = 1;

    internal static readonly IntPtr Nil = IntPtr.Zero;

    // The native entry points take raw UTF-8 pointers rather than marshalled strings: it removes
    // every ambiguity about the encoding (and satisfies CA2101 honestly rather than by suppression).
    [DllImport(Libobjc, EntryPoint = "objc_getClass", ExactSpelling = true)]
    private static extern IntPtr objc_getClass(IntPtr utf8Name);

    [DllImport(Libobjc, EntryPoint = "sel_registerName", ExactSpelling = true)]
    private static extern IntPtr sel_registerName(IntPtr utf8Name);

    [DllImport(LibSystem, EntryPoint = "dlopen", ExactSpelling = true)]
    private static extern IntPtr dlopen(IntPtr utf8Path, int mode);

    // Classes and selectors are process-wide and never change, so they are resolved once.
    private static readonly Dictionary<string, IntPtr> ClassCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IntPtr> SelectorCache = new(StringComparer.Ordinal);
    private static readonly object CacheLock = new();

    /// <summary>Resolves an Objective-C class by name (cached). <see cref="Nil"/> when unknown.</summary>
    internal static IntPtr GetClass(string name)
    {
        lock (CacheLock)
        {
            if (ClassCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var handle = WithUtf8(name, objc_getClass);
            ClassCache[name] = handle;
            return handle;
        }
    }

    /// <summary>Registers / resolves a selector by name (cached).</summary>
    internal static IntPtr Sel(string name)
    {
        lock (CacheLock)
        {
            if (SelectorCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var handle = WithUtf8(name, sel_registerName);
            SelectorCache[name] = handle;
            return handle;
        }
    }

    /// <summary>Calls <paramref name="native"/> with <paramref name="value"/> as a UTF-8 buffer.</summary>
    private static IntPtr WithUtf8(string value, Func<IntPtr, IntPtr> native)
    {
        var buffer = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return native(buffer);
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUTF8(buffer);
        }
    }

    // objc_msgSend is declared once per argument shape. Every shape used here returns a pointer,
    // an integer or nothing, so the plain objc_msgSend entry point is correct on arm64 and x64.
    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    internal static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    internal static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    internal static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    internal static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    internal static extern long SendLong(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// For selectors returning <c>BOOL</c>. Objective-C only defines the low byte of the return
    /// register for BOOL, so these must NOT go through the IntPtr-returning overload.
    /// </summary>
    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr a);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    internal static extern IntPtr SendWithLong(IntPtr receiver, IntPtr selector, long a);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    internal static extern IntPtr SendWithLongAndPtr(IntPtr receiver, IntPtr selector, long a, IntPtr b);

    /// <summary>Loads AppKit so NSWorkspace is available in processes without a UI (tests, CLI).</summary>
    internal static bool EnsureAppKit()
    {
        if (GetClass("NSWorkspace") != Nil)
        {
            return true;
        }

        var path = Marshal.StringToCoTaskMemUTF8(AppKitPath);
        try
        {
            dlopen(path, RtldLazy);
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUTF8(path);
        }

        // The cached miss from before the framework was loaded must not be kept.
        lock (CacheLock)
        {
            ClassCache.Clear();
        }

        return GetClass("NSWorkspace") != Nil;
    }

    /// <summary>Creates an autoreleased NSString, or <see cref="Nil"/> for <c>null</c>.</summary>
    internal static IntPtr NSString(string? value)
    {
        if (value is null)
        {
            return Nil;
        }

        var buffer = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(GetClass("NSString"), Sel("stringWithUTF8String:"), buffer);
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUTF8(buffer);
        }
    }

    /// <summary>Reads an NSString as a managed string, or <c>null</c> when the object is nil.</summary>
    internal static string? ToManaged(IntPtr nsString)
    {
        if (nsString == Nil)
        {
            return null;
        }

        var utf8 = Send(nsString, Sel("UTF8String"));
        return utf8 == Nil ? null : Marshal.PtrToStringUTF8(utf8);
    }

    /// <summary>NSURL from a file-system path.</summary>
    internal static IntPtr FileUrl(string path)
        => Send(GetClass("NSURL"), Sel("fileURLWithPath:"), NSString(path));

    /// <summary>NSURL from an absolute URL string.</summary>
    internal static IntPtr Url(string url)
        => Send(GetClass("NSURL"), Sel("URLWithString:"), NSString(url));

    /// <summary>The <c>path</c> property of an NSURL.</summary>
    internal static string? UrlPath(IntPtr nsUrl)
        => nsUrl == Nil ? null : ToManaged(Send(nsUrl, Sel("path")));

    /// <summary>Copies an NSArray of object pointers into a list.</summary>
    internal static List<IntPtr> ArrayItems(IntPtr nsArray)
    {
        var result = new List<IntPtr>();
        if (nsArray == Nil)
        {
            return result;
        }

        var count = SendLong(nsArray, Sel("count"));
        for (long i = 0; i < count; i++)
        {
            result.Add(SendWithLong(nsArray, Sel("objectAtIndex:"), i));
        }

        return result;
    }

    /// <summary>
    /// A Core Graphics / AppKit rectangle (<c>NSRect</c>). Four CGFloats, which are 64-bit on
    /// every platform BrowserFlip targets.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NSRect
    {
        internal double X;
        internal double Y;
        internal double Width;
        internal double Height;
    }

    /// <summary>For selectors taking a struct POINTER, such as CGImageForProposedRect:context:hints:.</summary>
    [DllImport(Libobjc, EntryPoint = "objc_msgSend", ExactSpelling = true)]
    internal static extern IntPtr SendWithRectPtr(IntPtr receiver, IntPtr selector, ref NSRect rect, IntPtr b, IntPtr c);

    /// <summary>Whether <paramref name="receiver"/> implements <paramref name="selector"/>.</summary>
    internal static bool RespondsTo(IntPtr receiver, IntPtr selector)
        => receiver != Nil && SendBool(receiver, Sel("respondsToSelector:"), selector);

    /// <summary>Whether <paramref name="obj"/> is an instance of <paramref name="cls"/> or a subclass.</summary>
    internal static bool IsKindOf(IntPtr obj, IntPtr cls)
        => obj != Nil && cls != Nil && SendBool(obj, Sel("isKindOfClass:"), cls);

    /// <summary>[NSWorkspace sharedWorkspace]</summary>
    internal static IntPtr SharedWorkspace() => Send(GetClass("NSWorkspace"), Sel("sharedWorkspace"));

    /// <summary>
    /// Reads an NSError as "domain code: localizedDescription", or <c>null</c> when the error
    /// is nil. The domain and code are included because the localised description alone is not
    /// enough to tell a refusal from a malformed request when something goes wrong on a user's
    /// machine, and it arrives in whatever language that machine runs in.
    /// </summary>
    internal static string? ErrorDescription(IntPtr nsError)
    {
        if (nsError == Nil)
        {
            return null;
        }

        var message = ToManaged(Send(nsError, Sel("localizedDescription")));
        var domain = ToManaged(Send(nsError, Sel("domain")));
        var code = SendLong(nsError, Sel("code"));
        return domain is null
            ? message
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{domain} {code}: {message}");
    }
}
