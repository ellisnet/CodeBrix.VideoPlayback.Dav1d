using System;
using System.Runtime.InteropServices;
using CodeBrix.VideoPlayback.Dav1d.Interop;
using CodeBrix.VideoPlayback.Frames;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Checks every managed declaration of a dav1d structure against the sizes and offsets the real headers
/// produce.
/// </summary>
/// <remarks>
/// <para>
/// A binding written in blittable structures is fast because nothing marshals anything - and wrong in the
/// worst possible way if a single field is at the wrong offset, because the result is not an exception but a
/// plausible-looking number read out of the middle of some other field. Native memory does not complain.
/// </para>
/// <para>
/// The values compared against were produced by compiling a C program against the exact headers vendored in
/// <c>dav1d-native-tools/dav1d/include/dav1d</c> and printing <c>sizeof</c> and <c>offsetof</c> for every
/// field this binding touches. MAINTAINER-README.txt has the program and how to re-run it if dav1d is ever
/// re-vendored.
/// </para>
/// </remarks>
public class Dav1dNativeLayoutTests
{
    [Fact]
    public unsafe void Every_structure_is_the_size_the_headers_say_it_is()
    {
        //Arrange & Act & Assert
        sizeof(Dav1dSettings).Should().Be(Dav1dNativeLayout.SettingsSize);
        sizeof(Dav1dPicAllocator).Should().Be(Dav1dNativeLayout.PicAllocatorSize);
        sizeof(Dav1dLogger).Should().Be(Dav1dNativeLayout.LoggerSize);
        sizeof(Dav1dPicture).Should().Be(Dav1dNativeLayout.PictureSize);
        sizeof(Dav1dPictureParameters).Should().Be(Dav1dNativeLayout.PictureParametersSize);
        sizeof(Dav1dData).Should().Be(Dav1dNativeLayout.DataSize);
        sizeof(Dav1dDataProps).Should().Be(Dav1dNativeLayout.DataPropsSize);
        sizeof(Dav1dUserData).Should().Be(Dav1dNativeLayout.UserDataSize);
        sizeof(Dav1dSequenceHeader).Should().Be(Dav1dNativeLayout.SequenceHeaderSize);
        sizeof(Dav1dFrameHeader).Should().Be(Dav1dNativeLayout.FrameHeaderSize);
        sizeof(Dav1dContentLightLevel).Should().Be(Dav1dNativeLayout.ContentLightLevelSize);
        sizeof(Dav1dMasteringDisplay).Should().Be(Dav1dNativeLayout.MasteringDisplaySize);
    }

