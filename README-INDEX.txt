================================================================================
README-INDEX: CodeBrix.VideoPlayback.Dav1d
Map of the README files in this repository
================================================================================

If you are an AI coding agent: find the NuGet package you are consuming below
and read its AGENT-README file in full. Read MAINTAINER-README.txt only if you
are changing this repository itself.

AGENT-README FILES (consumer documentation, one per NuGet package)
------------------------------------------------------------------
  AGENT-README.txt
      CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever - AV1 video decoding for
      CodeBrix.VideoPlayback, through a binding over the dav1d decoder, with
      self-built native libraries for Windows x64 and ARM64, macOS Intel and
      Apple Silicon, and Linux x64, ARM64 and RISC-V 64. Covers the one
      Register() call an application makes, the decoder options, the
      sequence-header probe that describes a stream before anything is decoded,
      and the pitfalls - back-pressure, film grain and checksums, the frame-size
      guard, and the fact that a decoder is not a presenter.

MAINTAINER AND EXTRAS
---------------------
  MAINTAINER-README.txt
      Building, testing, packaging and versioning this repository; the binding's
      design - the picture allocator, the stacked reference counts, the
      back-pressure loop, and the structure layouts pinned to the vendored
      headers; the provenance of the vendored dav1d source; and what remains to
      be verified on which devices. It opens with the one thing that must happen
      before this package is ever published.
  EXTRAS-README.txt
      The three folders that never ship: dav1d-native-tools (everything needed to
      build the seven native libraries, self-contained), its test-vectors (the
      six conformance streams and their expected hashes, used by both the native
      builds and the managed suite), and tests/assets (the WebM files the
      end-to-end tests play).

GENERAL
-------
  README.md
      Human-facing overview shown on GitHub and nuget.org.
  README-INDEX.txt
      This file.

ALSO WORTH READING, IN PLACE
----------------------------
  dav1d-native-tools/README.txt
      The self-contained-build rule, the folder layout, and which platform
      README to read for the machine you are on.
  dav1d-native-tools/BUILD-PROVENANCE.txt
      How each committed native library was actually built.
  dav1d-native-tools/test-vectors/README.txt
      Where the six conformance streams came from, what each one covers, and the
      film-grain note to read before touching EXPECTED.md5.
  tests/assets/ASSETS.txt
      The end-to-end playback files, and the opt-in switch for the audible test.
================================================================================
