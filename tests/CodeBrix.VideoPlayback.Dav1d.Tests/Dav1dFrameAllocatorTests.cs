using System;
using System.Runtime.InteropServices;
using CodeBrix.VideoPlayback.Dav1d.Decoding;
using CodeBrix.VideoPlayback.Dav1d.Interop;
using CodeBrix.VideoPlayback.Dav1d.Tests.Internal;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Fences the picture allocator's refusals: a request dav1d makes is checked before anything is rented, and
/// a buffer the pool rents is checked before dav1d is told to write into it.
/// </summary>
/// <remarks>
/// The allocator is driven directly with pictures built by hand, standing in for dav1d, so each wrong value
/// can be put exactly at the boundary. The last test drives the whole decoder over a real conformance stream
/// with a pool that rents the wrong buffers, to show the refusal reaches the application as an exception that
/// says why.
/// </remarks>
public unsafe class Dav1dFrameAllocatorTests
{
    [Fact]
    public void Allocate_points_the_picture_at_a_correctly_shaped_buffer()
    {
        //Arrange
        using RecordingFrameBufferPool pool = new RecordingFrameBufferPool();
        Dav1dFrameAllocator allocator = new Dav1dFrameAllocator(pool, 8192L * 8192L);
        Dav1dPicture* picture = Request(1280, 720, Dav1dPixelLayout.I420, 8);

        try
        {
            //Act
            int result = allocator.Allocate(picture);

            //Assert
            result.Should().Be(0);
            pool.Rents.Should().Be(1);
            pool.Contains(picture->Data0, 1).Should().BeTrue();
            allocator.TakeLastRefusal().Should().BeNull();
        }
        finally
        {
            Dav1dFrameAllocator.ReleaseCallback(picture, allocator.Cookie);
            NativeMemory.Free(picture);
            allocator.ReleaseDecoderReference();
        }

        pool.Returns.Should().Be(1);
    }

    [Fact]
    public void Allocate_refuses_a_picture_over_the_limit_before_renting_anything()
    {
        //Arrange
        using RecordingFrameBufferPool pool = new RecordingFrameBufferPool();
        Dav1dFrameAllocator allocator = new Dav1dFrameAllocator(pool, 1920L * 1080L);
        Dav1dPicture* picture = Request(7680, 4320, Dav1dPixelLayout.I420, 8);

        try
        {
            //Act
            int result = allocator.Allocate(picture);

            //Assert
            result.Should().Be(Dav1dErrorCodes.Invalid);
            pool.Rents.Should().Be(0);
            picture->AllocatorData.Should().Be(IntPtr.Zero);
            allocator.TakeLastRefusal().Should().Contain("7680 by 4320");
            allocator.TakeLastRefusal().Should().BeNull();
        }
        finally
        {
            NativeMemory.Free(picture);
            allocator.ReleaseDecoderReference();
        }
    }

    [Fact]
    public void Allocate_refuses_a_size_no_AV1_frame_can_have_even_without_a_limit()
    {
        //Arrange
        using RecordingFrameBufferPool pool = new RecordingFrameBufferPool();
        Dav1dFrameAllocator allocator = new Dav1dFrameAllocator(pool);
        Dav1dPicture* picture = Request(100000, 16, Dav1dPixelLayout.I444, 12);

        try
        {
            //Act
            int result = allocator.Allocate(picture);

            //Assert
            result.Should().Be(Dav1dErrorCodes.Invalid);
            pool.Rents.Should().Be(0);
        }
        finally
        {
            NativeMemory.Free(picture);
            allocator.ReleaseDecoderReference();
        }
    }

    [Fact]
    public void Allocate_refuses_a_buffer_smaller_than_requested_and_gives_it_back()
    {
        //Arrange
        using MisreportingFrameBufferPool pool = new MisreportingFrameBufferPool();
        Dav1dFrameAllocator allocator = new Dav1dFrameAllocator(pool);
        Dav1dPicture* picture = Request(1280, 720, Dav1dPixelLayout.I420, 8);

        try
        {
            //Act
            int result = allocator.Allocate(picture);

            //Assert
            result.Should().Be(Dav1dErrorCodes.Invalid);
            picture->Data0.Should().Be(IntPtr.Zero);
            picture->AllocatorData.Should().Be(IntPtr.Zero);
            pool.Rents.Should().Be(1);
            pool.Returns.Should().Be(1);
            allocator.TakeLastRefusal().Should().Contain("was asked for");
        }
        finally
        {
            NativeMemory.Free(picture);
            allocator.ReleaseDecoderReference();
        }
    }

    [Fact]
    public void A_refused_buffer_surfaces_as_an_exception_that_says_why()
    {
        //Arrange
        using MisreportingFrameBufferPool pool = new MisreportingFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 1 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        //Act
        Action act = () =>
        {
            foreach (IvfStreamReader.IvfFrame ivfFrame in stream.Frames)
            {
                VideoPacket packet = new VideoPacket(ivfFrame.Data, TimeSpan.Zero, false);
                while (!decoder.SendPacket(packet))
                {
                    if (decoder.TryReceiveFrame(out VideoFrame parked)) parked.Dispose();
                }

                while (decoder.TryReceiveFrame(out VideoFrame frame)) frame.Dispose();
            }

            decoder.Drain();
            while (decoder.TryReceiveFrame(out VideoFrame drained)) drained.Dispose();
        };

        //Assert
        Dav1dException failure = Assert.Throws<Dav1dException>(act);
        failure.Message.Should().Contain("The picture allocator refused");
        failure.Message.Should().Contain("was asked for");
        pool.Returns.Should().Be(pool.Rents);
    }

    private static Dav1dPicture* Request(int width, int height, Dav1dPixelLayout layout, int bitDepth)
    {
        Dav1dPicture* picture = (Dav1dPicture*)NativeMemory.AllocZeroed((nuint)sizeof(Dav1dPicture));
        picture->Parameters.Width = width;
        picture->Parameters.Height = height;
        picture->Parameters.Layout = layout;
        picture->Parameters.BitsPerComponent = bitDepth;
        return picture;
    }
}
