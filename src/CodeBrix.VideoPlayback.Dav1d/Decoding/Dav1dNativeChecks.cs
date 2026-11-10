using System;
using CodeBrix.VideoPlayback.Dav1d.Interop;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;

namespace CodeBrix.VideoPlayback.Dav1d.Decoding;

/// <summary>
/// The checks that stand between a value the native library reports and anything the binding builds from
/// it: a size, a stride, a plane pointer or a bit depth is compared with what this binding itself allocated
/// or with the limits of the AV1 format before it is used.
/// </summary>
/// <remarks>
/// <para>
/// dav1d is trusted to decode correctly; it is NOT trusted to describe memory. A frame handed to an
/// application carries plane pointers and dimensions that a presenter or a colour converter will read
/// straight through, so a native picture that disagreed with the buffer behind it - through a bug in the
/// shim, a structure layout that drifted from the vendored headers, or a corrupted picture - would turn
/// into reads and writes outside managed memory. Every such disagreement is refused here instead, with an
/// exception that says which value was wrong, before a frame exists.
/// </para>
/// <para>
/// The same reasoning applies in the other direction: a buffer the pool hands back is checked against the
/// descriptor it was rented for before dav1d is told to write into it, because dav1d writes as many rows of
/// as many bytes as the descriptor implies, whatever the buffer really holds.
/// </para>
/// <para>
/// None of these checks can fire for a correct native library and a correct pool. They are fenced by tests
/// that hand them deliberately wrong values.
/// </para>
/// </remarks>
internal static unsafe class Dav1dNativeChecks
{
    /// <summary>
    /// The largest width or height an AV1 frame can have: the format codes each as a 16-bit "minus one"
    /// value, so 65536 samples.
    /// </summary>
    public const int MaximumDimension = 65536;

    /// <summary>
    /// Decides whether a picture dav1d asks its allocator for is one the binding is prepared to rent memory
    /// for.
    /// </summary>
    /// <param name="parameters">The shape dav1d asked for.</param>
    /// <param name="frameSizeLimit">The configured frame-size limit, in luma samples.</param>
    /// <returns>Null when the request is acceptable; otherwise a sentence saying what was wrong with it.</returns>
    /// <remarks>
    /// dav1d applies the frame-size limit itself before it allocates. Applying it here as well means a
    /// native library that failed to - or a picture structure read at the wrong offsets - still cannot make
    /// the pool allocate a buffer of whatever size the native side names.
    /// </remarks>
    public static string CheckAllocationRequest(in Dav1dPictureParameters parameters, long frameSizeLimit)
    {
        int width = parameters.Width;
        int height = parameters.Height;
        int bitDepth = parameters.BitsPerComponent;

        if (Dav1dFrameAllocator.MapLayout(parameters.Layout) == VideoPixelLayout.Unknown)
        {
            return $"dav1d asked for a picture with plane layout {(int)parameters.Layout}, which is not one "
                + "of the four AV1 layouts.";
        }

        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension)
        {
            return $"dav1d asked for a {width} by {height} picture; an AV1 frame is 1 to {MaximumDimension} "
                + "samples on each side.";
        }

        if (bitDepth != 8 && bitDepth != 10 && bitDepth != 12)
        {
            return $"dav1d asked for a picture of {bitDepth} bits per component; AV1 has 8, 10 and 12.";
        }

        long samples = (long)width * height;
        if (frameSizeLimit > 0 && samples > frameSizeLimit)
        {
            return $"dav1d asked for a {width} by {height} picture ({samples} luma samples), which is over "
                + $"the configured limit of {frameSizeLimit} "
                + $"({nameof(VideoDecoderOptions)}.{nameof(VideoDecoderOptions.FrameSizeLimit)}).";
        }