    [Fact]
    public void The_settings_structure_puts_its_callbacks_where_the_headers_put_them()
    {
        //Arrange & Act & Assert
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.ThreadCount)).Should().Be(0);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.MaxFrameDelay)).Should().Be(4);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.ApplyGrain)).Should().Be(8);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.OperatingPoint)).Should().Be(12);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.AllLayers)).Should().Be(16);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.FrameSizeLimit)).Should().Be(20);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.Allocator))
            .Should().Be(Dav1dNativeLayout.SettingsAllocatorOffset);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.Logger))
            .Should().Be(Dav1dNativeLayout.SettingsLoggerOffset);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.StrictStdCompliance)).Should().Be(64);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.OutputInvisibleFrames)).Should().Be(68);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.InloopFilters)).Should().Be(72);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.DecodeFrameType)).Should().Be(76);
        OffsetOf<Dav1dSettings>(nameof(Dav1dSettings.Reserved))
            .Should().Be(Dav1dNativeLayout.SettingsReservedOffset);
    }

    [Fact]
    public void The_picture_structure_puts_its_planes_strides_and_allocator_data_where_the_headers_put_them()
    {
        //Arrange & Act & Assert
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.SequenceHeader)).Should().Be(0);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.FrameHeader)).Should().Be(8);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.Data0)).Should().Be(Dav1dNativeLayout.PictureDataOffset);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.Data1)).Should().Be(Dav1dNativeLayout.PictureDataOffset + 8);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.Data2)).Should().Be(Dav1dNativeLayout.PictureDataOffset + 16);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.Stride0)).Should().Be(Dav1dNativeLayout.PictureStrideOffset);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.Stride1)).Should().Be(Dav1dNativeLayout.PictureStrideOffset + 8);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.Parameters))
            .Should().Be(Dav1dNativeLayout.PictureParametersOffset);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.Properties))
            .Should().Be(Dav1dNativeLayout.PicturePropertiesOffset);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.ContentLight)).Should().Be(120);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.MasteringDisplay)).Should().Be(128);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.Reference))
            .Should().Be(Dav1dNativeLayout.PictureReferenceOffset);
        OffsetOf<Dav1dPicture>(nameof(Dav1dPicture.AllocatorData))
            .Should().Be(Dav1dNativeLayout.PictureAllocatorDataOffset);
    }

    [Fact]
    public void The_data_structure_puts_its_packet_metadata_where_the_headers_put_it()
    {
        //Arrange & Act & Assert
        OffsetOf<Dav1dData>(nameof(Dav1dData.Data)).Should().Be(0);
        OffsetOf<Dav1dData>(nameof(Dav1dData.Size)).Should().Be(8);
        OffsetOf<Dav1dData>(nameof(Dav1dData.Reference)).Should().Be(16);
        OffsetOf<Dav1dData>(nameof(Dav1dData.Properties)).Should().Be(Dav1dNativeLayout.DataPropertiesOffset);
        OffsetOf<Dav1dDataProps>(nameof(Dav1dDataProps.Timestamp)).Should().Be(0);
        OffsetOf<Dav1dDataProps>(nameof(Dav1dDataProps.Duration)).Should().Be(8);
        OffsetOf<Dav1dDataProps>(nameof(Dav1dDataProps.Offset)).Should().Be(16);
        OffsetOf<Dav1dDataProps>(nameof(Dav1dDataProps.Size)).Should().Be(24);
        OffsetOf<Dav1dDataProps>(nameof(Dav1dDataProps.UserData)).Should().Be(32);
    }

    [Fact]
    public void The_sequence_header_fields_the_binding_reads_are_where_the_headers_put_them()
    {
        //Arrange & Act & Assert
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.Profile)).Should().Be(0);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.MaxWidth))
            .Should().Be(Dav1dNativeLayout.SequenceHeaderMaxWidthOffset);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.MaxHeight)).Should().Be(8);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.Layout))
            .Should().Be(Dav1dNativeLayout.SequenceHeaderLayoutOffset);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.ColorPrimaries)).Should().Be(16);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.TransferCharacteristics)).Should().Be(20);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.MatrixCoefficients)).Should().Be(24);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.ChromaSamplePosition)).Should().Be(28);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.HighBitDepth))
            .Should().Be(Dav1dNativeLayout.SequenceHeaderHighBitDepthOffset);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.ColorRange))
            .Should().Be(Dav1dNativeLayout.SequenceHeaderColorRangeOffset);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.SubsamplingHorizontal))
            .Should().Be(Dav1dNativeLayout.SequenceHeaderSubsamplingHorizontalOffset);
        OffsetOf<Dav1dSequenceHeader>(nameof(Dav1dSequenceHeader.FilmGrainPresent))
            .Should().Be(Dav1dNativeLayout.SequenceHeaderFilmGrainPresentOffset);
    }

    [Fact]
    public void The_frame_header_fields_the_binding_reads_are_where_the_headers_put_them()
    {
        //Arrange & Act & Assert
        OffsetOf<Dav1dFrameHeader>(nameof(Dav1dFrameHeader.FrameType))
            .Should().Be(Dav1dNativeLayout.FrameHeaderFrameTypeOffset);
        OffsetOf<Dav1dFrameHeader>(nameof(Dav1dFrameHeader.CodedWidth))
            .Should().Be(Dav1dNativeLayout.FrameHeaderWidthOffset);
        OffsetOf<Dav1dFrameHeader>(nameof(Dav1dFrameHeader.UpscaledWidth))
            .Should().Be(Dav1dNativeLayout.FrameHeaderWidthOffset + 4);
        OffsetOf<Dav1dFrameHeader>(nameof(Dav1dFrameHeader.Height))
            .Should().Be(Dav1dNativeLayout.FrameHeaderHeightOffset);
        OffsetOf<Dav1dFrameHeader>(nameof(Dav1dFrameHeader.ShowFrame))
            .Should().Be(Dav1dNativeLayout.FrameHeaderShowFrameOffset);
        OffsetOf<Dav1dFrameHeader>(nameof(Dav1dFrameHeader.RenderWidth))
            .Should().Be(Dav1dNativeLayout.FrameHeaderRenderWidthOffset);
        OffsetOf<Dav1dFrameHeader>(nameof(Dav1dFrameHeader.RenderHeight))
            .Should().Be(Dav1dNativeLayout.FrameHeaderRenderHeightOffset);
    }

    [Fact]
    public void The_pools_layout_promises_are_the_ones_dav1d_asks_its_allocator_for()
    {
        //Arrange & Act & Assert
        VideoFrameBufferDescriptor.PlaneAlignment.Should().Be(Dav1dNativeLayout.PictureAlignment);
        VideoFrameBufferDescriptor.TailPadding.Should().Be(Dav1dNativeLayout.PictureAlignment);
        VideoFrameBufferDescriptor.DimensionMultiple.Should().Be(Dav1dNativeLayout.PictureDimensionMultiple);
    }

    [Fact]
    public void Try_again_is_the_platforms_own_negated_EAGAIN()
    {
        //Arrange
        int expected = Dav1dErrorCodes.UsesMacErrnoTable ? -35 : -11;

        //Act & Assert
        Dav1dErrorCodes.TryAgain.Should().Be(expected);
        Dav1dErrorCodes.IsTryAgain(expected).Should().BeTrue();
        Dav1dErrorCodes.Describe(expected).Should().Be("EAGAIN");
        Dav1dErrorCodes.Describe(-34).Should().Be("ERANGE");
        Dav1dErrorCodes.Describe(-12).Should().Be("ENOMEM");
        Dav1dErrorCodes.Describe(-22).Should().Be("EINVAL");
        Dav1dErrorCodes.Describe(-2).Should().Be("ENOENT");
        Dav1dErrorCodes.Describe(-5).Should().Be("EIO");
    }

    private static int OffsetOf<T>(string fieldName) => Marshal.OffsetOf<T>(fieldName).ToInt32();
}
