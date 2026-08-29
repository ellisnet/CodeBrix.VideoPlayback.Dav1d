using System;
using System.Collections.Generic;
using CodeBrix.VideoPlayback.Dav1d.Decoding;
using CodeBrix.VideoPlayback.Dav1d.Tests.Internal;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Checks how the decoder behaves at its edges: when it is over-fed, when it is flushed, when it is drained,
/// when it is handed something it cannot decode, and when it is asked for something outside dav1d's limits.
/// </summary>
public class Dav1dVideoDecoderTests
{
    [Fact]
    public void Over_feeding_the_decoder_gets_a_polite_no_and_loses_nothing()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 1 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        int accepted = 0;
        bool refusedWithoutDraining = false;

        //Act
        // Feed and NEVER pull, until the decoder says it is full. Nothing is drained in this loop, so this
        // is the moment dav1d's back-pressure actually shows up.
        foreach (IvfStreamReader.IvfFrame ivfFrame in stream.Frames)
        {
            VideoPacket packet = new VideoPacket(ivfFrame.Data, TimeSpan.Zero, accepted == 0);
            if (!decoder.SendPacket(packet))
            {
                refusedWithoutDraining = true;
                break;
            }

            accepted++;
        }

        // Now drain, then carry on with the packet that was refused - the same packet, as the contract says.
        int frames = 0;
        while (decoder.TryReceiveFrame(out VideoFrame frame))
        {
            frame.Dispose();
            frames++;
        }

        for (int index = accepted; index < stream.Frames.Count; index++)
        {
            VideoPacket packet = new VideoPacket(stream.Frames[index].Data, TimeSpan.Zero, false);

            while (!decoder.SendPacket(packet))
            {
                decoder.TryReceiveFrame(out VideoFrame parked).Should().BeTrue();
                parked.Dispose();
                frames++;
            }

            while (decoder.TryReceiveFrame(out VideoFrame produced))
            {
                produced.Dispose();
                frames++;
            }
        }

        decoder.Drain();
        while (decoder.TryReceiveFrame(out VideoFrame drained))
        {
            drained.Dispose();
            frames++;
        }

