using System.Diagnostics;
using System.Runtime.InteropServices;
namespace BrowserFlip.Core.Platform;

internal static class NativeMethods
{
    internal const int AssocfIsProtocol = 0x00001000;
    internal const int AssocstrExecutable = 2;
    internal const int AssocstrFriendlyAppName = 4;
    internal const int AssocstrProgId = 20;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int AssocQueryStringW(int flags, int str, string pszAssoc, string? pszExtra, [Out] char[]? pszOut, ref uint pcchOut);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SHLoadIndirectString(string pszSource, [Out] char[] pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);

    /// <summary>Converts a NUL-terminated buffer to a string.</summary>
    internal static string FromBuffer(char[] buffer)
    {
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }
}

/// <summary>
/// <see cref="IAssociationQuery"/> using <c>AssocQueryStringW(ASSOCF_IS_PROTOCOL, ASSOCSTR_PROGID)</c>.
/// This is the documented, read-only way to ask Windows which handler is effective; it works across
/// the legacy <c>UserChoice</c> and newer <c>UserChoiceLatest</c> storage formats.
/// </summary>
public sealed class ShellAssociationQuery : IAssociationQuery
{
    public AssociationInfo? QueryProtocol(string protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        var progId = Query(protocol, NativeMethods.AssocstrProgId);
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        return new AssociationInfo(
            progId,
            Query(protocol, NativeMethods.AssocstrFriendlyAppName),
            Query(protocol, NativeMethods.AssocstrExecutable));
    }

    private static string? Query(string protocol, int what)
    {
        uint size = 0;
        // First call obtains the required buffer size (S_FALSE = 1).
        var hr = NativeMethods.AssocQueryStringW(NativeMethods.AssocfIsProtocol, what, protocol, null, null, ref size);
        if ((hr != 0 && hr != 1) || size == 0 || size > 32_768)
        {
            return null;
        }

        var buffer = new char[size];
        hr = NativeMethods.AssocQueryStringW(NativeMethods.AssocfIsProtocol, what, protocol, null, buffer, ref size);
        return hr == 0 ? NativeMethods.FromBuffer(buffer) : null;
    }
}

public sealed class ShellIndirectStringResolver : IIndirectStringResolver
{
    public string? Resolve(string indirect)
    {
        if (string.IsNullOrEmpty(indirect) || indirect[0] != '@')
        {
            return indirect;
        }

        var buffer = new char[1024];
        return NativeMethods.SHLoadIndirectString(indirect, buffer, (uint)buffer.Length, IntPtr.Zero) == 0 ? NativeMethods.FromBuffer(buffer) : null;
    }
}

/// <summary>Launches URIs with <c>ShellExecute</c>. Only <c>ms-settings:</c> URIs are accepted.</summary>
public sealed class ShellUriLauncher : IUriLauncher
{
    public void Launch(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!string.Equals(uri.Scheme, "ms-settings", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only ms-settings: URIs may be launched.", nameof(uri));
        }

        using var p = Process.Start(new ProcessStartInfo(uri.OriginalString) { UseShellExecute = true });
    }
}
