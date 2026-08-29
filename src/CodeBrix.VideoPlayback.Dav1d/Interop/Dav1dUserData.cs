using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// A reference-counted user pointer - <c>Dav1dUserData</c> in <c>common.h</c>, 16 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Dav1dUserData
{
    /// <summary>The user pointer.</summary>
    public IntPtr Data;

    /// <summary>The allocation this pointer came from.</summary>
    public IntPtr Reference;
}
