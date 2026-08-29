================================================================================
AGENT-README: CodeBrix.VideoPlayback.Dav1d
A Guide for AI Coding Agents - CONSUMING the CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever NuGet package
================================================================================

OVERVIEW
========
This package teaches CodeBrix.VideoPlayback how to decode AV1 video. It is a
binding over dav1d, the reference software AV1 decoder, and it ships the native
dav1d libraries for all seven platforms the family supports: Windows x64 and
ARM64, macOS Intel and Apple Silicon, and Linux x64, ARM64 and RISC-V 64.

CodeBrix.VideoPlayback deliberately ships no video decoder of its own, because a
decoder carries a licence and a set of native binaries that not every
application wants. An application that plays AV1 references this package and
makes one call at start-up. Nothing else in the application ever names a decoder
type.

The decoder writes decoded frames STRAIGHT INTO the playback session's own
frame-buffer pool. There is no copy between what dav1d produces and what a
presenter uploads to the graphics device, and playback allocates no buffers once
it is warm. That is not a detail of the implementation; it is the contract, and
the test suite proves it.

Target framework: .NET 10 or later. Licence: BSD-2-Clause (dav1d's own licence;
the native libraries are built from the dav1d source vendored in this
package's repository).

INSTALLATION
============
    dotnet add package CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever

The package brings CodeBrix.VideoPlayback.MitLicenseForever with it, which in
turn brings CodeBrix.Audio.MitLicenseForever. Those three are the whole of what
a video player needs to READ and DECODE a file.

Two things this package does NOT bring, and an application usually needs:

  * A PRESENTER - something to draw the frames. Either
    CodeBrix.VideoPlayback.Skia.MitLicenseForever for a SkiaSharp application, or
    the CodeBrix.Platform video player element for a CodeBrix.Platform
    application. Without one, frames are decoded and never shown.
  * An OPUS DECODER, if the files carry Opus audio:
    CodeBrix.Audio.Opus.BsdLicenseForever. Vorbis audio needs nothing extra.

The native libraries arrive automatically. For an ordinary build they land in
runtimes/<rid>/native/ beside the application; for a runtime-specific publish
the build system copies the one that is needed. Both layouts are found.

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.VideoPlayback;            // VideoPlaybackSession, options
    using CodeBrix.VideoPlayback.Dav1d;      // the registration call and options
    using CodeBrix.VideoPlayback.Decoding;   // VideoStreamInfo, VideoCodecIds
    using CodeBrix.VideoPlayback.Frames;     // VideoFrame, if you handle frames

Everything an application touches is in CodeBrix.VideoPlayback.Dav1d. The
Interop and Decoding sub-namespaces exist for the binding's own use.

CORE API REFERENCE
==================

CodeBrixVideoPlaybackDav1d  (static)
------------------------------------
The front door. Four members matter.

    static void Register()
        Makes AV1 decoding available to every playback session in the process.
        Call it once at start-up. Safe to call again - it registers once. Safe
        from any thread.

        The native library is loaded and its API version checked HERE, so a
        missing or wrong native fails at start-up with a message naming every
        path that was looked in - not later, in the middle of opening a video.

    static void Register(VideoPlaybackSession session)
        Makes AV1 decoding available to ONE session, without touching the
        process-wide registry. For an application that plays several things at
        once with different needs, and for tests.

    static bool IsRegistered
        True once Register() has run. Register(session) does not set it, because
        it does not change the process-wide registry.

    static Dav1dDecoderFactory Factory
        The single factory instance. Hand it to
        VideoDecoders.Register or VideoPlaybackSession.RegisterDecoderFactory
        yourself if you manage the decoder list by hand.

    static string NativeVersion       // "1.5.4"
    static string NativeApiVersion    // "7.0.0"
    static string NativeLibraryPath   // where the native was actually loaded from
        Diagnostics. The first two load the native library if it is not loaded;
        the third reads null until something has.

    There is deliberately no module initializer. An initializer would run
    whenever the assembly was touched, keeping the decoder and every native
    library beside it alive through a trimmed publish even in an application
    that never plays a video.

Dav1dDecoderFactory
-------------------
    string FactoryId                       // "CodeBrix.VideoPlayback.Dav1d"
    IReadOnlyCollection<string> SupportedCodecIds  // { "av01" }
    int Priority                           // 0

    static bool TryProbe(ReadOnlySpan<byte> data, out VideoStreamInfo info)
    static bool TryProbe(VideoPacket packet, out VideoStreamInfo info)
        Reads an AV1 sequence header and describes the stream WITHOUT decoding
        anything: width, height, plane layout, bit depth, colour primaries,
        transfer, matrix, range and chroma siting. Use it to size a surface,
        choose a texture format, or decide whether to play a file at all, before
        frame one.

        Feed it a track's codec-private data (an av1C record - the four-byte
        record header is recognised and stepped over) or the first packet of the
        track; every AV1 key frame carries a sequence header. Data with no
        sequence header in it answers false rather than throwing.

