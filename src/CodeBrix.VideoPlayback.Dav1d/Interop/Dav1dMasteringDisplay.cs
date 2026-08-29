using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// Mastering display colour volume metadata - <c>Dav1dMasteringDisplay</c> in <c>headers.h</c>, 24 bytes.
/// </summary>
/// <remarks>
/// The primaries and white point are 0.16 fixed point, the maximum luminance 24.8 fixed point and the
/// minimum luminance 18.14 fixed point.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Dav1dMasteringDisplay
{
    /// <summary>Red, green and blue chromaticity, x then y, as 0.16 fixed point.</summary>
    public fixed ushort Primaries[6];

    /// <summary>The white point, x then y, as 0.16 fixed point.</summary>
    public fixed ushort WhitePoint[2];

    /// <summary>The maximum luminance as 24.8 fixed point.</summary>
    public uint MaxLuminance;

    /// <summary>The minimum luminance as 18.14 fixed point.</summary>
    public uint MinLuminance;
}
