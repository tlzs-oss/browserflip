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

            // NOTE (measured on macOS 27): -[NSWorkspace iconForFile:] returns an NSImage whose
            // representations are NSISIconImageRep (16-1024 px), not NSBitmapImageRep, so they
            // cannot encode themselves to PNG directly. Flattening through TIFFRepresentation is
            // the documented path that works for every rep type. It yields the largest resolution
            // (1-2 MB per browser); selecting a 64 px rep would need CGImageForProposedRect:
            // bridging, which is tracked as a follow-up rather than done in this prototype.
            // The view bounds decode size, so UI memory stays small regardless.
            var tiff = ObjC.Send(image, ObjC.Sel("TIFFRepresentation"));
            if (tiff == ObjC.Nil)
            {
                return null;
            }

            var rep = ObjC.Send(ObjC.GetClass("NSBitmapImageRep"), ObjC.Sel("imageRepWithData:"), tiff);
            if (rep == ObjC.Nil)
            {
                return null;
            }

            var empty = ObjC.Send(ObjC.GetClass("NSDictionary"), ObjC.Sel("dictionary"));
            var png = ObjC.SendWithLongAndPtr(rep, ObjC.Sel("representationUsingType:properties:"), PngFileType, empty);
            return ToBytes(png);
        }
#pragma warning disable CA1031 // A missing icon must never break the list.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
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