Dav1dDecoderOptions : VideoDecoderOptions
-----------------------------------------
Set one of these as VideoPlaybackOptions.DecoderOptions before constructing a
session. A session given the plain VideoDecoderOptions gets dav1d's defaults for
everything below.

Inherited from VideoDecoderOptions - these are the ones most applications touch:

    int Threads             0 (default) lets dav1d count the logical cores.
                            1 to 256 otherwise.
    int MaxFrameDelay       0 (default) lets dav1d choose for throughput.
                            1 gives the first frame as soon as it is decoded,
                            which is what a short preloaded clip wants.
    bool ApplyFilmGrain     true (default). See the pitfalls below.
    long FrameSizeLimit     8192 x 8192 luma samples by default. The guard
                            against a hostile file.
    IVideoFrameBufferPool BufferPool
                            Set for you by the session. Driving a decoder
                            directly means setting it yourself.

Added by this package:

    int OperatingPoint      0 to 31, default 0. Which operating point of a
                            scalable stream to decode.
    bool AllLayers          true (default). Output every spatial layer.
    bool StrictStdCompliance
                            false (default). true refuses a stream over
                            compliance violations that do not affect decoding -
                            a tool's setting, not a player's.
    bool OutputInvisibleFrames
                            false (default). An analysis setting: with it on,
                            some pictures appear twice.
    Action<string> Logger   null (default). Receives dav1d's diagnostics. See
                            the pitfalls: the messages are printf format strings
                            and arrive unexpanded.

Dav1dException : VideoPlaybackException
---------------------------------------
    string ErrorName        The C errno name dav1d returned: "EIO", "ENOMEM",
                            "ERANGE", "ENOPROTOOPT", "EINVAL", "ENOENT".
    int ErrorCode           The raw negative value.

Catching VideoPlaybackException catches these too, so an application does not
have to know which decoder package is installed.

COMPLETE EXAMPLES
=================

Playing a file
--------------
    using CodeBrix.VideoPlayback;
    using CodeBrix.VideoPlayback.Dav1d;

    // Once, at start-up.
    CodeBrixVideoPlaybackDav1d.Register();

    VideoPlaybackSession session = new VideoPlaybackSession();
    session.FrameReady += (sender, args) =>
    {
        // Take the newest frame on the drawing thread and hand it to a presenter.
        if (session.Presenter.TryTakeLatest(out VideoFrame frame))
        {
            using (frame) { /* draw it */ }
        }
    };
    session.PlaybackEnded += (sender, args) => Console.WriteLine("done");

    session.Open("clip.webm");
    session.Play();

Sizing a surface before anything is decoded
-------------------------------------------
    session.Open("clip.webm");

    if (Dav1dDecoderFactory.TryProbe(session.VideoTrack.CodecPrivate.Span,
                                     out VideoStreamInfo info))
    {
        // info.Width, info.Height, info.BitDepth, info.Layout, info.Color,
        // info.MaxSampleValue (255, 1023 or 4095), info.ChromaShiftX/Y
        AllocateSurface(info.DisplayWidth, info.DisplayHeight);
    }

Low latency for a short clip
----------------------------
    VideoPlaybackOptions options = new VideoPlaybackOptions
    {
        DecoderOptions = new Dav1dDecoderOptions
        {
            MaxFrameDelay = 1,   // first frame out as soon as it is decoded
            Threads = 2,
        },
    };

    VideoPlaybackSession session = new VideoPlaybackSession(options);

Refusing an unreasonable file
-----------------------------
    VideoPlaybackOptions options = new VideoPlaybackOptions
    {
        DecoderOptions = new Dav1dDecoderOptions
        {
            FrameSizeLimit = 1920L * 1080L,   // nothing bigger than 1080p
        },
    };

    try
    {
        session.Open(untrustedFile);
        session.Play();
    }
    catch (Dav1dException failure) when (failure.ErrorName == "ERANGE")
    {
        // The message names the limit that was set and what the file asked for.
    }

