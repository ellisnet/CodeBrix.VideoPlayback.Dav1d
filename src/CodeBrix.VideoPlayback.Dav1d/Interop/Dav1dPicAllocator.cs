using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// The picture allocator dav1d decodes into - <c>Dav1dPicAllocator</c> in <c>picture.h</c>, 24 bytes.
/// </summary>
/// <remarks>
/// Both callbacks are unmanaged function pointers rather than delegates, so nothing has to be kept alive
/// against collection and the binding stays ahead-of-time friendly. The release callback runs on dav1d's
/// frame threads as well as on the thread that calls <c>dav1d_get_picture</c>, so it must be thread-safe.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Dav1dPicAllocator
{
    /// <summary>The pointer passed back to both callbacks.</summary>
    public IntPtr Cookie;

    /// <summary>
    /// Fills in a picture's plane pointers, strides and allocator data. Returns 0, or a negative dav1d
    /// error code.
    /// </summary>
    public delegate* unmanaged[Cdecl]<Dav1dPicture*, IntPtr, int> AllocPictureCallback;

    /// <summary>Releases a picture's buffer. May run on any dav1d frame thread.</summary>
    public delegate* unmanaged[Cdecl]<Dav1dPicture*, IntPtr, void> ReleasePictureCallback;
}
