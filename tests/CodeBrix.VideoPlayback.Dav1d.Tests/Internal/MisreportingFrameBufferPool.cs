using System;
using System.Threading;
using CodeBrix.VideoPlayback.Frames;

namespace CodeBrix.VideoPlayback.Dav1d.Tests.Internal;

/// <summary>
/// A frame-buffer pool with a bug: asked for a buffer, it hands back a real one rented for a SMALLER
/// picture - half the requested width and height.
/// </summary>
/// <remarks>
/// It is the shape of mistake that would let a decoder write past the end of its buffer, so it is what the
/// allocator's check on the rented buffer is fenced with. It counts what it rents and what comes back, so a
/// test can show the refused buffer was returned rather than leaked.
/// </remarks>
internal sealed class MisreportingFrameBufferPool : IVideoFrameBufferPool, IDisposable
{
    private readonly PinnedFrameBufferPool inner = new PinnedFrameBufferPool();
    private long rents;
    private long returns;

    /// <summary>How many buffers have been handed out.</summary>
    public long Rents => Interlocked.Read(ref rents);

    /// <summary>How many buffers have come back.</summary>
    public long Returns => Interlocked.Read(ref returns);

    /// <inheritdoc />
    public VideoFrameBuffer Rent(VideoFrameBufferDescriptor descriptor)
    {
        Interlocked.Increment(ref rents);
        VideoFrameBufferDescriptor smaller = new VideoFrameBufferDescriptor(
            Math.Max(1, descriptor.Width / 2),
            Math.Max(1, descriptor.Height / 2),
            descriptor.Layout,
            descriptor.BitDepth);
        return inner.Rent(smaller);
    }

    /// <inheritdoc />
    public void Return(VideoFrameBuffer buffer)
    {
        Interlocked.Increment(ref returns);
        inner.Return(buffer);
    }

    /// <inheritdoc />
    public void Dispose() => inner.Dispose();
}