Driving the decoder directly
----------------------------
Only for a tool that has its own packet source. A session does all of this.

    PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
    VideoDecoderOptions options = new VideoDecoderOptions { BufferPool = pool };

    using IVideoDecoder decoder = CodeBrixVideoPlaybackDav1d.Factory
        .CreateDecoder(VideoCodecIds.Av1, codecPrivate, options);

    foreach (VideoPacket packet in packets)
    {
        // FALSE means the decoder is full, not that anything failed. Pull a
        // frame and offer THE SAME packet again.
        while (!decoder.SendPacket(packet))
        {
            if (decoder.TryReceiveFrame(out VideoFrame parked))
            {
                using (parked) Handle(parked);
            }
        }

        while (decoder.TryReceiveFrame(out VideoFrame frame))
        {
            using (frame) Handle(frame);
        }
    }

    decoder.Drain();
    while (decoder.TryReceiveFrame(out VideoFrame frame))
    {
        using (frame) Handle(frame);
    }

MINIMUM VIABLE PROJECT TEMPLATE
===============================
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever" Version="*" />
        <!-- and a presenter, and CodeBrix.Audio.Opus.BsdLicenseForever for Opus audio -->
      </ItemGroup>
    </Project>

    using CodeBrix.VideoPlayback;
    using CodeBrix.VideoPlayback.Dav1d;

    CodeBrixVideoPlaybackDav1d.Register();

    using VideoPlaybackSession session = new VideoPlaybackSession();
    session.PlaybackEnded += (s, e) => Environment.Exit(0);
    session.Open(args[0]);
    session.Play();
    Console.ReadLine();

PERFORMANCE TIPS
================
* LEAVE Threads AT 0 unless you have measured otherwise. dav1d counts the
  logical cores and picks its own frame and tile parallelism, and it is better
  at that than a guess.

* MaxFrameDelay IS A TRADE, NOT A QUALITY SETTING. 1 shortens the delay between
  a packet going in and a frame coming out, at the cost of throughput, because
  frame-level parallelism has nowhere to work. Use it for short clips that must
  start instantly; leave it at 0 for long-form playback.

* FILM GRAIN COSTS REAL TIME on a slow device. Turning ApplyFilmGrain off makes
  a cleaner but less faithful picture; on a Pi-class board that may be the
  difference between smooth and not. It changes what you SEE, so it is a
  content decision, not only a performance one.

* THE FRAMES ARE ALREADY WHERE YOU WANT THEM. Do not copy a frame's planes
  before uploading them: they are 64-byte aligned, their strides are multiples
  of 64 bytes, and 10-bit and 12-bit samples are little-endian 16-bit words
  justified towards the least significant bit - which is exactly what an R8 or
  R16 texture upload wants. Copying undoes the whole point of the package.

* USE THE FENCE HOOK for asynchronous uploads. Put an IVideoFrameFence, or a
  Func<bool>, in frame.Buffer.Tag before starting an upload and dispose the
  frame as usual; the pool holds the memory back until the fence signals. It is
  the pool's slot to read and the presenter's to write - this package never
  touches it.

* PLAYBACK ALLOCATES NOTHING once it is warm. Not "very little" - nothing:
  the frame buffers, the frame objects over them, the pinned blocks packets are
  handed to dav1d in, and the binding's own per-frame bookkeeping all come from
  pools. Six hundred decoded pictures through a warm decoder touch the managed
  heap not at all, and there is a test that says so to the byte. Watch
  PinnedFrameBufferPool.GetStatistics().Allocations: in a healthy steady state
  it stops rising after the first few frames.

COMMON PITFALLS TO AVOID
========================
* SendPacket RETURNING FALSE IS NOT AN ERROR, and the packet has NOT been
  taken. It means the decoder is holding as much data as it can and wants frames
  pulled out first. Pull, then offer THE SAME packet again. Treating false as a
  failure loses a frame; treating it as success loses a frame silently, which is
  worse.

* FILM GRAIN CHANGES THE PIXELS, so it changes any checksum of the output. AV1
  film grain is synthesised by the DECODER from parameters in the bitstream,
  which means one stream has two different correct outputs - with grain and
  without. If you are comparing decoded output against a recorded hash, state
  which one you meant. (dav1d's own command-line tool defaults grain OFF when
  its muxer is md5, and ON otherwise, which has caught people out for years.)

* FrameSizeLimit REFUSES THE WHOLE STREAM, it does not scale anything down. The
  default of 8192 x 8192 luma samples is a guard against a file that claims
  enormous dimensions to make you allocate gigabytes. If you lower it, a legal
  file above the limit stops playing, with ERANGE and a message naming the
  limit you set.

