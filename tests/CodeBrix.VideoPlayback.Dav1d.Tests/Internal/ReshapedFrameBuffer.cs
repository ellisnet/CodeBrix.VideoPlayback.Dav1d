using CodeBrix.VideoPlayback.Frames;

namespace CodeBrix.VideoPlayback.Dav1d.Tests.Internal;

/// <summary>
/// A frame buffer whose descriptor and planes are whatever the test says they are - including combinations
/// no correct pool would ever produce.
/// </summary>
/// <remarks>
/// The planes normally point into a real buffer rented from a <see cref="PinnedFrameBufferPool" />, so the
/// addresses are real memory; what the test changes is the stride, the alignment or the descriptor that the
/// buffer claims to have been allocated for. It owns no memory of its own.
/// </remarks>
internal sealed class ReshapedFrameBuffer : VideoFrameBuffer
{
    /// <summary>Creates a buffer with the given descriptor and planes.</summary>
    /// <param name="descriptor">The descriptor the buffer claims to have been allocated for.</param>
    /// <param name="y">The luma plane.</param>
    /// <param name="u">The first chroma plane.</param>
    /// <param name="v">The second chroma plane.</param>
    public ReshapedFrameBuffer(
        VideoFrameBufferDescriptor descriptor,
        VideoFramePlane y,
        VideoFramePlane u,
        VideoFramePlane v)
        : base(descriptor, VideoFrameStorage.HostMemory)
    {
        Y = y;
        U = u;
        V = v;
    }

    /// <summary>Copies a plane with a different stride.</summary>
    /// <param name="plane">The plane to copy.</param>
    /// <param name="stride">The stride the copy reports.</param>
    /// <returns>The copy.</returns>
    public static VideoFramePlane WithStride(VideoFramePlane plane, int stride) =>
        new VideoFramePlane(plane.Data, stride, plane.Width, plane.Height, plane.BytesPerSample);

    /// <summary>Copies a plane starting a few bytes further on.</summary>
    /// <param name="plane">The plane to copy.</param>
    /// <param name="offset">How many bytes to move the start by.</param>
    /// <returns>The copy.</returns>
    public static VideoFramePlane Offset(VideoFramePlane plane, int offset) =>
        new VideoFramePlane(plane.Data + offset, plane.Stride, plane.Width, plane.Height, plane.BytesPerSample);
}
