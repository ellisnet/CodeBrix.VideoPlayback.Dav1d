# CodeBrix.VideoPlayback.Dav1d

AV1 video decoding for CodeBrix.VideoPlayback, for applications that play AV1 video files.
CodeBrix.VideoPlayback.Dav1d binds the dav1d AV1 decoder, and is provided as a .NET 10 library and associated `CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever` NuGet package.

CodeBrix.VideoPlayback.Dav1d supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.VideoPlayback.Dav1d`:

* NuGet package ID: `CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever`
* Assembly and primary namespace: `CodeBrix.VideoPlayback.Dav1d` - i.e. `using CodeBrix.VideoPlayback.Dav1d;`

XML documentation (IntelliSense) ships alongside the assembly.

The package pulls in the following automatically; no version pinning is needed in the consuming project:

* `CodeBrix.VideoPlayback.MitLicenseForever` - the playback session, the container readers and the frame-buffer pool. It brings the platform-neutral `CodeBrix.Audio.Core.MitLicenseForever` audio contracts.

The native decoder libraries for all nine supported runtime identifiers travel inside this package, including Android 13/API 33 and newer on ARM64 and x64. There is no separate native-asset package to add.

An application supplies the following as needed:

* A presenter to draw the decoded frames. Desktop applications can use `CodeBrix.VideoPlayback.Skia.MitLicenseForever` or the CodeBrix.Platform video player element. Android applications supply an Android-compatible presenter; the Skia and Authoring companions are outside this package's Android support.
* An audio output backend: `CodeBrix.Audio.MitLicenseForever` on Windows, Linux or macOS, or `CodeBrix.Audio.Android.ApacheLicenseForever` on Android. Video-only playback does not require an audio backend.
* `CodeBrix.Audio.Opus.BsdLicenseForever`, when the files carry Opus audio. Vorbis decoding is already provided by Audio.Core.

## CodeBrix.VideoPlayback.Dav1d supports:

* AV1 decoding for WebM, Matroska and the `.cbv` container, wherever CodeBrix.VideoPlayback can read them
* Native decoder libraries for nine runtime identifiers - Windows x64 and ARM64, macOS Intel and Apple Silicon, Linux x64, ARM64 and RISC-V 64, and Android 13+ x64 and ARM64 - found automatically
* A zero-copy frame path: the decoder writes decoded pictures straight into the playback session's own frame-buffer pool, so there is no copy between the decoder's output and a graphics upload, and no buffer allocation at all once playback is warm
* A sequence-header probe - `Dav1dDecoderFactory.TryProbe` - describing a stream's dimensions, layout, bit depth and colour before a single frame is decoded, so a host can size its surface first
* 8, 10 and 12-bit content, in 4:2:0, 4:2:2, 4:4:4 and monochrome, with film grain synthesised by the decoder and HDR metadata carried through to the frame
* Decoded output checked against AV1 conformance streams covering 8-bit and 10-bit, 4:2:0 and 4:4:4, an odd frame size, and film grain both applied and not
* One registration call - `CodeBrixVideoPlaybackDav1d.Register()` - after which nothing else in an application ever names a decoder type

## What this package does not do

No hardware decoding: this is a software decoder, with no VA-API, VideoToolbox or D3D11 path. No other codec - AV1 and nothing else. No encoding. No drawing or colour conversion: frames come out as planar YUV with their colour metadata attached, so an application also needs a presenter to put them on a screen. No container reading either - that is CodeBrix.VideoPlayback's work; this package sees packets, never files.

## Sample Code

### Register the decoder and play a file

```csharp
using CodeBrix.VideoPlayback;
using CodeBrix.VideoPlayback.Dav1d;

CodeBrixVideoPlaybackDav1d.Register();      // once, at start-up

VideoPlaybackSession session = new VideoPlaybackSession();
session.Open("clip.webm");
session.Play();
```

That is the whole of the integration. CodeBrix.VideoPlayback ships no video decoder of its own - a decoder brings a licence and a set of native binaries that not every application wants - so an application that plays AV1 references this package and makes one call.

### Android

Use a .NET 10 Android application with minimum Android version 33 and runtime identifiers `android-arm64` and/or `android-x64`. NuGet and the .NET Android SDK package `libdav1d.so` into the selected APK ABIs (`arm64-v8a` and `x86_64`). No manual native-library copy is needed. The libraries target API 33 using NDK r30 and have 16 KB ELF load alignment for newer Android devices.

For audio, add `CodeBrix.Audio.Android.ApacheLicenseForever` and initialize its backend before opening any playback session:

```csharp
CodeBrix.Audio.Android.CodeBrixAndroidAudio.Initialize(applicationContext);
CodeBrixVideoPlaybackDav1d.Register();
```

An Opus track additionally needs `CodeBrix.Audio.Opus.BsdLicenseForever` and `CodeBrix.Audio.Opus.CodeBrixAudioOpus.Register()`. The application supplies the visual presenter.

Decoder checks and AV1 + Opus playback pass on Android 13 ARM64 and x64 in Debug and Release. Release Vorbis playback requires the signed-offset fix in Audio.Core; full playback with that local fix also passes on both devices. The fix must be published and selected by the consuming application. See the [Android validation notes](https://github.com/ellisnet/CodeBrix.VideoPlayback.Dav1d/blob/main/tests/CodeBrix.VideoPlayback.Dav1d.AndroidTests/README.txt) for dependency versions and reproduction steps.

### Describe a stream before anything is decoded

```csharp
using CodeBrix.VideoPlayback;
using CodeBrix.VideoPlayback.Dav1d;
using CodeBrix.VideoPlayback.Decoding;

session.Open("clip.webm");

if (Dav1dDecoderFactory.TryProbe(session.VideoTrack.CodecPrivate.Span, out VideoStreamInfo info))
{
    // info.Width, info.Height, info.BitDepth, info.Layout, info.Color,
    // info.MaxSampleValue (255, 1023 or 4095), info.ChromaShiftX/Y
    AllocateSurface(info.Width, info.Height);
}
```

The probe reads a sequence header, so it reports the stream's CODED picture size. A render size that differs from it - the size a player would scale to - is carried on the decoded frames, and only appears once frames arrive.

### Lowest latency for a short clip

```csharp
using CodeBrix.VideoPlayback;
using CodeBrix.VideoPlayback.Dav1d;

VideoPlaybackOptions options = new VideoPlaybackOptions
{
    DecoderOptions = new Dav1dDecoderOptions
    {
        MaxFrameDelay = 1,   // first frame out as soon as it is decoded
        Threads = 2,
    },
};

VideoPlaybackSession session = new VideoPlaybackSession(options);
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

This package decodes AV1 and nothing else. Opening files, demuxing, timing, audio and presenting frames belong to `CodeBrix.VideoPlayback.MitLicenseForever`; read that package's own `AGENT-README.txt` for the session, container and presenter model.

Additional sample code and usage examples are available in the `CodeBrix.VideoPlayback.Dav1d.Tests` project:
https://github.com/ellisnet/CodeBrix.VideoPlayback.Dav1d/tree/main/tests/CodeBrix.VideoPlayback.Dav1d.Tests

## License

CodeBrix.VideoPlayback.Dav1d is licensed under the BSD 2-Clause License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.VideoPlayback.Dav1d/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.VideoPlayback.Dav1d/blob/main/THIRD-PARTY-NOTICES.txt).
