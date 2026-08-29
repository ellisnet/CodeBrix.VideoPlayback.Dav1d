using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CodeBrix.VideoPlayback.Containers;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;
using CodeBrix.VideoPlayback.Playback;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Plays whole files through a <see cref="VideoPlaybackSession" /> with the dav1d decoder registered - the
/// end-to-end case an application actually has.
/// </summary>
/// <remarks>
/// Everything below the session is real: a WebM file on disk, the Matroska reader, the demultiplexer, the
/// clock, the session's own frame-buffer pool and the mailbox presenter. The only thing these tests add is
/// the decoder, which is the whole of what this package contributes.
/// </remarks>
public class Dav1dSessionPlaybackTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>The folder the playback assets are copied into beside the test assembly.</summary>
    private static string AssetPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "assets", fileName);

    [Fact]
    public void A_webm_file_plays_from_beginning_to_end_and_the_frames_arrive()
    {
        //Arrange
        using VideoPlaybackSession session = new VideoPlaybackSession(
            new VideoPlaybackOptions { PlayAudio = false });
        CodeBrixVideoPlaybackDav1d.Register(session);

        int frameReadyCount = 0;
        int presentedCount = 0;
        TimeSpan lastTimestamp = TimeSpan.MinValue;
        bool timestampsAscend = true;
        using ManualResetEventSlim ended = new ManualResetEventSlim(false);

        session.FrameReady += (_, args) =>
        {
            Interlocked.Increment(ref frameReadyCount);
            if (args.Timestamp < lastTimestamp) timestampsAscend = false;
            lastTimestamp = args.Timestamp;

            if (session.Presenter.TryTakeLatest(out VideoFrame frame))
            {
                using (frame)
                {
                    frame.Width.Should().Be(160);
                    frame.Height.Should().Be(96);
                    frame.BitDepth.Should().Be(8);
                    frame.Layout.Should().Be(VideoPixelLayout.I420);
                    Interlocked.Increment(ref presentedCount);
                }
            }
        };

        session.PlaybackEnded += (_, _) => ended.Set();

        //Act
        session.Open(AssetPath("av1-opus.webm"));
        session.Play();
        bool finished = ended.Wait(Patience, TestContext.Current.CancellationToken);

        //Assert
        finished.Should().BeTrue();
        frameReadyCount.Should().BeGreaterThan(0);
        presentedCount.Should().BeGreaterThan(0);
        timestampsAscend.Should().BeTrue();
        session.State.Should().Be(VideoPlaybackState.Ended);
    }

    [Fact]
    public void The_session_describes_the_stream_the_way_the_probe_does()
    {
        //Arrange
        using VideoPlaybackSession session = new VideoPlaybackSession(
            new VideoPlaybackOptions { PlayAudio = false });
        CodeBrixVideoPlaybackDav1d.Register(session);

        //Act
        session.Open(AssetPath("av1-opus.webm"));
        MediaTrackInfo videoTrack = session.VideoTrack;
        bool probed = Dav1dDecoderFactory.TryProbe(videoTrack.CodecPrivate.Span, out VideoStreamInfo probedInfo);

        //Assert
        videoTrack.Should().NotBeNull();
        videoTrack.CodecId.Should().Be(VideoCodecIds.Av1);
        probed.Should().BeTrue();
        probedInfo.Width.Should().Be(160);
        probedInfo.Height.Should().Be(96);
        probedInfo.BitDepth.Should().Be(8);
        probedInfo.Layout.Should().Be(VideoPixelLayout.I420);
        session.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void A_video_only_file_plays_on_the_sessions_own_clock()
    {
        //Arrange
        using VideoPlaybackSession session = new VideoPlaybackSession(
            new VideoPlaybackOptions { PlayAudio = false });
        CodeBrixVideoPlaybackDav1d.Register(session);

        int frames = 0;
        using ManualResetEventSlim ended = new ManualResetEventSlim(false);
        session.FrameReady += (_, _) => Interlocked.Increment(ref frames);
        session.PlaybackEnded += (_, _) => ended.Set();

        //Act
        session.Open(AssetPath("av1-video-only.webm"));
        session.Play();
        bool finished = ended.Wait(Patience, TestContext.Current.CancellationToken);

        //Assert
        finished.Should().BeTrue();
        frames.Should().BeGreaterThan(0);
        session.AudioTrack.Should().BeNull();
    }

    [Fact]
    public void Seeking_lands_on_a_key_frame_and_playback_carries_on_from_there()
    {
        //Arrange
        using VideoPlaybackSession session = new VideoPlaybackSession(
            new VideoPlaybackOptions { PlayAudio = false });
        CodeBrixVideoPlaybackDav1d.Register(session);

        using ManualResetEventSlim ended = new ManualResetEventSlim(false);
        List<TimeSpan> afterSeek = new List<TimeSpan>();
        bool seeked = false;

        session.FrameReady += (_, args) =>
        {
            if (Volatile.Read(ref seeked))
            {
                lock (afterSeek) afterSeek.Add(args.Timestamp);
            }
        };

        session.PlaybackEnded += (_, _) => ended.Set();

        //Act
        session.Open(AssetPath("av1-opus.webm"));
        session.Seek(TimeSpan.FromSeconds(1));
        Volatile.Write(ref seeked, true);
        session.Play();
        bool finished = ended.Wait(Patience, TestContext.Current.CancellationToken);

        //Assert
        finished.Should().BeTrue();
        lock (afterSeek)
        {
            afterSeek.Count.Should().BeGreaterThan(0);
            afterSeek[0].Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
        }
    }

    [Fact]
    public void Without_a_decoder_the_session_says_which_package_is_missing()
    {
        //Arrange
        using VideoPlaybackSession session = new VideoPlaybackSession(
            new VideoPlaybackOptions { PlayAudio = false });

        Action act = () => session.Open(AssetPath("av1-opus.webm"));

        //Act & Assert
        // No Register call: the session has no AV1 decoder, and the point of this test is that saying so is
        // the library's job and it does say so.
        act.Should().Throw<VideoPlaybackException>().WithMessage("*av01*");
    }

    [Fact]
    public void The_sessions_own_pool_is_the_one_dav1d_decodes_into()
    {
        //Arrange
        using VideoPlaybackSession session = new VideoPlaybackSession(
            new VideoPlaybackOptions { PlayAudio = false });
        CodeBrixVideoPlaybackDav1d.Register(session);

        using ManualResetEventSlim ended = new ManualResetEventSlim(false);
        session.PlaybackEnded += (_, _) => ended.Set();

        //Act
        session.Open(AssetPath("av1-opus.webm"));
        session.Play();
        ended.Wait(Patience, TestContext.Current.CancellationToken).Should().BeTrue();

        PinnedFrameBufferPool pool = (PinnedFrameBufferPool)session.BufferPool;
        VideoFrameBufferPoolStatistics statistics = pool.GetStatistics();

        //Assert
        // Nothing but this decoder ever rents from a session's pool, so a rent count above zero is the
        // session's pool and dav1d's allocator being the same thing.
        statistics.Rents.Should().BeGreaterThan(0);
        statistics.Allocations.Should().BeLessThan(statistics.Rents);
        statistics.Generation.Should().Be(0);
    }

    [Fact]
    public void A_file_with_vorbis_audio_plays_audibly_when_the_opt_in_is_set()
    {
        //Arrange
        Assert.SkipUnless(
            string.Equals(
                Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS"),
                "1",
                StringComparison.Ordinal),
            "This test opens the sound device and plays a two-second tone. Set "
            + "CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run it; without a device the rest of the suite still "
            + "runs green, which is the point of the switch.");

        using VideoPlaybackSession session = new VideoPlaybackSession(
            new VideoPlaybackOptions { PlayAudio = true });
        CodeBrixVideoPlaybackDav1d.Register(session);

        int frames = 0;
        using ManualResetEventSlim ended = new ManualResetEventSlim(false);
        session.FrameReady += (_, _) => Interlocked.Increment(ref frames);
        session.PlaybackEnded += (_, _) => ended.Set();

        Stopwatch clock = Stopwatch.StartNew();

        //Act
        session.Open(AssetPath("av1-vorbis.webm"));
        session.Play();
        bool finished = ended.Wait(Patience, TestContext.Current.CancellationToken);
        clock.Stop();

        //Assert
        finished.Should().BeTrue();
        frames.Should().BeGreaterThan(0);
        session.AudioTrack.Should().NotBeNull();
        session.AudioTrack.CodecId.Should().Be(VideoCodecIds.Vorbis);

        // Two seconds of audio really takes about two seconds when a device is pacing it, which is the one
        // thing this test can assert that the silent variants cannot.
        clock.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(1));
    }
}
