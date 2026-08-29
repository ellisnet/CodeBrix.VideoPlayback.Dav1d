using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.VideoPlayback.Frames;

namespace CodeBrix.VideoPlayback.Dav1d.Tests.Internal;

/// <summary>
/// A frame-buffer pool that hands every request straight to a real
/// <see cref="PinnedFrameBufferPool" /> and writes down what happened on the way through.
/// </summary>
/// <remarks>
/// The tests need to see three things the pool itself does not report: which memory ranges buffers actually
/// occupy, which THREAD each return arrived on, and whether a particular buffer had come back at a
/// particular moment. Wrapping the real pool rather than reimplementing it means the behaviour under test is
/// still the shipped behaviour - the alignment, the padding, the fence parking, the generations - and only
/// the observation is new.
/// </remarks>
internal sealed class RecordingFrameBufferPool : IVideoFrameBufferPool, IDisposable
{
    private readonly PinnedFrameBufferPool inner = new PinnedFrameBufferPool();
    private readonly object gate = new object();
    private readonly List<PlaneRange> ranges = new List<PlaneRange>();
    private readonly HashSet<int> returnThreadIds = new HashSet<int>();
    private readonly HashSet<VideoFrameBuffer> outstanding = new HashSet<VideoFrameBuffer>();

    private long rents;
    private long returns;

    /// <summary>One plane's memory range, as a half-open interval of addresses.</summary>
    /// <param name="Start">The first byte.</param>
    /// <param name="End">One past the last byte the allocation covers.</param>
    internal readonly record struct PlaneRange(long Start, long End);

    /// <summary>The real pool underneath, for its statistics.</summary>
    public PinnedFrameBufferPool Inner => inner;

    /// <summary>How many buffers have been rented through this wrapper.</summary>
    public long Rents => Interlocked.Read(ref rents);

    /// <summary>How many buffers have been returned through this wrapper.</summary>
    public long Returns => Interlocked.Read(ref returns);

    /// <inheritdoc />
    public VideoFrameBuffer Rent(VideoFrameBufferDescriptor descriptor)
    {
        VideoFrameBuffer buffer = inner.Rent(descriptor);

        lock (gate)
        {
            rents++;
            outstanding.Add(buffer);
            Record(buffer.Y, descriptor.LumaStride, descriptor.LumaAllocationRows);
            Record(buffer.U, descriptor.ChromaStride, descriptor.ChromaAllocationRows);
            Record(buffer.V, descriptor.ChromaStride, descriptor.ChromaAllocationRows);
        }

        return buffer;
    }

    /// <inheritdoc />
    public void Return(VideoFrameBuffer buffer)
    {
        lock (gate)
        {
            returns++;
            returnThreadIds.Add(Environment.CurrentManagedThreadId);
            outstanding.Remove(buffer);
        }

        inner.Return(buffer);
    }

    /// <summary>True when the pool is still waiting for this buffer to come back.</summary>
    /// <param name="buffer">The buffer to ask about.</param>
    /// <returns>True while the buffer is rented out.</returns>
    public bool IsOutstanding(VideoFrameBuffer buffer)
    {
        lock (gate) return outstanding.Contains(buffer);
    }

    /// <summary>The distinct managed thread identifiers returns have arrived on.</summary>
    /// <returns>The identifiers.</returns>
    public int[] GetReturnThreadIds()
    {
        lock (gate)
        {
            int[] ids = new int[returnThreadIds.Count];
            returnThreadIds.CopyTo(ids);
            return ids;
        }
    }

    /// <summary>True when the address lies inside memory this pool allocated.</summary>
    /// <param name="address">The address to test.</param>
    /// <param name="length">How many bytes past it must also lie inside.</param>
    /// <returns>True when the whole span is inside one of the pool's plane allocations.</returns>
    public bool Contains(IntPtr address, long length)
    {
        long start = address.ToInt64();
        long end = start + length;

        lock (gate)
        {
            foreach (PlaneRange range in ranges)
            {
                if (start >= range.Start && end <= range.End) return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose() => inner.Dispose();

    private void Record(VideoFramePlane plane, int stride, int rows)
    {
        if (plane.Data == IntPtr.Zero || rows <= 0) return;

        long start = plane.Data.ToInt64();
        long end = start + ((long)stride * rows) + VideoFrameBufferDescriptor.TailPadding;

        foreach (PlaneRange existing in ranges)
        {
            if (existing.Start == start && existing.End == end) return;
        }

        ranges.Add(new PlaneRange(start, end));
    }
}
