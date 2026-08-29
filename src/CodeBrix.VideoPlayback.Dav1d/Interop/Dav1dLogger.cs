using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// dav1d's logging hook - <c>Dav1dLogger</c> in <c>dav1d.h</c>, 16 bytes.
/// </summary>
/// <remarks>
/// The callback is a C <c>vprintf</c>-style pair: a format string and a <c>va_list</c>. Expanding a
/// <c>va_list</c> from managed code is not portable - its representation differs between x86-64, AArch64 and
/// Windows - so this binding passes the format string on unexpanded and never touches the argument list.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Dav1dLogger
{
    /// <summary>The pointer passed back to the callback.</summary>
    public IntPtr Cookie;

    /// <summary>The logging callback, or null to disable dav1d's logging entirely.</summary>
    public delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr, void> Callback;
}
