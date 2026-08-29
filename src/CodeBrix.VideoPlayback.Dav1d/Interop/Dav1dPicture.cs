using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// One reference to a decoded picture - <c>Dav1dPicture</c> in <c>picture.h</c>, 272 bytes.
/// </summary>
/// <remarks>
/// <para>
/// The three <c>void *data[3]</c> pointers and the two <c>ptrdiff_t stride[2]</c> values are spelled out as
/// separate fields here rather than as fixed buffers, because a fixed buffer cannot hold a pointer-sized
/// element in C#. The offsets are identical either way and
/// <see cref="CodeBrix.VideoPlayback.Dav1d.Interop.Dav1dNativeLayout" /> states them, so the test suite can
/// prove it.
/// </para>
/// <para>
/// The picture allocator fills in only <see cref="Data0" />, <see cref="Data1" />, <see cref="Data2" />,
/// <see cref="Stride0" />, <see cref="Stride1" /> and <see cref="AllocatorData" />; dav1d owns every other
/// field.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Dav1dPicture
{
    /// <summary>The sequence header this picture was decoded under.</summary>
    public Dav1dSequenceHeader* SequenceHeader;

    /// <summary>The frame header this picture was decoded under.</summary>
    public Dav1dFrameHeader* FrameHeader;

    /// <summary>The luma plane.</summary>
    public IntPtr Data0;

    /// <summary>The first chroma plane (Cb).</summary>
    public IntPtr Data1;

    /// <summary>The second chroma plane (Cr).</summary>
    public IntPtr Data2;

    /// <summary>The distance in bytes between luma rows.</summary>
    public nint Stride0;

    /// <summary>The distance in bytes between chroma rows - shared by both chroma planes.</summary>
    public nint Stride1;

    /// <summary>The picture's shape.</summary>
    public Dav1dPictureParameters Parameters;

    /// <summary>The metadata carried over from the input packet.</summary>
    public Dav1dDataProps Properties;

    /// <summary>Content light level metadata, or null.</summary>
    public Dav1dContentLightLevel* ContentLight;

    /// <summary>Mastering display metadata, or null.</summary>
    public Dav1dMasteringDisplay* MasteringDisplay;

    /// <summary>ITU-T T.35 metadata, or null.</summary>
    public IntPtr ItutT35;

    /// <summary>How many ITU-T T.35 entries there are.</summary>
    public UIntPtr ItutT35Count;

    /// <summary>Reserved by dav1d for future use.</summary>
    public IntPtr Reserved0;

    /// <summary>Reserved by dav1d for future use.</summary>
    public IntPtr Reserved1;

    /// <summary>Reserved by dav1d for future use.</summary>
    public IntPtr Reserved2;

    /// <summary>Reserved by dav1d for future use.</summary>
    public IntPtr Reserved3;

    /// <summary>The allocation the frame header came from.</summary>
    public IntPtr FrameHeaderReference;

    /// <summary>The allocation the sequence header came from.</summary>
    public IntPtr SequenceHeaderReference;

    /// <summary>The allocation the content light level metadata came from.</summary>
    public IntPtr ContentLightReference;

    /// <summary>The allocation the mastering display metadata came from.</summary>
    public IntPtr MasteringDisplayReference;

    /// <summary>The allocation the ITU-T T.35 metadata came from.</summary>
    public IntPtr ItutT35Reference;

    /// <summary>Reserved by dav1d for future use.</summary>
    public IntPtr ReservedReference0;

    /// <summary>Reserved by dav1d for future use.</summary>
    public IntPtr ReservedReference1;

    /// <summary>Reserved by dav1d for future use.</summary>
    public IntPtr ReservedReference2;

    /// <summary>Reserved by dav1d for future use.</summary>
    public IntPtr ReservedReference3;

    /// <summary>The allocation the frame data came from - what <c>dav1d_picture_unref</c> releases.</summary>
    public IntPtr Reference;

    /// <summary>The pointer the picture allocator chose to associate with this buffer.</summary>
    public IntPtr AllocatorData;
}
