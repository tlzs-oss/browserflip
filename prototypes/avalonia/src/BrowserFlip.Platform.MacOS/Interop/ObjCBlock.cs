using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BrowserFlip.Platform.MacOS.Interop;

/// <summary>
/// Builds the one kind of Objective-C block BrowserFlip needs: <c>void (^)(NSError *)</c>, the
/// completion handler of
/// <c>-[NSWorkspace setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:]</c>.
/// <para>
/// Apple documents that method as: "If a change requires user consent, the system asks the for
/// consent asynchronously before invoking the completion handler." The handler is therefore the
/// only public, documented signal that distinguishes "the user answered and refused" from "the
/// user has not answered yet". Passing <c>nil</c> - as this adapter used to - throws that
/// distinction away and leaves polling as the only evidence, which cannot tell a decline from a
/// dialog that is still open.
/// </para>
/// <para>
/// The block ABI is public and stable (the <c>Block_private.h</c> layout shipped in the open
/// source compiler-rt runtime), and nothing here touches a private Apple API: it only
/// constructs a value the public method already accepts.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ObjCBlock
{
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    /// <summary>dlsym's "search every loaded image" pseudo-handle.</summary>
    private static readonly IntPtr RtldDefault = new(-2);

    /// <summary>BLOCK_IS_GLOBAL. A global block is never copied or freed by the runtime.</summary>
    private const int BlockIsGlobal = 1 << 28;

    /// <summary>BLOCK_HAS_SIGNATURE: the descriptor carries the encoded signature below.</summary>
    private const int BlockHasSignature = 1 << 30;

    /// <summary>Objective-C type encoding of <c>void (^)(NSError *)</c>.</summary>
    private const string VoidTakingObject = "v16@?0@8";

    private const int BlockLiteralSize = 32;
    private const int DescriptorSize = 24;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ErrorHandler(IntPtr block, IntPtr error);

    [DllImport(LibSystem, EntryPoint = "dlsym", ExactSpelling = true)]
    private static extern IntPtr dlsym(IntPtr handle, IntPtr utf8Symbol);

    /// <summary>
    /// Everything a live block points at. macOS may invoke the handler minutes after the
    /// request - the consent alert has been measured open for 7.5 minutes - and it may invoke
    /// it after the awaiting code has given up, so none of this is ever freed. One switch
    /// attempt leaks well under a hundred bytes, which is the correct trade against a
    /// use-after-free in AppKit's hands.
    /// </summary>
    private static readonly List<object> KeptAlive = [];

    private static readonly object Gate = new();

    /// <summary>
    /// Creates a completion-handler block that calls <paramref name="onCompleted"/> with the
    /// NSError's <c>localizedDescription</c>, or <c>null</c> when the OS reported success.
    /// Returns <see cref="ObjC.Nil"/> when the runtime symbol cannot be resolved; the caller
    /// then passes nil, exactly as before, and falls back to read-back polling alone.
    /// </summary>
    internal static IntPtr CreateErrorHandler(Action<string?> onCompleted)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);

        var isa = Symbol("_NSConcreteGlobalBlock");
        if (isa == IntPtr.Zero)
        {
            return ObjC.Nil;
        }

        // An exception crossing back into AppKit would terminate the process, so the callback
        // can never throw. There is nowhere to report to from here, which is why the result is
        // also delivered through onCompleted rather than by raising.
        ErrorHandler handler = (_, error) =>
        {
#pragma warning disable CA1031 // A managed exception must not unwind into the Objective-C caller.
            try
            {
                onCompleted(ObjC.ErrorDescription(error));
            }
            catch (Exception)
            {
                // Deliberately swallowed: see above.
            }
#pragma warning restore CA1031
        };

        var signature = Marshal.StringToHGlobalAnsi(VoidTakingObject);

        var descriptor = Marshal.AllocHGlobal(DescriptorSize);
        Marshal.WriteInt64(descriptor, 0, 0);                  // reserved
        Marshal.WriteInt64(descriptor, 8, BlockLiteralSize);   // size of the literal below
        Marshal.WriteIntPtr(descriptor, 16, signature);

        var block = Marshal.AllocHGlobal(BlockLiteralSize);
        Marshal.WriteIntPtr(block, 0, isa);
        Marshal.WriteInt32(block, 8, BlockIsGlobal | BlockHasSignature);
        Marshal.WriteInt32(block, 12, 0);                      // reserved
        Marshal.WriteIntPtr(block, 16, Marshal.GetFunctionPointerForDelegate(handler));
        Marshal.WriteIntPtr(block, 24, descriptor);

        lock (Gate)
        {
            // The delegate must outlive the block: the function pointer above does not root it.
            KeptAlive.Add(handler);
        }

        return block;
    }

    private static IntPtr Symbol(string name)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            return dlsym(RtldDefault, utf8);
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUTF8(utf8);
        }
    }
}
