using System;
using System.Collections.Generic;
using CodeBrix.VideoPlayback.Dav1d.Decoding;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;

namespace CodeBrix.VideoPlayback.Dav1d.Tests.Internal;

/// <summary>
/// Drives a dav1d decoder over one of the conformance streams and hands every frame it produces to a
/// callback.
/// </summary>
/// <remarks>
/// The loop here is the one the decoder's own documentation describes, written out in full because several
/// tests need to watch it happen rather than only see its result: offer a packet, and while the decoder says
/// it is full, pull a frame and offer THE SAME packet again; when the packet is taken, pull whatever is
/// ready; at the end of the stream, drain.
/// </remarks>
internal static class VectorDecoder
{
    /// <summary>What one run of a stream produced.</summary>
    /// <param name="Frames">How many frames came out.</param>
    /// <param name="Packets">How many packets went in.</param>
    /// <param name="BackPressureEvents">How many times the decoder answered "full, drain first".</param>
    /// <param name="Statistics">The pool's counters at the end of the run.</param>
    /// <param name="StreamInfo">What the decoder knew about the stream when the run finished.</param>
    internal sealed record RunResult(
        int Frames,
        int Packets,
        int BackPressureEvents,
        VideoFrameBufferPoolStatistics Statistics,
        VideoStreamInfo StreamInfo);

    /// <summary>Decodes a whole stream.</summary>
    /// <param name="fileName">The conformance stream's file name.</param>
    /// <param name="options">The decoder settings; the pool is supplied by this method.</param>
    /// <param name="pool">The pool to decode into.</param>
    /// <param name="onFrame">
    /// Called for every frame, in output order. The frame's reference belongs to this method, which disposes
    /// it as soon as the callback returns - a callback that wants to keep the frame must retain it.
    /// </param>
    /// <param name="applyGrainAfterwards">
    /// True to pull grained frames through the decoder's own grain pass instead of ordinary ones. Only
    /// meaningful when the decoder was told not to apply grain itself.
    /// </param>
    /// <returns>What the run produced.</returns>
    public static RunResult Decode(
        string fileName,
        VideoDecoderOptions options,
        PinnedFrameBufferPool pool,
        Action<VideoFrame> onFrame,
        bool applyGrainAfterwards = false)
    {
        IvfStreamReader.IvfStream stream = IvfStreamReader.Read(ConformanceVectors.PathOf(fileName));
        options.BufferPool = pool;

        int frames = 0;
        int backPressure = 0;

        using Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory()
            .CreateDecoder(VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty, options);

        foreach (IvfStreamReader.IvfFrame ivfFrame in stream.Frames)
        {
            VideoPacket packet = new VideoPacket(
                ivfFrame.Data,
                TimestampOf(ivfFrame.Timestamp, stream),
                frames == 0);

            while (!decoder.SendPacket(packet))
            {
                backPressure++;
                if (!Receive(decoder, applyGrainAfterwards, out VideoFrame parked))
                {
                    throw new InvalidOperationException(
                        "The decoder reported back-pressure but then produced no frame, which would be a "
                        + "deadlock: neither side can make progress.");
                }

                using (parked) onFrame(parked);
                frames++;
            }

            while (Receive(decoder, applyGrainAfterwards, out VideoFrame produced))
            {
                using (produced) onFrame(produced);
                frames++;
            }
        }

        decoder.Drain();

        while (Receive(decoder, applyGrainAfterwards, out VideoFrame drained))
        {
            using (drained) onFrame(drained);
            frames++;
        }

        return new RunResult(frames, stream.Frames.Count, backPressure, pool.GetStatistics(), decoder.Info);
    }

    /// <summary>The time base one of the conformance streams counts in.</summary>
    /// <param name="timestamp">The raw IVF timestamp.</param>
    /// <param name="stream">The stream it came from.</param>
    /// <returns>The timestamp as a duration from the start of the stream.</returns>
    public static TimeSpan TimestampOf(ulong timestamp, IvfStreamReader.IvfStream stream)
    {
        if (stream.TimeBaseNumerator == 0) return TimeSpan.Zero;

        double seconds = timestamp * (stream.TimeBaseDenominator / (double)stream.TimeBaseNumerator);
        return TimeSpan.FromSeconds(seconds);
    }

    private static bool Receive(Dav1dVideoDecoder decoder, bool applyGrain, out VideoFrame frame) =>
        applyGrain ? decoder.TryReceiveGrainedFrame(out frame) : decoder.TryReceiveFrame(out frame);
}
