using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// The parts of dav1d's <c>Dav1dFrameHeader</c> this binding reads, at their exact native offsets.
/// </summary>
/// <remarks>
/// The native structure is 1152 bytes, almost all of it per-frame coding state - tiling, quantisation,
/// segmentation, loop filter, global motion - that a player has no use for. Only the frame type, the coded
/// and render sizes and the layer identifiers are declared, at explicit offsets taken from the vendored
/// headers and restated on <see cref="Dav1dNativeLayout" /> for the test suite to check.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Dav1dNativeLayout.FrameHeaderSize)]
internal struct Dav1dFrameHeader
{
    /// <summary>The frame type.</summary>
    [FieldOffset(232)] public Dav1dFrameType FrameType;

    /// <summary>The coded width, before super-resolution upscaling.</summary>
    [FieldOffset(236)] public int CodedWidth;

    /// <summary>The width after super-resolution upscaling - the width of the samples in the picture.</summary>
    [FieldOffset(240)] public int UpscaledWidth;

    /// <summary>The frame height.</summary>
    [FieldOffset(244)] public int Height;

    /// <summary>The frame's own number.</summary>
    [FieldOffset(248)] public byte FrameOffset;

    /// <summary>The temporal layer this frame belongs to.</summary>
    [FieldOffset(249)] public byte TemporalId;

    /// <summary>The spatial layer this frame belongs to.</summary>
    [FieldOffset(250)] public byte SpatialId;

    /// <summary>Non-zero when this frame header only re-shows an already decoded frame.</summary>
    [FieldOffset(251)] public byte ShowExistingFrame;

    /// <summary>Non-zero when the frame is meant to be shown rather than only referred to.</summary>
    [FieldOffset(264)] public byte ShowFrame;

    /// <summary>The width the frame should be displayed at once the pixel aspect ratio is applied.</summary>
    [FieldOffset(408)] public int RenderWidth;

    /// <summary>The height the frame should be displayed at once the pixel aspect ratio is applied.</summary>
    [FieldOffset(412)] public int RenderHeight;

    /// <summary>Non-zero when the stream stated a render size of its own.</summary>
    [FieldOffset(418)] public byte HaveRenderSize;

    /// <summary>True when decoding may start at this frame.</summary>
    public readonly bool IsKeyFrame => FrameType == Dav1dFrameType.Key;
}
