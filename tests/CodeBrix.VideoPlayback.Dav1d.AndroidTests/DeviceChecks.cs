using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CodeBrix.Audio.Opus;
using CodeBrix.VideoPlayback.Color;
using CodeBrix.VideoPlayback.Dav1d.Decoding;
using CodeBrix.VideoPlayback.Dav1d.Tests.Internal;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;
using CodeBrix.VideoPlayback.Playback;

namespace CodeBrix.VideoPlayback.Dav1d.AndroidTests;

/// <summary>Conformance, managed/native lifetime, and playback checks through the actual NuGet package.</summary>
internal static class DeviceChecks
{
    internal static string Run(string directory, bool audio, bool audioOnly, Action<string> progress)
    {
        StringBuilder report = new StringBuilder();
        if (audioOnly)
        {
            // Isolate the published audio backend from all dav1d loads and decodes.
            progress("Starting standalone Android audio check; dav1d has not been loaded");
            CheckAudioOnly(directory, progress);
            return "PASS\nStandalone Android audio check\n";
        }
        report.AppendLine($"{RuntimeInformation.RuntimeIdentifier}; {RuntimeInformation.FrameworkDescription}");
        CodeBrixVideoPlaybackDav1d.Register();
        Require(CodeBrixVideoPlaybackDav1d.NativeVersion == "1.5.4", "Unexpected dav1d version");
        Require(CodeBrixVideoPlaybackDav1d.NativeApiVersion == "7.0.0", "Unexpected dav1d API");
        report.AppendLine($"Loaded {CodeBrixVideoPlaybackDav1d.NativeVersion}: {CodeBrixVideoPlaybackDav1d.NativeLibraryPath}");
        foreach (string line in File.ReadLines(Path.Combine(directory, "vectors", "EXPECTED.md5")))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            string[] fields = line.Split('|');
            progress("Starting " + fields[0] + " " + fields[1]);
            CheckVector(Path.Combine(directory, "vectors", fields[0]), fields[1].EndsWith('1'), fields[2]);
            progress("PASS " + fields[0] + " " + fields[1]);
            report.AppendLine($"PASS {fields[0]} {fields[1]} {fields[2]} (two decodes, flush, GC, retained frame)");
        }
        progress("Starting video-only playback");
        CheckPlayback(Path.Combine(directory, "media", "av1-video-only.webm"), false);
        report.AppendLine("PASS video-only playback, pause/seek/drain");
        progress("PASS video-only playback");
        if (!audio) return "PASS\n" + report + "Audio integration skipped by explicit request\n";
        CodeBrixAudioOpus.Register();
        foreach (string file in new[] { "av1-opus.webm", "av1-vorbis.webm" })
        {
            progress("Starting audio playback: " + file);
            CheckPlayback(Path.Combine(directory, "media", file), true);
            progress("PASS " + file);
            report.AppendLine($"PASS {file}: Android audio output (muted), pause/seek/drain");
        }
        return "PASS\n" + report;
    }

    private static void CheckAudioOnly(string directory, Action<string> progress)
    {
        using CodeBrix.Audio.Wave.WaveOutEvent output = new CodeBrix.Audio.Wave.WaveOutEvent();
        CodeBrix.Audio.Wave.BufferedWaveProvider silence = new CodeBrix.Audio.Wave.BufferedWaveProvider(
            CodeBrix.Audio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        output.Init(silence);
        output.Volume = 0;
        output.Play();
        Thread.Sleep(1500);
        output.Stop();
        progress("PASS standalone silent PCM output; starting Vorbis packet decode without dav1d");
        using CodeBrix.VideoPlayback.Containers.Matroska.MatroskaReader reader = new(
            new CodeBrix.VideoPlayback.Sources.FileMediaSource(Path.Combine(directory, "media", "av1-vorbis.webm")));
        CodeBrix.VideoPlayback.Containers.MediaTrackInfo track = reader.Tracks.Single(t => t.CodecId == VideoCodecIds.Vorbis);
        using CodeBrix.Audio.Engine.Interfaces.IPacketSoundDecoder decoder =
            CodeBrix.Audio.Wave.SharedAudioOutput.CreatePacketDecoder(track.CodecId, track.CodecPrivate);
        float[] samples = new float[decoder.MaxSamplesPerPacket];
        int count = 0;
        while (reader.TryReadPacket(out CodeBrix.VideoPlayback.Containers.MediaPacket packet))
        {
            if (packet.TrackId != track.Id) continue;
            count += decoder.DecodePacket(packet.Data.Span, samples);
        }
        Require(count > 0, "No standalone Vorbis samples");
        progress("PASS standalone Vorbis packet decode");
    }

    private static void CheckVector(string path, bool grain, string expected)
    {
        IvfStreamReader.IvfStream stream = IvfStreamReader.Read(path);
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoFrame retained = null;
        string retainedHash = null;
        try
        {
            using (Dav1dVideoDecoder decoder = (Dav1dVideoDecoder)new Dav1dDecoderFactory().CreateDecoder(
                VideoCodecIds.Av1, ReadOnlyMemory<byte>.Empty,
                new Dav1dDecoderOptions { BufferPool = pool, Threads = 4, MaxFrameDelay = 3, ApplyFilmGrain = grain }))
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    decoder.Flush();
                    using PlanarFrameHasher hasher = new PlanarFrameHasher();
                    void Consume(VideoFrame frame)
                    {
                        using (frame)
                        {
                            Require(frame.Y.Data == frame.Buffer.Y.Data && frame.U.Data == frame.Buffer.U.Data
                                && frame.V.Data == frame.Buffer.V.Data, "The frame was copied");
                            Require((frame.Y.Data.ToInt64() & 63) == 0, "Unaligned frame");
                            hasher.Add(frame);
                            if (retained == null)
                            {
                                retained = frame.Retain();
                                using PlanarFrameHasher first = new PlanarFrameHasher();
                                first.Add(frame);
                                retainedHash = first.Finish();
                                byte[] bgra = new byte[VideoFrameConverter.GetBgraBufferSize(frame.Width, frame.Height)];
                                VideoFrameConverter.ToBgra32(frame, bgra, VideoFrameConverter.GetBgraStride(frame.Width));
                                Require(bgra.Any(value => value != 0), "Empty BGRA conversion");
                            }
                        }
                    }
                    for (int index = 0; index < stream.Frames.Count; index++)
                    {
                        IvfStreamReader.IvfFrame input = stream.Frames[index];
                        VideoPacket packet = new VideoPacket(input.Data,
                            TimeSpan.FromSeconds(input.Timestamp * (double)stream.TimeBaseDenominator / stream.TimeBaseNumerator), index == 0);
                        while (!decoder.SendPacket(packet))
                        {
                            Require(decoder.TryReceiveFrame(out VideoFrame parked), "Decode made no progress");
                            Consume(parked);
                        }
                        // Native frame workers may still hold compressed input and output buffers here.
                        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                        GC.WaitForPendingFinalizers();
                        while (decoder.TryReceiveFrame(out VideoFrame produced)) Consume(produced);
                    }
                    decoder.Drain();
                    while (decoder.TryReceiveFrame(out VideoFrame drained)) Consume(drained);
                    Require(hasher.FrameCount == stream.Frames.Count, "Missing frames");
                    Require(hasher.Finish() == expected, "Conformance hash mismatch: " + Path.GetFileName(path));
                }
            }
            using PlanarFrameHasher held = new PlanarFrameHasher();
            held.Add(retained);
            Require(held.Finish() == retainedHash, "Retained frame changed after decoder disposal");
            Task.Run(retained.Dispose).GetAwaiter().GetResult();
            retained = null;
            Require(pool.GetStatistics().Live == 0, "Native or managed frame references leaked");
        }
        finally
        {
            retained?.Dispose();
        }
    }

    private static void CheckPlayback(string path, bool audio)
    {
        using VideoPlaybackSession session = new VideoPlaybackSession(new VideoPlaybackOptions { PlayAudio = audio });
        session.Volume = 0;
        int frames = 0;
        using ManualResetEventSlim ended = new ManualResetEventSlim();
        session.FrameReady += (_, _) =>
        {
            if (session.Presenter.TryTakeLatest(out VideoFrame frame))
            {
                using (frame) Interlocked.Increment(ref frames);
            }
        };
        session.PlaybackEnded += (_, _) => ended.Set();
        session.Open(path);
        Require((session.AudioTrack != null) == audio, "Unexpected audio track");
        session.Play();
        Thread.Sleep(150);
        session.Pause();
        session.Seek(TimeSpan.FromMilliseconds(500));
        Stopwatch timer = Stopwatch.StartNew();
        session.Play();
        Require(ended.Wait(TimeSpan.FromSeconds(30)), "Playback timed out: " + path);
        Require(session.State == VideoPlaybackState.Ended && frames > 0, "Playback did not present frames");
        if (audio) Require(timer.Elapsed > TimeSpan.FromMilliseconds(500), "Audio did not pace playback");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