        //Assert
        refusedWithoutDraining.Should().BeTrue();
        accepted.Should().BeLessThan(stream.Frames.Count);
        frames.Should().Be(stream.Frames.Count);
    }

    [Fact]
    public void A_refusal_holds_the_packet_rather_than_swallowing_it()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 1 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        int index = 0;
        while (decoder.SendPacket(new VideoPacket(stream.Frames[index].Data, TimeSpan.Zero, index == 0)))
        {
            index++;
        }

        int refusedIndex = index;
        int frames = 0;

        //Act
        // Offer THE SAME packet again after draining, as the contract says, until it is taken. If a refusal
        // had swallowed the packet, its frame would never appear and the totals below would not add up.
        while (!decoder.SendPacket(new VideoPacket(stream.Frames[refusedIndex].Data, TimeSpan.Zero, false)))
        {
            decoder.TryReceiveFrame(out VideoFrame parked).Should().BeTrue();
            parked.Dispose();
            frames++;
        }

        for (int next = refusedIndex + 1; next < stream.Frames.Count; next++)
        {
            VideoPacket packet = new VideoPacket(stream.Frames[next].Data, TimeSpan.Zero, false);

            while (!decoder.SendPacket(packet))
            {
                decoder.TryReceiveFrame(out VideoFrame parked).Should().BeTrue();
                parked.Dispose();
                frames++;
            }
        }

        decoder.Drain();
        while (decoder.TryReceiveFrame(out VideoFrame drained))
        {
            drained.Dispose();
            frames++;
        }

        //Assert
        refusedIndex.Should().BeGreaterThan(0);
        refusedIndex.Should().BeLessThan(stream.Frames.Count);
        frames.Should().Be(stream.Frames.Count);
    }

    [Fact]
    public void The_frame_delay_the_decoder_reports_is_at_least_one_frame()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions lowLatency = new Dav1dDecoderOptions
        {
            BufferPool = pool,
            Threads = 4,
            MaxFrameDelay = 1,
        };

        //Act
        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, lowLatency);

        //Assert
        decoder.FrameDelay.Should().Be(1);
        decoder.ThreadCount.Should().Be(4);
    }

    [Fact]
    public void Draining_produces_the_frames_dav1d_was_still_holding()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 4 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        // A host that takes AT MOST one frame per packet - a paced player, rather than a batch decoder - is
        // the case that makes draining matter: it never pulls hard enough to empty a frame-threaded decoder,
        // so the tail of the stream is still inside dav1d when the packets run out.
        int beforeDrain = 0;
        foreach (IvfStreamReader.IvfFrame ivfFrame in stream.Frames)
        {
            VideoPacket packet = new VideoPacket(ivfFrame.Data, TimeSpan.Zero, beforeDrain == 0);

            while (!decoder.SendPacket(packet))
            {
                decoder.TryReceiveFrame(out VideoFrame parked).Should().BeTrue();
                parked.Dispose();
                beforeDrain++;
            }

            if (decoder.TryReceiveFrame(out VideoFrame produced))
            {
                produced.Dispose();
                beforeDrain++;
            }
        }

        //Act
        decoder.Drain();
        int afterDrain = 0;
        while (decoder.TryReceiveFrame(out VideoFrame drained))
        {
            drained.Dispose();
            afterDrain++;
        }

        //Assert
        afterDrain.Should().BeGreaterThan(0);
        (beforeDrain + afterDrain).Should().Be(stream.Frames.Count);
    }

    [Fact]
    public void Flushing_throws_away_what_was_buffered_and_decoding_starts_again_at_a_key_frame()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 4 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("06-8bit-420-oddsize-keyframes-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        for (int index = 0; index < 4; index++)
        {
            decoder.SendPacket(new VideoPacket(stream.Frames[index].Data, TimeSpan.Zero, index == 0));
        }

        //Act
        decoder.Flush();
        bool anythingLeft = decoder.TryReceiveFrame(out VideoFrame stale);
        stale?.Dispose();

        // Vector 06 has a key frame every eight frames, so frame 8 is a legitimate restart point.
        decoder.SendPacket(new VideoPacket(stream.Frames[8].Data, TimeSpan.FromSeconds(1), true)).Should().BeTrue();

        int frames = 0;
        while (decoder.TryReceiveFrame(out VideoFrame frame))
        {
            frame.Dispose();
            frames++;
        }

        decoder.Drain();
        while (decoder.TryReceiveFrame(out VideoFrame drained))
        {
            drained.Dispose();
            frames++;
        }

        //Assert
        anythingLeft.Should().BeFalse();
        frames.Should().Be(1);
    }

    [Fact]
    public void A_frame_larger_than_the_limit_is_refused_with_a_message_naming_the_limit()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, FrameSizeLimit = 1024 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        Action act = () =>
        {
            foreach (IvfStreamReader.IvfFrame ivfFrame in stream.Frames)
            {
                VideoPacket packet = new VideoPacket(ivfFrame.Data, TimeSpan.Zero, false);
                while (!decoder.SendPacket(packet))
                {
                    if (!decoder.TryReceiveFrame(out VideoFrame parked)) break;
                    parked.Dispose();
                }

                while (decoder.TryReceiveFrame(out VideoFrame produced)) produced.Dispose();
            }
        };

        //Act
        Dav1dException failure = Assert.Throws<Dav1dException>(act);

        //Assert
        // 320 by 180 is 57 600 luma samples, well past the 1 024 the limit allows. dav1d answers ERANGE; the
        // binding names the limit itself, because dav1d states it only in a log message whose printf
        // conversions this binding deliberately does not expand.
        failure.ErrorName.Should().Be("ERANGE");
        failure.ErrorCode.Should().Be(-34);
        failure.Message.Should().Contain("frame-size limit");
        failure.Message.Should().Contain("The configured limit is 1024 luma samples");
        failure.Message.Should().Contain("FrameSizeLimit");
        failure.Message.Should().Contain("exceeds limit");
    }

    [Fact]
    public void Garbage_is_refused_by_name_rather_than_by_crashing()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool };
        byte[] noise = new byte[4096];
        new Random(1729).NextBytes(noise);

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        Action act = () =>
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                decoder.SendPacket(new VideoPacket(noise, TimeSpan.Zero, attempt == 0));
                while (decoder.TryReceiveFrame(out VideoFrame frame)) frame.Dispose();
            }
        };

        //Act
        Dav1dException failure = Assert.Throws<Dav1dException>(act);

        //Assert
        failure.ErrorName.Should().NotBeNullOrEmpty();
        failure.ErrorCode.Should().BeLessThan(0);
        failure.Message.Should().Contain(failure.ErrorName);
    }

    [Fact]
    public void A_stream_that_stops_in_the_middle_ends_without_a_fuss()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 2 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        int frames = 0;
        int truncatedAt = stream.Frames.Count / 2;

        Dav1dException reported = null;

        //Act
        try
        {
            for (int index = 0; index < truncatedAt; index++)
            {
                byte[] data = stream.Frames[index].Data;

                // Cut the last packet in half, which is what a file that stops mid-frame looks like.
                if (index == truncatedAt - 1) data = data[..(data.Length / 2)];

                VideoPacket packet = new VideoPacket(data, TimeSpan.Zero, index == 0);
                while (!decoder.SendPacket(packet))
                {
                    if (!decoder.TryReceiveFrame(out VideoFrame parked)) break;
                    parked.Dispose();
                    frames++;
                }

                while (decoder.TryReceiveFrame(out VideoFrame produced))
                {
                    produced.Dispose();
                    frames++;
                }
            }

            decoder.Drain();

            while (decoder.TryReceiveFrame(out VideoFrame drained))
            {
                drained.Dispose();
                frames++;
            }
        }
        catch (Dav1dException failure)
        {
            // A half a packet is not a stream, and dav1d says so. What matters is that it is REPORTED - by
            // name, with the decoder's own words - rather than crashing or quietly producing rubbish, and
            // that everything decoded before the damage came out.
            reported = failure;
        }

        //Assert
        frames.Should().BeGreaterThan(0);
        frames.Should().BeLessThan(stream.Frames.Count);
        reported.Should().NotBeNull();
        reported.ErrorName.Should().Be("EINVAL");
        reported.Message.Should().Contain("Error parsing OBU data");
    }

    [Fact]
    public void An_empty_packet_is_taken_and_changes_nothing()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool };

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        //Act
        bool taken = decoder.SendPacket(new VideoPacket(ReadOnlyMemory<byte>.Empty, TimeSpan.Zero, false));
        bool produced = decoder.TryReceiveFrame(out VideoFrame frame);
        frame?.Dispose();

        //Assert
        taken.Should().BeTrue();
        produced.Should().BeFalse();
    }

    [Theory]
    [InlineData(257)]
    [InlineData(1000)]
    public void A_thread_count_outside_dav1ds_range_is_refused_with_the_range_in_the_message(int threads)
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = threads };
        Action act = () => new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        //Act & Assert
        act.Should().Throw<Dav1dException>().WithMessage("*0 to 256 threads*");
    }

    [Fact]
    public void Using_a_disposed_decoder_says_so_rather_than_reaching_into_freed_memory()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool };
        Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);
        decoder.Dispose();

        //Act
        Action send = () => decoder.SendPacket(new VideoPacket(new byte[4], TimeSpan.Zero, false));
        Action receive = () => decoder.TryReceiveFrame(out _);
        Action flush = decoder.Flush;

        //Assert
        send.Should().Throw<ObjectDisposedException>();
        receive.Should().Throw<ObjectDisposedException>();
        flush.Should().Throw<ObjectDisposedException>();
        decoder.Dispose();
    }

    [Fact]
    public void Timestamps_travel_through_the_decoder_on_the_frames_they_belong_to()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 1 };
        List<TimeSpan> timestamps = new List<TimeSpan>();

        //Act
        VectorDecoder.Decode(
            "01-8bit-420-aom.ivf",
            options,
            pool,
            frame => timestamps.Add(frame.Timestamp));

        //Assert
        timestamps.Count.Should().BeGreaterThan(1);
        timestamps[0].Should().Be(TimeSpan.Zero);
        timestamps[1].Should().BeGreaterThan(timestamps[0]);
    }

    [Fact]
    public void Frames_are_numbered_in_the_order_they_come_out_and_the_first_one_is_a_key_frame()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool };
        List<long> numbers = new List<long>();
        bool firstIsKey = false;

        //Act
        VectorDecoder.Decode("01-8bit-420-aom.ivf", options, pool, frame =>
        {
            if (numbers.Count == 0) firstIsKey = frame.IsKeyFrame;
            numbers.Add(frame.FrameNumber);
        });

        //Assert
        firstIsKey.Should().BeTrue();
        numbers[0].Should().Be(0);
        numbers[^1].Should().Be(numbers.Count - 1);
    }

    [Fact]
    public void The_first_picture_of_a_stream_raises_the_new_sequence_flag_and_reading_it_clears_it()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool, Threads = 1 };
        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        decoder.SendPacket(new VideoPacket(stream.Frames[0].Data, TimeSpan.Zero, true)).Should().BeTrue();
        decoder.TryReceiveFrame(out VideoFrame frame).Should().BeTrue();
        frame.Dispose();

        //Act
        Interop.Dav1dEventFlags first = decoder.TakeEventFlags();
        Interop.Dav1dEventFlags second = decoder.TakeEventFlags();

        //Assert
        first.HasFlag(Interop.Dav1dEventFlags.NewSequence).Should().BeTrue();
        second.Should().Be(Interop.Dav1dEventFlags.None);
    }

    [Fact]
    public void A_decoding_failure_names_the_packet_it_happened_on()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions { BufferPool = pool };
        byte[] noise = new byte[4096];
        new Random(1729).NextBytes(noise);

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        TimeSpan timestamp = TimeSpan.FromMilliseconds(1500);
        Action act = () =>
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                decoder.SendPacket(new VideoPacket(noise, timestamp, attempt == 0));
                while (decoder.TryReceiveFrame(out VideoFrame frame)) frame.Dispose();
            }
        };

        //Act
        Dav1dException failure = Assert.Throws<Dav1dException>(act);

        //Assert
        // The timestamp travels into dav1d on the packet and comes back out attached to the error, which is
        // what turns "decoding failed" into somewhere to look. Note that dav1d only records this for
        // bitstream errors, not for its own frame-size refusal - which is why that refusal states the limit
        // itself instead.
        failure.ErrorName.Should().Be("EINVAL");
        failure.Message.Should().Contain(timestamp.ToString());
        failure.Message.Should().Contain("It was reading the packet at");
    }

    [Fact]
    public void The_logger_hears_what_dav1d_has_to_say_when_something_goes_wrong()
    {
        //Arrange
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        List<string> messages = new List<string>();
        Dav1dDecoderOptions options = new Dav1dDecoderOptions
        {
            BufferPool = pool,
            FrameSizeLimit = 1024,
            Logger = messages.Add,
        };

        IvfStreamReader.IvfStream stream =
            IvfStreamReader.Read(ConformanceVectors.PathOf("01-8bit-420-aom.ivf"));

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        //Act
        try
        {
            decoder.SendPacket(new VideoPacket(stream.Frames[0].Data, TimeSpan.Zero, true));
        }
        catch (Dav1dException)
        {
            // The refusal is the point of the test above; here it is only how a message gets logged.
        }

        //Assert
        messages.Should().NotBeNullOrEmpty();
        decoder.LastLogMessage.Should().Contain("exceeds limit");
    }
}
