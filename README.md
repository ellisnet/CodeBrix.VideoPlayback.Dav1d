# CodeBrix.VideoPlayback.Dav1d

AV1 video decoding for [CodeBrix.VideoPlayback](https://github.com/ellisnet/CodeBrix.VideoPlayback),
through a binding over [dav1d](https://code.videolan.org/videolan/dav1d) — the
reference software AV1 decoder — with self-built native libraries for seven
platforms.

    dotnet add package CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever

```csharp
using CodeBrix.VideoPlayback;
using CodeBrix.VideoPlayback.Dav1d;

CodeBrixVideoPlaybackDav1d.Register();      // once, at start-up

VideoPlaybackSession session = new VideoPlaybackSession();
session.Open("clip.webm");
session.Play();
```

That is the whole of the integration. CodeBrix.VideoPlayback ships no video
decoder of its own — a decoder brings a licence and a set of native binaries
that not every application wants — so an application that plays AV1 references
this package and makes one call. Nothing else in the application ever names a
decoder type.

## What is in the box

* **AV1 decoding** for WebM, Matroska and the bespoke `.cbv` container, wherever
  CodeBrix.VideoPlayback can read them.
* **Native dav1d for seven platforms**, built from the dav1d source vendored in
  this repository: Windows x64 and ARM64, macOS Intel and Apple Silicon, and
  Linux x64, ARM64 and RISC-V 64. They are found automatically, whether an
  application publishes for one runtime or for none.
* **A zero-copy frame path.** dav1d decodes straight into the playback session's
  own frame-buffer pool: no copy between the decoder's output and a graphics
  upload, and no buffer allocation at all once playback is warm. dav1d's
  allocator contract and the pool's contract are the same contract, so nothing is
  ever reformatted.
* **A sequence-header probe** — `Dav1dDecoderFactory.TryProbe` — that describes a
  stream's dimensions, layout, bit depth and colour before a single frame is
  decoded, so a host can size its surface first.
* **8, 10 and 12-bit content**, 4:2:0, 4:2:2, 4:4:4 and monochrome, with film
  grain, HDR metadata and pixel aspect ratio carried through to the frame.

## What it does not do

No hardware decoding — dav1d is a software decoder. No other codec: AV1 and
nothing else. No encoding. No drawing: frames come out as planar YUV with their
colour metadata attached, and an application also needs a **presenter** —
`CodeBrix.VideoPlayback.Skia` for a SkiaSharp application, or the CodeBrix.Platform
video player element — to put them on a screen. Files with Opus audio also need
`CodeBrix.Audio.Opus`; Vorbis needs nothing extra.

## Correctness

The binding decodes six conformance streams — 8-bit and 10-bit, 4:2:0 and 4:4:4,
two encoders, an odd frame size, film grain on and off — and the MD5 of its
output matches, byte for byte, what dav1d's own command-line decoder produces.
Those hashes were established by three independent decoders. The streams and the
hashes live in the repository and are read from there; nothing is downloaded, at
build time or test time.

## Documentation

* [`AGENT-README.txt`](AGENT-README.txt) — the full consumer guide: the API, the
  options, worked examples, and the pitfalls.
* [`MAINTAINER-README.txt`](MAINTAINER-README.txt) — the binding's design and how
  to work on this repository.
* [`EXTRAS-README.txt`](EXTRAS-README.txt) — the native build tooling, the
  conformance streams and the test assets.

## Licence

BSD 2-Clause — dav1d's own licence. See [`LICENSE`](LICENSE) and
[`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).

Copyright (c) 2026 Jeremy Ellis and contributors.
