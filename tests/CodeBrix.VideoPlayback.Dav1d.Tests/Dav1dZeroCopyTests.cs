using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.VideoPlayback.Dav1d.Decoding;
using CodeBrix.VideoPlayback.Dav1d.Tests.Internal;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Proves the thing this binding exists for: that dav1d decodes INTO the host's own frame buffers, that
/// nothing is copied on the way out, that playback settles into allocating nothing, and that a buffer goes
/// home only when both the application and dav1d have finished with it.
/// </summary>
/// <remarks>
/// These are the guarantees the frame data path in CodeBrix.VideoPlayback makes to a presenter, restated
/// from the decoder's side. A binding that quietly copied every frame would pass every conformance test in
/// this suite and fail every test in this class.
/// </remarks>
public class Dav1dZeroCopyTests
{
    [Fact]
    public void The_decoder_says_it_writes_into_the_hosts_buffers()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoDecoderOptions options = new VideoDecoderOptions { BufferPool = pool };

        //Act
        using IVideoDecoder decoder = new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        //Assert
        decoder.SupportsExternalBuffers.Should().BeTrue();
    }

    [Fact]
    public void Every_frames_plane_pointers_land_inside_memory_the_pool_handed_out()
    {
        //Arrange
        using RecordingFrameBufferPool pool = new RecordingFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool };
        int checkedFrames = 0;
        bool everyPlaneInside = true;

        //Act
        DecodeThrough(pool, options, "01-8bit-420-aom.ivf", frame =>
        {
            checkedFrames++;
            everyPlaneInside &= pool.Contains(frame.Y.Data, (long)frame.Y.Stride * frame.Height);
            everyPlaneInside &= pool.Contains(frame.U.Data, (long)frame.U.Stride * frame.U.Height);
            everyPlaneInside &= pool.Contains(frame.V.Data, (long)frame.V.Stride * frame.V.Height);
        });

        //Assert
        checkedFrames.Should().BeGreaterThan(0);
        everyPlaneInside.Should().BeTrue();
    }

    [Fact]
    public void A_frames_planes_are_the_pool_buffers_own_planes_and_not_a_copy()
    {
        //Arrange
        using RecordingFrameBufferPool pool = new RecordingFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool };
        bool sameMemory = true;
        int frames = 0;

        //Act
        DecodeThrough(pool, options, "01-8bit-420-aom.ivf", frame =>
        {
            frames++;
            sameMemory &= frame.Y.Data == frame.Buffer.Y.Data;
            sameMemory &= frame.U.Data == frame.Buffer.U.Data;
            sameMemory &= frame.V.Data == frame.Buffer.V.Data;
        });

        //Assert
        frames.Should().BeGreaterThan(0);
        sameMemory.Should().BeTrue();
    }

    [Fact]
    public void The_pool_stops_allocating_once_playback_is_warm()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { Threads = 2 };
        List<long> allocationsPerFrame = new List<long>();

        //Act
        VectorDecoder.RunResult result = VectorDecoder.Decode(
            "01-8bit-420-aom.ivf",
            options,
            pool,
            _ => allocationsPerFrame.Add(pool.GetStatistics().Allocations));

        //Assert
        result.Frames.Should().BeGreaterThan(12);
        allocationsPerFrame.Last().Should().Be(allocationsPerFrame[allocationsPerFrame.Count / 2]);
        result.Statistics.Rents.Should().BeGreaterThan(result.Statistics.Allocations);
        result.Statistics.Generation.Should().Be(0);
    }

    [Fact]
    public void Buffers_come_back_from_dav1ds_own_frame_threads_and_the_pool_takes_them()
    {
        //Arrange
        using RecordingFrameBufferPool pool = new RecordingFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 4 };
        int decodeThreadId = Environment.CurrentManagedThreadId;

        //Act
        DecodeThrough(pool, options, "01-8bit-420-aom.ivf", _ => { });
        int[] returnThreads = pool.GetReturnThreadIds();

        //Assert
        pool.Returns.Should().Be(pool.Rents);
        returnThreads.Any(id => id != decodeThreadId).Should().BeTrue();
    }

    [Fact]
    public void The_allocator_sees_the_same_releases_the_pool_does_and_records_the_threads()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 4 };
        int decodeThreadId = Environment.CurrentManagedThreadId;
        Dav1dFrameAllocator allocator;
        long releasedBeforeClosing;

        //Act
        using (Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options))
        {
            allocator = decoder.Allocator;
            allocator.TrackReleaseThreads = true;
            Drive(decoder, "01-8bit-420-aom.ivf", _ => { });
            releasedBeforeClosing = allocator.Releases;
        }

        //Assert
        allocator.Allocations.Should().BeGreaterThan(0);

        // Every buffer comes back - but only AFTER the decoder is closed. At the end of the stream dav1d is
        // still holding the pictures in its reference-frame slots, which is precisely the lifetime this
        // binding has to respect: the application had disposed all of these frames already.
        releasedBeforeClosing.Should().BeLessThan(allocator.Allocations);
        allocator.Releases.Should().Be(allocator.Allocations);
        allocator.GetReleaseThreadIds().Any(id => id != decodeThreadId).Should().BeTrue();
    }

    [Fact]
    public void A_buffer_stays_out_of_the_pool_while_dav1d_is_still_predicting_from_it()
    {
        //Arrange
        using RecordingFrameBufferPool pool = new RecordingFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 1 };
        int heldByDav1dAfterDisposal = 0;
        int frames = 0;

        //Act
        using (Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options))
        {
            DriveWithoutDisposing(decoder, "01-8bit-420-aom.ivf", frame =>
            {
                frames++;
                VideoFrameBuffer buffer = frame.Buffer;
                frame.Dispose();
                if (pool.IsOutstanding(buffer)) heldByDav1dAfterDisposal++;
            });
        }

        //Assert
        frames.Should().BeGreaterThan(0);
        heldByDav1dAfterDisposal.Should().BeGreaterThan(0);
        pool.Returns.Should().Be(pool.Rents);
    }

    [Fact]
    public void Retaining_a_frame_keeps_its_buffer_out_of_the_pool_until_the_last_reference_goes()
    {
        //Arrange
        using RecordingFrameBufferPool pool = new RecordingFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 1 };
        VideoFrame held = null;
        VideoFrameBuffer heldBuffer = null;
        bool outstandingWhileRetained = false;

        //Act
        using (Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options))
        {
            DriveWithoutDisposing(decoder, "01-8bit-420-aom.ivf", frame =>
            {
                if (held == null)
                {
                    held = frame.Retain();
                    heldBuffer = frame.Buffer;
                }

                frame.Dispose();
            });

            outstandingWhileRetained = pool.IsOutstanding(heldBuffer);
        }

        held.Dispose();

        //Assert
        heldBuffer.Should().NotBeNull();
        outstandingWhileRetained.Should().BeTrue();
        pool.IsOutstanding(heldBuffer).Should().BeFalse();
    }

    [Fact]
    public void A_warm_decode_loop_allocates_nothing_at_all()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 1 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        // The callback is built ONCE, here, so the measurement below is of the decode path and not of the
        // harness driving it.
        int frames = 0;
        Action<VideoFrame> release = frame =>
        {
            frame.Dispose();
            frames++;
        };

        // Warm up. The first passes allocate the buffers, the picture leases, the pinned input blocks and
        // the pool's own free lists; after that there is nothing left to allocate.
        for (int pass = 0; pass < 3; pass++)
        {
            decoder.Flush();
            DriveWithoutDisposing(decoder, stream, release);
        }

        int warmUpFrames = frames;

        //Act
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int pass = 0; pass < 25; pass++)
        {
            decoder.Flush();
            DriveWithoutDisposing(decoder, stream, release);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        //Assert
        // NONE. Not "a little". Buffers, leases, input blocks and now the frame objects themselves all come
        // from pools, so six hundred decoded pictures touch the managed heap not at all.
        //
        // This number was 128 bytes a frame until IVideoFrameBufferPool grew TakeFrame and ReturnFrame:
        // before that, a decoder had to interpose a pool of its own to map the managed reference count onto
        // dav1d's, and interposing meant the session's pool was never the pool a frame was created with, so
        // every picture allocated an object. The lease now forwards both members to the session's pool.
        warmUpFrames.Should().BeGreaterThan(0);
        (frames - warmUpFrames).Should().Be(25 * stream.Frames.Count);
        allocated.Should().Be(0);
    }

    [Fact]
    public void Frame_objects_are_recycled_rather_than_allocated_afresh()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 1 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));
        HashSet<VideoFrame> distinct = new HashSet<VideoFrame>(ReferenceEqualityComparer.Instance);
        int frames = 0;

        //Act
        using (Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options))
        {
            for (int pass = 0; pass < 5; pass++)
            {
                decoder.Flush();
                DriveWithoutDisposing(decoder, stream, frame =>
                {
                    distinct.Add(frame);
                    frame.Dispose();
                    frames++;
                });
            }
        }

        //Assert
        // Far more pictures than frame objects: the same handful of objects go round and round, which is
        // what "recycled" has to mean if it is to mean anything.
        frames.Should().Be(5 * stream.Frames.Count);
        distinct.Count.Should().BeLessThan(frames);
        distinct.Count.Should().BeLessThan(32);
    }

    private static void DecodeThrough(
        IVideoFrameBufferPool pool,
        VideoDecoderOptions options,
        string fileName,
        Action<VideoFrame> onFrame)
    {
        options.BufferPool = pool;

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        Drive(decoder, fileName, onFrame);
    }

    private static void Drive(Dav1dVideoDecoder decoder, string fileName, Action<VideoFrame> onFrame) =>
        DriveWithoutDisposing(decoder, fileName, frame =>
        {
            using (frame) onFrame(frame);
        });

    private static void DriveWithoutDisposing(
        Dav1dVideoDecoder decoder,
        string fileName,
        Action<VideoFrame> onFrame) =>
        DriveWithoutDisposing(decoder, IvfStreamReader.Read(ConformanceVectors.PathOf(fileName)), onFrame);

    private static void DriveWithoutDisposing(
        Dav1dVideoDecoder decoder,
        IvfStreamReader.IvfStream stream,
        Action<VideoFrame> onFrame)
    {
        // Indexed rather than foreach: enumerating an IReadOnlyList<T> boxes an enumerator every time, and
        // one of the tests below measures allocation down to the byte.
        for (int index = 0; index < stream.Frames.Count; index++)
        {
            IvfStreamReader.IvfFrame ivfFrame = stream.Frames[index];
            VideoPacket packet = new VideoPacket(
                ivfFrame.Data,
                VectorDecoder.TimestampOf(ivfFrame.Timestamp, stream),
                index == 0);

            while (!decoder.SendPacket(packet))
            {
                if (!decoder.TryReceiveFrame(out VideoFrame parked))
                {
                    throw new InvalidOperationException(
                        "The decoder reported back-pressure but produced no frame, which would be a deadlock.");
                }

                onFrame(parked);
            }

            while (decoder.TryReceiveFrame(out VideoFrame produced)) onFrame(produced);
        }

        decoder.Drain();
        while (decoder.TryReceiveFrame(out VideoFrame drained)) onFrame(drained);
    }
}
