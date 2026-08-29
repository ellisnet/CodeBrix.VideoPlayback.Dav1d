using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// One reference to a block of bitstream data - <c>Dav1dData</c> in <c>data.h</c>, 72 bytes.
/// </summary>
/// <remarks>
/// A zeroed value is "no reference". <c>dav1d_send_data</c> zeroes the caller's copy when it takes the
/// data, and leaves it untouched when it answers "try again", which is what makes the back-pressure loop
/// safe to write as "offer the same value again".
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Dav1dData
{
    /// <summary>The first byte of the data.</summary>
    public byte* Data;

    /// <summary>How many bytes there are.</summary>
    public UIntPtr Size;

    /// <summary>The allocation this reference came from.</summary>
    public IntPtr Reference;

    /// <summary>The metadata that travels with the packet.</summary>
    public Dav1dDataProps Properties;
}