        return null;
    }

    /// <summary>
    /// Decides whether a buffer the pool returned really is the buffer that was asked for, before dav1d is
    /// told to decode into it.
    /// </summary>
    /// <param name="buffer">The rented buffer.</param>
    /// <param name="requested">The descriptor it was rented for.</param>
    /// <returns>Null when the buffer is safe to hand to dav1d; otherwise a sentence saying why it is not.</returns>
    /// <remarks>
    /// dav1d writes <see cref="VideoFrameBufferDescriptor.LumaAllocationRows" /> rows at the luma stride and
    /// the chroma rows at ONE shared chroma stride, and relies on 64-byte-aligned planes. A buffer that is
    /// smaller, misaligned, or whose two chroma planes have different strides would be overrun, so each of
    /// those is refused. A stride LARGER than the descriptor's is accepted, because the pool contract lets a
    /// pool pad its rows - a pool that reports a wider stride owns an allocation of that stride. What is
    /// refused is a stride too narrow for the rows dav1d will write.
    /// </remarks>
    public static string CheckRentedBuffer(VideoFrameBuffer buffer, in VideoFrameBufferDescriptor requested)
    {
        if (buffer == null) return "the frame-buffer pool returned no buffer.";

        if (buffer.Descriptor != requested)
        {
            return $"the frame-buffer pool was asked for {requested} and returned a buffer for "
                + $"{buffer.Descriptor}.";
        }

        string luma = CheckPlane(buffer.Y, requested.LumaStride, "luma");
        if (luma != null) return luma;

        if (requested.IsMonochrome) return null;

        string first = CheckPlane(buffer.U, requested.ChromaStride, "first chroma");
        if (first != null) return first;

        string second = CheckPlane(buffer.V, requested.ChromaStride, "second chroma");
        if (second != null) return second;

        if (buffer.U.Stride != buffer.V.Stride)
        {
            return $"the frame-buffer pool returned chroma planes with different strides ({buffer.U.Stride} "
                + $"and {buffer.V.Stride} bytes); dav1d writes both at one shared stride.";
        }

        return null;
    }

    /// <summary>
    /// Checks that a decoded picture describes exactly the buffer this binding allocated for it, before a
    /// frame is built over that buffer.
    /// </summary>
    /// <param name="picture">The picture dav1d returned.</param>
    /// <param name="buffer">The pool buffer the picture's allocator data points at.</param>
    /// <exception cref="Dav1dException">
    /// The picture's dimensions, layout, bit depth, plane pointers or strides disagree with the buffer, or
    /// its frame header states a render size outside the AV1 format.
    /// </exception>
    /// <remarks>
    /// The frame an application receives takes its planes from the BUFFER and its dimensions from the
    /// PICTURE; a reader walks the first using the second. So the two must agree exactly, and the plane
    /// pointers and strides dav1d reports must be the ones the allocator wrote - dav1d never moves them.
    /// </remarks>
    public static void ValidatePicture(Dav1dPicture* picture, VideoFrameBuffer buffer)
    {
        if (picture == null) throw Mismatch("dav1d returned no picture structure.");
        if (buffer == null) throw Mismatch("there is no pool buffer behind the picture dav1d returned.");

        VideoFrameBufferDescriptor descriptor = buffer.Descriptor;
        Dav1dPictureParameters parameters = picture->Parameters;
        VideoPixelLayout layout = Dav1dFrameAllocator.MapLayout(parameters.Layout);

        if (parameters.Width != descriptor.Width
            || parameters.Height != descriptor.Height
            || layout != descriptor.Layout
            || parameters.BitsPerComponent != descriptor.BitDepth)
        {
            throw Mismatch(
                $"dav1d described the picture as {parameters.Width} by {parameters.Height}, layout "
                + $"{(int)parameters.Layout}, {parameters.BitsPerComponent}-bit, but the buffer behind it was "
                + $"allocated for {descriptor}.");
        }

        if (picture->Data0 != buffer.Y.Data || picture->Stride0 != buffer.Y.Stride)
        {
            throw Mismatch(
                $"dav1d reported the luma plane at 0x{picture->Data0.ToInt64():X} with a stride of "
                + $"{picture->Stride0} bytes, but the allocator gave it 0x{buffer.Y.Data.ToInt64():X} with "
                + $"{buffer.Y.Stride}.");
        }

        if (!descriptor.IsMonochrome
            && (picture->Data1 != buffer.U.Data
                || picture->Data2 != buffer.V.Data
                || picture->Stride1 != buffer.U.Stride))
        {
            throw Mismatch(
                $"dav1d reported the chroma planes at 0x{picture->Data1.ToInt64():X} and "
                + $"0x{picture->Data2.ToInt64():X} with a stride of {picture->Stride1} bytes, but the "
                + $"allocator gave it 0x{buffer.U.Data.ToInt64():X} and 0x{buffer.V.Data.ToInt64():X} with "
                + $"{buffer.U.Stride}.");
        }

        Dav1dFrameHeader* frameHeader = picture->FrameHeader;
        if (frameHeader != null
            && (frameHeader->RenderWidth > MaximumDimension || frameHeader->RenderHeight > MaximumDimension))
        {
            throw Mismatch(
                $"dav1d's frame header states a render size of {frameHeader->RenderWidth} by "
                + $"{frameHeader->RenderHeight}; an AV1 render size is at most {MaximumDimension} on each "
                + "side.");
        }
    }

    /// <summary>Checks the values a parsed sequence header reports before a stream description is built from them.</summary>
    /// <param name="header">The header dav1d filled in.</param>
    /// <exception cref="Dav1dException">The maximum frame size or the bit depth is outside the AV1 format.</exception>
    /// <remarks>
    /// A host sizes surfaces and chooses texture formats from the probe's answer before anything is decoded,
    /// so a nonsensical size here would be acted on just as a decoded frame's would.
    /// </remarks>
    public static void ValidateSequenceHeader(in Dav1dSequenceHeader header)
    {
        if (header.MaxWidth <= 0 || header.MaxHeight <= 0
            || header.MaxWidth > MaximumDimension || header.MaxHeight > MaximumDimension)
        {
            throw Mismatch(
                $"dav1d parsed a sequence header with a maximum frame size of {header.MaxWidth} by "
                + $"{header.MaxHeight}; an AV1 frame is 1 to {MaximumDimension} samples on each side.");
        }

        if (header.HighBitDepth > 2)
        {
            throw Mismatch(
                $"dav1d parsed a sequence header with a high-bit-depth code of {header.HighBitDepth}; the "
                + "only values are 0, 1 and 2 (8, 10 and 12 bits).");
        }
    }

    private static string CheckPlane(VideoFramePlane plane, int requiredStride, string name)
    {
        if (plane.Data == IntPtr.Zero) return $"the frame-buffer pool returned no memory for the {name} plane.";

        if ((plane.Data.ToInt64() & (VideoFrameBufferDescriptor.PlaneAlignment - 1)) != 0)
        {
            return $"the frame-buffer pool returned a {name} plane at 0x{plane.Data.ToInt64():X}, which is not "
                + $"{VideoFrameBufferDescriptor.PlaneAlignment}-byte aligned.";
        }

        if (plane.Stride < requiredStride || plane.Stride % VideoFrameBufferDescriptor.PlaneAlignment != 0)
        {
            return $"the frame-buffer pool returned a {name} plane with a stride of {plane.Stride} bytes; the "
                + $"descriptor needs at least {requiredStride}, in multiples of "
                + $"{VideoFrameBufferDescriptor.PlaneAlignment}.";
        }

        return null;
    }

    private static Dav1dException Mismatch(string detail) =>
        new Dav1dException(
            "The native dav1d library reported a value this binding does not accept, so the picture was "
            + $"refused rather than exposed: {detail} That should be impossible with a correct native library "
            + "and means the library and this binding disagree - a build or structure-layout mismatch, or a "
            + "defect in the native code.");
}
