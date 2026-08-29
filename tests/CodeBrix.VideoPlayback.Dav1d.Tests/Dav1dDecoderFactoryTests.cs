using System;
using System.Linq;
using CodeBrix.VideoPlayback.Dav1d.Tests.Internal;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Checks the factory's identity, its manners when it is offered a codec it does not serve, and the
/// sequence-header probe that lets a host size a surface before a frame is decoded.
/// </summary>
public class Dav1dDecoderFactoryTests
{
    [Fact]
    public void The_factory_identifies_itself_as_this_package_and_serves_only_av1()
    {
        //Arrange
        Dav1dDecoderFactory factory = new Dav1dDecoderFactory();

        //Act
        string id = factory.FactoryId;
        string[] codecs = factory.SupportedCodecIds.ToArray();
        int priority = factory.Priority;

        //Assert
        id.Should().Be("CodeBrix.VideoPlayback.Dav1d");
        codecs.Length.Should().Be(1);
        codecs[0].Should().Be("av01");
        priority.Should().Be(0);
    }

    [Fact]
    public void A_codec_the_factory_does_not_serve_is_declined_rather_than_refused()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoDecoderOptions options = new VideoDecoderOptions { BufferPool = pool };

        //Act
        IVideoDecoder decoder = new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Vp9, ReadOnlyMemory<byte>.Empty, options);

        //Assert
        decoder.Should().BeNull();
    }

    [Fact]
    public void The_codec_identifier_is_matched_without_regard_to_case()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoDecoderOptions options = new VideoDecoderOptions { BufferPool = pool };

        //Act
        using IVideoDecoder decoder = new Dav1dDecoderFactory()
            .CreateDecoder("AV01", ReadOnlyMemory<byte>.Empty, options);

        //Assert
        decoder.Should().NotBeNull();
        decoder.CodecId.Should().Be("av01");
    }

    [Fact]
    public void A_decoder_without_a_buffer_pool_is_refused_with_a_message_that_says_what_to_set()
    {
        //Arrange
        VideoDecoderOptions options = new VideoDecoderOptions();
        Action act = () => new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        //Act & Assert
        act.Should().Throw<Dav1dException>().WithMessage("*BufferPool*");
    }

    [Theory]
    [InlineData("01-8bit-420-aom.ivf", 320, 180, 8, VideoPixelLayout.I420)]
    [InlineData("02-8bit-420-svtav1.ivf", 320, 180, 8, VideoPixelLayout.I420)]
    [InlineData("03-10bit-420-aom.ivf", 320, 180, 10, VideoPixelLayout.I420)]
    [InlineData("04-8bit-444-aom.ivf", 320, 180, 8, VideoPixelLayout.I444)]
    [InlineData("05-8bit-420-filmgrain-svtav1.ivf", 320, 180, 8, VideoPixelLayout.I420)]
    [InlineData("06-8bit-420-oddsize-keyframes-aom.ivf", 322, 182, 8, VideoPixelLayout.I420)]
    public void The_first_packet_of_every_stream_describes_the_stream_before_anything_is_decoded(
        string fileName,
        int width,
        int height,
        int bitDepth,
        VideoPixelLayout layout)
    {
        //Arrange
        IvfStreamReader.IvfStream stream = IvfStreamReader.Read(ConformanceVectors.PathOf(fileName));

        //Act
        bool probed = Dav1dDecoderFactory.TryProbe(stream.Frames[0].Data, out VideoStreamInfo info);

        //Assert
        probed.Should().BeTrue();
        info.IsKnown.Should().BeTrue();
        info.Width.Should().Be(width);
        info.Height.Should().Be(height);
        info.BitDepth.Should().Be(bitDepth);
        info.Layout.Should().Be(layout);
        info.MaxSampleValue.Should().Be((1 << bitDepth) - 1);
        stream.Width.Should().Be(width);
        stream.Height.Should().Be(height);
    }

    [Fact]
    public void The_probe_agrees_with_what_the_decoder_reports_once_it_has_decoded_a_frame()
    {
        //Arrange
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("03-10bit-420-aom.ivf"));
        Dav1dDecoderFactory.TryProbe(stream.Frames[0].Data, out VideoStreamInfo probed);
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();

        //Act
        VectorDecoder.RunResult result =
            VectorDecoder.Decode("03-10bit-420-aom.ivf", new Dav1dDecoderOptions(), pool, _ => { });

        //Assert
        result.StreamInfo.Width.Should().Be(probed.Width);
        result.StreamInfo.Height.Should().Be(probed.Height);
        result.StreamInfo.BitDepth.Should().Be(probed.BitDepth);
        result.StreamInfo.Layout.Should().Be(probed.Layout);
        result.StreamInfo.Color.Should().Be(probed.Color);
    }

    [Fact]
    public void The_probe_reads_the_colour_description_the_streams_actually_carry()
    {
        //Arrange
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        //Act
        Dav1dDecoderFactory.TryProbe(stream.Frames[0].Data, out VideoStreamInfo info);
        VideoColorInfo resolved = info.Color.Resolve(info.Height);

        //Assert
        // The streams were encoded without a colour description, so the sequence header says "unspecified"
        // and the library's own resolution rule applies: 180 lines is standard definition, which falls back
        // to BT.601 rather than BT.709.
        info.Color.Range.Should().Be(VideoColorRange.Limited);
        info.Color.Matrix.Should().Be(VideoMatrixCoefficients.Unspecified);
        info.Color.IsHighDynamicRange.Should().BeFalse();
        resolved.Matrix.Should().Be(VideoMatrixCoefficients.Smpte170M);
        resolved.Primaries.Should().Be(VideoColorPrimaries.Smpte170M);
        resolved.ChromaSiting.Should().Be(VideoChromaSiting.Vertical);
    }

    [Fact]
    public void Data_that_carries_no_sequence_header_is_answered_no_rather_than_refused()
    {
        //Arrange
        byte[] noise = new byte[64];
        new Random(20260829).NextBytes(noise);

        //Act
        bool probed = Dav1dDecoderFactory.TryProbe(noise, out VideoStreamInfo info);

        //Assert
        probed.Should().BeFalse();
        info.IsKnown.Should().BeFalse();
    }

    [Fact]
    public void Probing_nothing_at_all_is_answered_no()
    {
        //Act
        bool probed = Dav1dDecoderFactory.TryProbe(ReadOnlySpan<byte>.Empty, out VideoStreamInfo info);

        //Assert
        probed.Should().BeFalse();
        info.Should().BeSameAs(VideoStreamInfo.Unknown);
    }

    [Fact]
    public void An_av1c_configuration_record_is_recognised_and_its_four_byte_header_stepped_over()
    {
        //Arrange
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));
        byte[] obus = stream.Frames[0].Data;
        byte[] record = new byte[obus.Length + 4];
        record[0] = 0x81;
        record[1] = 0x00;
        record[2] = 0x00;
        record[3] = 0x00;
        obus.CopyTo(record, 4);

        //Act
        bool probed = Dav1dDecoderFactory.TryProbe(record, out VideoStreamInfo info);
        ReadOnlySpan<byte> stripped = Dav1dDecoderFactory.StripAv1ConfigurationRecord(record);

        //Assert
        probed.Should().BeTrue();
        info.Width.Should().Be(320);
        stripped.Length.Should().Be(obus.Length);
    }

    [Fact]
    public void A_packet_can_be_probed_as_well_as_a_span()
    {
        //Arrange
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("04-8bit-444-aom.ivf"));
        VideoPacket packet = new VideoPacket(stream.Frames[0].Data, TimeSpan.Zero, true);

        //Act
        bool probed = Dav1dDecoderFactory.TryProbe(packet, out VideoStreamInfo info);

        //Assert
        probed.Should().BeTrue();
        info.Layout.Should().Be(VideoPixelLayout.I444);
        info.ChromaShiftX.Should().Be(0);
        info.ChromaShiftY.Should().Be(0);
    }
}
