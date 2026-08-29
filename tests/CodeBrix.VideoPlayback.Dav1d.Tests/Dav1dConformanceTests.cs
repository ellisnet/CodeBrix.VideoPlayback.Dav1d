using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.VideoPlayback.Dav1d.Tests.Internal;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Decodes every vendored conformance stream through the binding and checks that the pictures come out
/// byte-for-byte identical to what dav1d's own command-line decoder produces.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that says the binding is CORRECT rather than merely running. The hashes in
/// <c>EXPECTED.md5</c> were established by three independent decoders - the dav1d command-line tool built
/// from the vendored source, and FFmpeg's libdav1d and libaom-av1 decoders - and cover 8-bit and 10-bit
/// content, 4:2:0 and 4:4:4 chroma, two different encoders, an odd frame size, and film grain both applied
/// and not.
/// </para>
/// <para>
/// A mismatch here means the samples that reached managed code are not the samples dav1d decoded, which
/// would point at the allocator, the plane pointers, the strides, or the visible-versus-padded dimensions -
/// in other words, at exactly the parts of a zero-copy binding that are easy to get subtly wrong and
/// impossible to spot by looking at a picture.
/// </para>
/// </remarks>
public class Dav1dConformanceTests
{
    /// <summary>Every line of EXPECTED.md5, as xUnit test data.</summary>
    /// <returns>File name, grain flag and expected hash, one row per line of the file.</returns>
    public static TheoryData<string, bool, string> Expectations()
    {
        TheoryData<string, bool, string> data = new TheoryData<string, bool, string>();
        foreach (ConformanceVectors.Expectation expectation in ConformanceVectors.ReadExpectations())
        {
            data.Add(expectation.FileName, expectation.ApplyFilmGrain, expectation.Md5);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Expectations))]
    public void Every_conformance_stream_decodes_to_the_hash_dav1d_itself_produces(
        string fileName,
        bool applyFilmGrain,
        string expectedMd5)
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        using PlanarFrameHasher hasher = new PlanarFrameHasher();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { ApplyFilmGrain = applyFilmGrain };

        //Act
        VectorDecoder.RunResult result = VectorDecoder.Decode(fileName, options, pool, hasher.Add);
        string actual = hasher.Finish();

        //Assert
        actual.Should().Be(expectedMd5);
        result.Frames.Should().BeGreaterThan(0);
        hasher.FrameCount.Should().Be(result.Frames);
    }

    [Fact]
    public void The_film_grain_stream_really_is_hashed_differently_with_grain_and_without()
    {
        //Arrange
        List<ConformanceVectors.Expectation> grainLines = ConformanceVectors.ReadExpectations()
            .Where(expectation => expectation.FileName.Contains("filmgrain", StringComparison.Ordinal))
            .ToList();

        //Act
        List<string> hashes = grainLines.Select(line => line.Md5).Distinct(StringComparer.Ordinal).ToList();

        //Assert
        grainLines.Count.Should().Be(2);
        hashes.Count.Should().Be(2);
    }

    [Fact]
    public void Grain_applied_by_a_separate_pass_gives_the_same_picture_as_grain_applied_while_decoding()
    {
        //Arrange
        ConformanceVectors.Expectation grainOn = ConformanceVectors.ReadExpectations()
            .Single(expectation =>
                expectation.FileName.Contains("filmgrain", StringComparison.Ordinal) && expectation.ApplyFilmGrain);

        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        using PlanarFrameHasher hasher = new PlanarFrameHasher();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { ApplyFilmGrain = false };

        //Act
        VectorDecoder.Decode(grainOn.FileName, options, pool, hasher.Add, applyGrainAfterwards: true);
        string actual = hasher.Finish();

        //Assert
        actual.Should().Be(grainOn.Md5);
    }

    [Theory]
    [InlineData("01-8bit-420-aom.ivf")]
    [InlineData("02-8bit-420-svtav1.ivf")]
    [InlineData("03-10bit-420-aom.ivf")]
    [InlineData("04-8bit-444-aom.ivf")]
    [InlineData("05-8bit-420-filmgrain-svtav1.ivf")]
    [InlineData("06-8bit-420-oddsize-keyframes-aom.ivf")]
    public void Every_packet_that_goes_in_comes_back_out_as_a_frame(string fileName)
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions();

        //Act
        VectorDecoder.RunResult result = VectorDecoder.Decode(fileName, options, pool, _ => { });

        //Assert
        result.Frames.Should().Be(result.Packets);
    }

    [Fact]
    public void The_ten_bit_stream_produces_ten_bit_frames_with_samples_inside_the_ten_bit_range()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions();
        int maximumSample = 0;
        int bytesPerSample = 0;
        int maximumValueReported = 0;
        int bitDepth = 0;

        //Act
        VectorDecoder.Decode("03-10bit-420-aom.ivf", options, pool, frame =>
        {
            bitDepth = frame.BitDepth;
            maximumValueReported = frame.MaxSampleValue;
            bytesPerSample = frame.Y.BytesPerSample;
            maximumSample = Math.Max(maximumSample, LargestSample(frame));
        });

        //Assert
        bitDepth.Should().Be(10);
        bytesPerSample.Should().Be(2);
        maximumValueReported.Should().Be(1023);
        maximumSample.Should().BeGreaterThan(255);
        maximumSample.Should().BeLessThanOrEqualTo(1023);
    }

    [Fact]
    public void The_four_four_four_stream_produces_full_size_chroma_planes()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions();
        VideoPixelLayout layout = VideoPixelLayout.Unknown;
        int chromaWidth = 0;
        int lumaWidth = 0;

        //Act
        VectorDecoder.Decode("04-8bit-444-aom.ivf", options, pool, frame =>
        {
            layout = frame.Layout;
            chromaWidth = frame.U.Width;
            lumaWidth = frame.Y.Width;
        });

        //Assert
        layout.Should().Be(VideoPixelLayout.I444);
        chromaWidth.Should().Be(lumaWidth);
    }

    [Fact]
    public void The_odd_sized_stream_keeps_its_odd_dimensions_all_the_way_through()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions();
        int width = 0;
        int height = 0;
        int chromaWidth = 0;
        int chromaHeight = 0;

        //Act
        VectorDecoder.Decode("06-8bit-420-oddsize-keyframes-aom.ivf", options, pool, frame =>
        {
            width = frame.Width;
            height = frame.Height;
            chromaWidth = frame.U.Width;
            chromaHeight = frame.U.Height;
        });

        //Assert
        width.Should().Be(322);
        height.Should().Be(182);
        chromaWidth.Should().Be(161);
        chromaHeight.Should().Be(91);
    }

    private static unsafe int LargestSample(VideoFrame frame)
    {
        int largest = 0;
        byte* start = (byte*)frame.Y.Data;

        for (int row = 0; row < frame.Height; row++)
        {
            ushort* samples = (ushort*)(start + ((long)row * frame.Y.Stride));
            for (int column = 0; column < frame.Width; column++)
            {
                if (samples[column] > largest) largest = samples[column];
            }
        }

        return largest;
    }
}
