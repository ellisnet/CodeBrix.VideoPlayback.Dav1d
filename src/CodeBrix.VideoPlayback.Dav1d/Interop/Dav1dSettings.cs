using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// The settings a decoder instance is opened with - <c>Dav1dSettings</c> in <c>dav1d.h</c>, 96 bytes.
/// </summary>
/// <remarks>
/// Always fill this in by calling <c>dav1d_default_settings</c> first and then changing individual fields.
/// The trailing <see cref="Reserved" /> bytes are dav1d's room for growth within API version 7 and must be
/// left exactly as <c>dav1d_default_settings</c> wrote them.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Dav1dSettings
{
    /// <summary>How many threads to decode with; 0 lets dav1d count the logical cores.</summary>
    public int ThreadCount;

    /// <summary>How many frames may be in flight; 1 is lowest latency, 0 lets dav1d choose.</summary>
    public int MaxFrameDelay;

    /// <summary>Non-zero to synthesise film grain on output frames.</summary>
    public int ApplyGrain;

    /// <summary>Which operating point of a scalable stream to decode, 0 to 31.</summary>
    public int OperatingPoint;

    /// <summary>Non-zero to output every spatial layer of a scalable stream.</summary>
    public int AllLayers;

    /// <summary>The largest frame, in pixels, that will be decoded; 0 means no limit.</summary>
    public uint FrameSizeLimit;

    /// <summary>The picture allocator decoded frames are written into.</summary>
    public Dav1dPicAllocator Allocator;

    /// <summary>The logging hook.</summary>
    public Dav1dLogger Logger;

    /// <summary>Non-zero to fail on standard-compliance violations that do not affect decoding.</summary>
    public int StrictStdCompliance;

    /// <summary>Non-zero to output invisibly coded frames as well as visible ones.</summary>
    public int OutputInvisibleFrames;

    /// <summary>Which in-loop filters to run.</summary>
    public Dav1dInloopFilterType InloopFilters;

    /// <summary>Which frame types to decode and return.</summary>
    public Dav1dDecodeFrameType DecodeFrameType;

    /// <summary>Reserved by dav1d for future use; leave exactly as the defaults left it.</summary>
    public fixed byte Reserved[16];
}