* dav1d's LOG MESSAGES ARRIVE WITH THEIR printf CONVERSIONS UNEXPANDED - you
  will see "Frame size %dx%d exceeds limit %u" rather than the numbers. Expanding
  a C variadic argument list from managed code is not portable across the
  architectures this package supports, so the binding does not try. The wording
  still identifies the problem, and where a number really matters - the
  frame-size limit - the exception message states it itself.

* REGISTERING A DECODER DOES NOT DRAW ANYTHING. An application also needs a
  presenter. Frames that are decoded and never taken from the presenter are
  disposed and recycled, so the symptom of a missing presenter is a video that
  plays perfectly and shows nothing.

* DO NOT HOLD A FRAME PAST ITS Dispose. A frame is reference-counted: whoever
  obtains one owns ONE reference and must dispose it, and anyone who needs it
  to outlive that scope calls Retain() and disposes the result in turn. Reading
  a frame after its last reference has gone reads somebody else's picture,
  because both the buffer and the frame object have been recycled by then.

* A BUFFER DOES NOT COME BACK THE MOMENT YOU DISPOSE A FRAME, and that is
  correct. dav1d keeps decoded pictures alive as prediction references for later
  frames; the buffer returns to the pool when the application AND dav1d have both
  finished with it. Expect the pool's Live count to sit at several buffers
  throughout playback, and expect the last few to come back only when the
  decoder is disposed.

* THE av1C RECORD IS NOT BARE OBUs. A Matroska or .cbv track's codec-private
  data is an av1C configuration record: four bytes of its own, then the
  configuration OBUs. TryProbe recognises and steps over that header; code that
  hands the record to something else may need to skip it by hand.

WHAT THIS PACKAGE DOES NOT DO
=============================
* No hardware decoding. dav1d is a software decoder; there is no VA-API, no
  VideoToolbox, no D3D11 path here.
* No other codec. AV1 only - "av01" and nothing else. VP9, H.264 and HEVC would
  each be a separate package registering the same way.
* No encoding. dav1d is a decoder.
* No presenting, drawing or colour conversion. Frames come out as planar YUV
  with their colour metadata attached; turning that into pixels on a screen is a
  presenter's job.
* No AV1 alpha, and no HDR tone-mapping. HDR metadata is decoded and carried on
  the frame; nothing maps it down to a standard-range display.
* No container reading. That is CodeBrix.VideoPlayback's work; this package sees
  packets, never files.

WORKING EXAMPLES ON GITHUB
==========================
    https://github.com/ellisnet/CodeBrix.VideoPlayback.Dav1d

The test suite in tests/CodeBrix.VideoPlayback.Dav1d.Tests is the fullest set of
worked examples: the decode loop with back-pressure, the probe, the film-grain
options, the frame-size guard, and whole files played through a session.

QUICK REFERENCE CARD
====================
    CodeBrixVideoPlaybackDav1d.Register()            once, at start-up
    CodeBrixVideoPlaybackDav1d.Register(session)     one session only
    CodeBrixVideoPlaybackDav1d.IsRegistered          has the process-wide call run
    CodeBrixVideoPlaybackDav1d.Factory               the single factory instance
    CodeBrixVideoPlaybackDav1d.NativeVersion         "1.5.4"
    CodeBrixVideoPlaybackDav1d.NativeApiVersion      "7.0.0"
    CodeBrixVideoPlaybackDav1d.NativeLibraryPath     where the native came from

    Dav1dDecoderFactory.TryProbe(data, out info)     describe before decoding
    Dav1dDecoderFactory.FactoryId                    "CodeBrix.VideoPlayback.Dav1d"
    Dav1dDecoderFactory.SupportedCodecIds            { "av01" }

    Dav1dDecoderOptions.Threads                      0 = auto
    Dav1dDecoderOptions.MaxFrameDelay                1 = lowest latency
    Dav1dDecoderOptions.ApplyFilmGrain               true; changes the pixels
    Dav1dDecoderOptions.FrameSizeLimit               8192 x 8192 by default
    Dav1dDecoderOptions.OperatingPoint               scalable streams
    Dav1dDecoderOptions.AllLayers                    scalable streams
    Dav1dDecoderOptions.StrictStdCompliance          a tool's setting
    Dav1dDecoderOptions.OutputInvisibleFrames        an analysis setting
    Dav1dDecoderOptions.Logger                       Action<string>

    SendPacket -> false                              full; drain, re-offer
    TryReceiveFrame -> false                         nothing ready yet
    Drain then pull until false                      end of stream
    Flush                                            after a seek

    Dav1dException.ErrorName / .ErrorCode            the errno dav1d returned
================================================================================
