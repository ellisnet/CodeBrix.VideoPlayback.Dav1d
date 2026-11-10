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
/// Fences the checks that stop a value reported by the native library - a size, a stride, a plane pointer,
/// a bit depth - from being used before it has been compared with what the binding allocated.
/// </summary>
/// <remarks>
/// A correct dav1d never trips any of these, so the tests hand each check a deliberately wrong value at the
/// boundary: a picture or sequence header built by hand in native memory, or a buffer that claims a shape it
/// does not have. Each wrong value must be refused, and named in the refusal.
/// </remarks>
public unsafe class Dav1dNativeChecksTests
{
    private static readonly VideoFrameBufferDescriptor Hd420 =
        new VideoFrameBufferDescriptor(1920, 1080, VideoPixelLayout.I420, 8);

    private const long DefaultLimit = 8192L * 8192L;

    [Fact]
    public void CheckAllocationRequest_accepts_an_ordinary_picture()
    {
        //Arrange
        Dav1dPictureParameters parameters = Parameters(1920, 1080, Dav1dPixelLayout.I420, 10);

        //Act
        string refusal = Dav1dNativeChecks.CheckAllocationRequest(parameters, DefaultLimit);

        //Assert
        refusal.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-16, 1080)]
    [InlineData(65537, 16)]
    [InlineData(16, 65537)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void CheckAllocationRequest_refuses_a_size_no_AV1_frame_can_have(int width, int height)
    {
        //Arrange
        Dav1dPictureParameters parameters = Parameters(width, height, Dav1dPixelLayout.I420, 8);

        //Act
        string refusal = Dav1dNativeChecks.CheckAllocationRequest(parameters, 0);

        //Assert
        refusal.Should().Contain($"{width} by {height}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(16)]
    public void CheckAllocationRequest_refuses_a_bit_depth_AV1_does_not_have(int bitDepth)
    {
        //Arrange
        Dav1dPictureParameters parameters = Parameters(64, 64, Dav1dPixelLayout.I420, bitDepth);

        //Act
        string refusal = Dav1dNativeChecks.CheckAllocationRequest(parameters, DefaultLimit);

        //Assert
        refusal.Should().Contain($"{bitDepth} bits");
    }

    [Fact]
    public void CheckAllocationRequest_refuses_a_layout_AV1_does_not_have()
    {
        //Arrange
        Dav1dPictureParameters parameters = Parameters(64, 64, (Dav1dPixelLayout)7, 8);

        //Act
        string refusal = Dav1dNativeChecks.CheckAllocationRequest(parameters, DefaultLimit);

        //Assert
        refusal.Should().Contain("plane layout 7");
    }

    [Fact]
    public void CheckAllocationRequest_applies_the_frame_size_limit_itself()
    {
        //Arrange
        // Legal AV1, and over a 1080p limit: dav1d should have refused it first, and this is the second line.
        Dav1dPictureParameters parameters = Parameters(3840, 2160, Dav1dPixelLayout.I420, 8);

        //Act
        string refusal = Dav1dNativeChecks.CheckAllocationRequest(parameters, 1920L * 1080L);

        //Assert
        refusal.Should().Contain("3840 by 2160");
        refusal.Should().Contain((1920L * 1080L).ToString());
    }

    [Fact]
    public void CheckRentedBuffer_accepts_what_the_shipped_pool_rents()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer buffer = pool.Rent(Hd420);

        //Act
        string refusal = Dav1dNativeChecks.CheckRentedBuffer(buffer, Hd420);

        //Assert
        refusal.Should().BeNull();
        pool.Return(buffer);
    }

    [Fact]
    public void CheckRentedBuffer_refuses_a_buffer_rented_for_a_smaller_picture()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer buffer = pool.Rent(new VideoFrameBufferDescriptor(640, 360, VideoPixelLayout.I420, 8));

        //Act
        string refusal = Dav1dNativeChecks.CheckRentedBuffer(buffer, Hd420);

        //Assert
        refusal.Should().Contain("was asked for");
        pool.Return(buffer);
    }

    [Fact]
    public void CheckRentedBuffer_refuses_a_luma_stride_too_narrow_for_the_rows_dav1d_writes()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer real = pool.Rent(Hd420);
        ReshapedFrameBuffer narrow = new ReshapedFrameBuffer(
            Hd420,
            ReshapedFrameBuffer.WithStride(real.Y, Hd420.LumaStride - 64),
            real.U,
            real.V);

        //Act
        string refusal = Dav1dNativeChecks.CheckRentedBuffer(narrow, Hd420);

        //Assert
        refusal.Should().Contain("luma plane with a stride");
        pool.Return(real);
    }

    [Fact]
    public void CheckRentedBuffer_refuses_chroma_planes_whose_strides_differ()
    {
        //Arrange
        // dav1d writes both chroma planes at ONE stride. A wider U stride would make it overrun V.
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer real = pool.Rent(Hd420);
        ReshapedFrameBuffer uneven = new ReshapedFrameBuffer(
            Hd420,
            real.Y,
            ReshapedFrameBuffer.WithStride(real.U, real.U.Stride + 64),
            real.V);

        //Act
        string refusal = Dav1dNativeChecks.CheckRentedBuffer(uneven, Hd420);

        //Assert
        refusal.Should().Contain("different strides");
        pool.Return(real);
    }

    [Fact]
    public void CheckRentedBuffer_refuses_a_plane_that_is_not_64_byte_aligned()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer real = pool.Rent(Hd420);
        ReshapedFrameBuffer misaligned = new ReshapedFrameBuffer(
            Hd420,
            real.Y,
            real.U,
            ReshapedFrameBuffer.Offset(real.V, 8));

        //Act
        string refusal = Dav1dNativeChecks.CheckRentedBuffer(misaligned, Hd420);

        //Assert
        refusal.Should().Contain("second chroma plane");
        refusal.Should().Contain("aligned");
        pool.Return(real);
    }

    [Fact]
    public void ValidatePicture_accepts_a_picture_that_describes_its_own_buffer()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer buffer = pool.Rent(Hd420);
        Dav1dPicture* picture = PictureOver(buffer, Dav1dPixelLayout.I420);

        try
        {
            //Act
            Action act = () => Dav1dNativeChecks.ValidatePicture(picture, buffer);

            //Assert
            act.Should().NotThrow();
        }
        finally
        {
            NativeMemory.Free(picture);
            pool.Return(buffer);
        }
    }

    [Fact]
    public void ValidatePicture_refuses_a_picture_larger_than_the_buffer_behind_it()
    {
        //Arrange
        // The frame would take its planes from the 1080p buffer and its size from the picture - so a reader
        // would walk 2160 rows of memory holding 1080.
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer buffer = pool.Rent(Hd420);
        Dav1dPicture* picture = PictureOver(buffer, Dav1dPixelLayout.I420);
        picture->Parameters.Width = 3840;
        picture->Parameters.Height = 2160;

        try
        {
            //Act
            Dav1dException failure = Assert.Throws<Dav1dException>(
                () => Dav1dNativeChecks.ValidatePicture(picture, buffer));

            //Assert
            failure.Message.Should().Contain("3840 by 2160");
            failure.Message.Should().Contain("refused rather than exposed");
        }
        finally
        {
            NativeMemory.Free(picture);
            pool.Return(buffer);
        }
    }

    [Fact]
    public void ValidatePicture_refuses_a_bit_depth_that_differs_from_the_buffer()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer buffer = pool.Rent(Hd420);
        Dav1dPicture* picture = PictureOver(buffer, Dav1dPixelLayout.I420);
        picture->Parameters.BitsPerComponent = 10;

        try
        {
            //Act
            Dav1dException failure = Assert.Throws<Dav1dException>(
                () => Dav1dNativeChecks.ValidatePicture(picture, buffer));

            //Assert
            failure.Message.Should().Contain("10-bit");
        }
        finally
        {
            NativeMemory.Free(picture);
            pool.Return(buffer);
        }
    }

    [Fact]
    public void ValidatePicture_refuses_a_luma_pointer_the_allocator_did_not_give_out()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer buffer = pool.Rent(Hd420);
        Dav1dPicture* picture = PictureOver(buffer, Dav1dPixelLayout.I420);
        picture->Data0 += 4096;

        try
        {
            //Act
            Dav1dException failure = Assert.Throws<Dav1dException>(
                () => Dav1dNativeChecks.ValidatePicture(picture, buffer));

            //Assert
            failure.Message.Should().Contain("luma plane");
        }
        finally
        {
            NativeMemory.Free(picture);
            pool.Return(buffer);
        }
    }

    [Fact]
    public void ValidatePicture_refuses_a_chroma_stride_the_allocator_did_not_give_out()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer buffer = pool.Rent(Hd420);
        Dav1dPicture* picture = PictureOver(buffer, Dav1dPixelLayout.I420);
        picture->Stride1 *= 4;

        try
        {
            //Act
            Dav1dException failure = Assert.Throws<Dav1dException>(
                () => Dav1dNativeChecks.ValidatePicture(picture, buffer));

            //Assert
            failure.Message.Should().Contain("chroma planes");
        }
        finally
        {
            NativeMemory.Free(picture);
            pool.Return(buffer);
        }
    }

    [Fact]
    public void ValidatePicture_ignores_the_chroma_fields_of_a_monochrome_picture()
    {
        //Arrange
        // A monochrome buffer has no chroma planes, and dav1d never reads or writes them.
        VideoFrameBufferDescriptor gray = new VideoFrameBufferDescriptor(320, 240, VideoPixelLayout.Gray, 8);
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer buffer = pool.Rent(gray);
        Dav1dPicture* picture = PictureOver(buffer, Dav1dPixelLayout.I400);
        picture->Data1 = (IntPtr)0x1234;

        try
        {
            //Act
            Action act = () => Dav1dNativeChecks.ValidatePicture(picture, buffer);

            //Assert
            act.Should().NotThrow();
        }
        finally
        {
            NativeMemory.Free(picture);
            pool.Return(buffer);
        }
    }

    [Fact]
    public void ValidatePicture_refuses_a_render_size_outside_the_AV1_format()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrameBuffer buffer = pool.Rent(Hd420);
        Dav1dPicture* picture = PictureOver(buffer, Dav1dPixelLayout.I420);
        Dav1dFrameHeader* header = (Dav1dFrameHeader*)NativeMemory.AllocZeroed((nuint)sizeof(Dav1dFrameHeader));
        header->RenderWidth = 1 << 20;
        header->RenderHeight = 1080;
        picture->FrameHeader = header;

        try
        {
            //Act
            Dav1dException failure = Assert.Throws<Dav1dException>(
                () => Dav1dNativeChecks.ValidatePicture(picture, buffer));

            //Assert
            failure.Message.Should().Contain("render size");
        }
        finally
        {
            NativeMemory.Free(header);
            NativeMemory.Free(picture);
            pool.Return(buffer);
        }
    }

    [Fact]
    public void ValidateSequenceHeader_accepts_an_ordinary_header()
    {
        //Arrange
        Dav1dSequenceHeader header = default;
        header.MaxWidth = 1920;
        header.MaxHeight = 1080;
        header.HighBitDepth = 1;

        //Act
        Action act = () => Dav1dNativeChecks.ValidateSequenceHeader(header);

        //Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0, 1080, 0)]
    [InlineData(1920, -1, 0)]
    [InlineData(70000, 1080, 0)]
    [InlineData(1920, 70000, 0)]
    [InlineData(1920, 1080, 3)]
    public void ValidateSequenceHeader_refuses_values_outside_the_AV1_format(int maxWidth, int maxHeight, byte highBitDepth)
    {
        //Arrange
        Dav1dSequenceHeader header = default;
        header.MaxWidth = maxWidth;
        header.MaxHeight = maxHeight;
        header.HighBitDepth = highBitDepth;

        //Act
        Action act = () => Dav1dNativeChecks.ValidateSequenceHeader(header);

        //Assert
        act.Should().Throw<Dav1dException>().WithMessage("*sequence header*");
    }

    private static Dav1dPictureParameters Parameters(int width, int height, Dav1dPixelLayout layout, int bitDepth) =>
        new Dav1dPictureParameters
        {
            Width = width,
            Height = height,
            Layout = layout,
            BitsPerComponent = bitDepth,
        };

    /// <summary>Builds, in native memory, the picture a correct dav1d would return over a buffer.</summary>
    private static Dav1dPicture* PictureOver(VideoFrameBuffer buffer, Dav1dPixelLayout layout)
    {
        Dav1dPicture* picture = (Dav1dPicture*)NativeMemory.AllocZeroed((nuint)sizeof(Dav1dPicture));
        picture->Parameters = Parameters(
            buffer.Descriptor.Width,
            buffer.Descriptor.Height,
            layout,
            buffer.Descriptor.BitDepth);
        picture->Data0 = buffer.Y.Data;
        picture->Data1 = buffer.U.Data;
        picture->Data2 = buffer.V.Data;
        picture->Stride0 = buffer.Y.Stride;
        picture->Stride1 = buffer.U.Stride;
        return picture;
    }
}
