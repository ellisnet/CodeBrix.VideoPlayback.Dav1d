using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// The metadata dav1d carries from an input packet through to the picture decoded from it -
/// <c>Dav1dDataProps</c> in <c>common.h</c>, 48 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Dav1dDataProps
{
    /// <summary>The container timestamp of the input packet, or <see cref="long.MinValue" /> when unknown.</summary>
    public long Timestamp;

    /// <summary>The container duration of the input packet, or zero when unknown.</summary>
    public long Duration;

    /// <summary>The stream offset of the input packet, or -1 when unknown.</summary>
    public long Offset;

    /// <summary>The packet size.</summary>
    public UIntPtr Size;

    /// <summary>The caller's own reference-counted pointer.</summary>
    public Dav1dUserData UserData;
}
