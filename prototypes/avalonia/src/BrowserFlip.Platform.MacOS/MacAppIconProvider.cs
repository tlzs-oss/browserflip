using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BrowserFlip.Platform.MacOS.Interop;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Platform.MacOS;

/// <summary>
/// Renders an application's icon to PNG with <c>-[NSWorkspace iconForFile:]</c> and
/// <c>NSBitmapImageRep</c> — both public AppKit API.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacAppIconProvider : IAppIconProvider
{
    /// <summary>NSBitmapImageFileTypePNG.</summary>
    private const long PngFileType = 4;

    public byte[]? GetIconPng(string? applicationPath, int pixelSize = 64)
    {
        if (string.IsNullOrWhiteSpace(applicationPath) || !MacLaunchServices.IsAvailable)
        {
            return null;
        }

        try
        {
            var workspace = ObjC.SharedWorkspace();
            var image = ObjC.Send(workspace, ObjC.Sel("iconForFile:"), ObjC.NSString(applicationPath));
            if (image == ObjC.Nil)
            {
                return null;
            }

            // -[NSWorkspace iconForFile:] returns a multi-resolution NSImage whose representations
            // are NSISIconImageRep (16-1024 px), which cannot encode themselves to PNG. Asking for
            // a CGImage at the size we actually want makes AppKit pick the right representation
            // and hands back something NSBitmapImageRep can encode - so the result is a ~64 px
            // icon of a few kilobytes instead of a 1-2 MB flattening of the 1024 px master.
            var rep = CreateBitmapRep(image, pixelSize);
            if (rep == ObjC.Nil)
            {
                return null;
            }

            try
            {
                var empty = ObjC.Send(ObjC.GetClass("NSDictionary"), ObjC.Sel("dictionary"));
                var png = ObjC.SendWithLongAndPtr(rep, ObjC.Sel("representationUsingType:properties:"), PngFileType, empty);
                return ToBytes(png);
            }
            finally
            {
                // alloc/init transferred ownership to us, so it has to be released.
                ObjC.Send(rep, ObjC.Sel("release"));
            }
        }
#pragma warning disable CA1031 // A missing icon must never break the list.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>
    /// Builds an owned <c>NSBitmapImageRep</c> of about <paramref name="pixelSize"/> square from
    /// <paramref name="image"/>. The caller must <c>release</c> the result.
    /// Falls back to flattening the whole image when the CGImage route is unavailable.
    /// </summary>
    private static IntPtr CreateBitmapRep(IntPtr image, int pixelSize)
    {
        var rect = new ObjC.NSRect { X = 0, Y = 0, Width = pixelSize, Height = pixelSize };
        var selector = ObjC.Sel("CGImageForProposedRect:context:hints:");
        if (ObjC.RespondsTo(image, selector))
        {
            var cgImage = ObjC.SendWithRectPtr(image, selector, ref rect, ObjC.Nil, ObjC.Nil);
            if (cgImage != ObjC.Nil)
            {
                var allocated = ObjC.Send(ObjC.GetClass("NSBitmapImageRep"), ObjC.Sel("alloc"));
                var rep = ObjC.Send(allocated, ObjC.Sel("initWithCGImage:"), cgImage);
                if (rep != ObjC.Nil)
                {
                    return rep;
                }
            }
        }

        // Fallback: the documented path that works for any representation type, at the cost of
        // encoding the largest resolution available.
        var tiff = ObjC.Send(image, ObjC.Sel("TIFFRepresentation"));
        if (tiff == ObjC.Nil)
        {
            return ObjC.Nil;
        }

        var autoreleased = ObjC.Send(ObjC.GetClass("NSBitmapImageRep"), ObjC.Sel("imageRepWithData:"), tiff);
        // Retain so the caller can release it uniformly regardless of which path produced it.
        return autoreleased == ObjC.Nil ? ObjC.Nil : ObjC.Send(autoreleased, ObjC.Sel("retain"));
    }

    /// <summary>Copies an NSData into a managed array.</summary>
    private static byte[]? ToBytes(IntPtr nsData)
    {
        if (nsData == ObjC.Nil)
        {
            return null;
        }

        var length = ObjC.SendLong(nsData, ObjC.Sel("length"));
        if (length <= 0 || length > 16 * 1024 * 1024)
        {
            return null;
        }

        var pointer = ObjC.Send(nsData, ObjC.Sel("bytes"));
        if (pointer == ObjC.Nil)
        {
            return null;
        }

        var buffer = new byte[length];
        Marshal.Copy(pointer, buffer, 0, (int)length);
        return buffer;
    }
}
