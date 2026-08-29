================================================================================
dav1d-native-tools/test-vectors - the conformance streams
================================================================================

WHAT THESE ARE
--------------------------------------------------------------------------------
Six small AV1 bitstreams in IVF containers, plus EXPECTED.md5, the hashes their
decoded pictures must produce. Every native build in this repository decodes all
of them with the dav1d CLI it just built and compares; a mismatch fails the
build. That check is what turns "the library compiled and loads" into "the
decoder is correct on this architecture" - and it is the check that would catch
a broken NEON or RVV path on hardware nobody has to hand.

They are read straight out of the repository. Nothing is downloaded, at build
time or ever, which is the rule this whole folder exists for.

PROVENANCE: THESE ARE NOT THIRD-PARTY CONTENT
--------------------------------------------------------------------------------
Every stream was encoded here, on 2026-08-28, from an ffmpeg lavfi SYNTHETIC
GENERATOR - testsrc2, mandelbrot - by generate-test-vectors.sh in this folder.
There is no camera footage, no sample clip, no downloaded material and nothing
anyone else holds a copyright in. The streams are original content of this
repository, covered by the repository's own licence, and they are deliberately
NOT listed in THIRD-PARTY-NOTICES.txt, because listing them there would be a
false statement about their origin.

The upstream dav1d project has a much larger conformance suite in a separate
`dav1d-test-data` repository. It is not used here and must not be: pulling it in
would put a build-time dependency on something outside this repository, which is
exactly what this tooling exists to prevent. Running that suite by hand is a
perfectly good OPTIONAL extra check; it is not part of any build.

THE SIX STREAMS
--------------------------------------------------------------------------------
Each is 320x180 (except 06), 20-24 frames, a few tens of kilobytes; 207 KB in
total, which keeps the repository small enough that nobody is tempted to skip
the conformance step.

  file                                     what it covers
  ---------------------------------------  ------------------------------------
  01-8bit-420-aom.ivf                      the baseline: 8-bit 4:2:0, libaom,
                                           24 frames of moving synthetic detail
  02-8bit-420-svtav1.ivf                   a SECOND ENCODER. SVT-AV1 chooses
                                           different coding tools from libaom,
                                           so this is not a duplicate of 01
  03-10bit-420-aom.ivf                     10-bit. dav1d's high-bit-depth code
                                           is a separate set of DSP functions
                                           from the 8-bit set - roughly half the
                                           decoder is untested without this
  04-8bit-444-aom.ivf                      4:4:4 chroma. Different prediction,
                                           loop-filter and grain paths again
  05-8bit-420-filmgrain-svtav1.ivf         carries AV1 film-grain metadata; see
                                           the film-grain note below
  06-8bit-420-oddsize-keyframes-aom.ivf    322x182 - neither dimension a multiple
                                           of 8 - with a keyframe every 8 frames.
                                           Exercises edge padding and repeated
                                           sequence-header handling

  The exact ffmpeg command behind each file is in generate-test-vectors.sh,
  which is the authoritative record: it is code, so it cannot drift from what
  was actually run the way a prose list can.

THE FILM-GRAIN NOTE (read this before touching EXPECTED.md5)
--------------------------------------------------------------------------------
AV1 film grain is synthesised by the DECODER from parameters in the bitstream,
so the same stream legitimately has two different correct outputs: with grain
applied and without. dav1d applies grain by default - EXCEPT in its CLI, where
`--muxer md5` and `--muxer xxh3` turn it off, on the reasonable grounds that a
checksum usually wants the pre-grain picture.

Depending on that default would make the gate depend on a CLI convention, so
every line of EXPECTED.md5 states `--filmgrain 0` or `--filmgrain 1` explicitly.
Stream 05 appears TWICE, once each way, and the two hashes differ - which is the
gate proving that grain synthesis really ran, rather than being quietly skipped.
The other five streams carry no grain metadata, so the flag makes no difference
to them and they are listed once, with `--filmgrain 1`.

CROSS-CHECK: TWO INDEPENDENT DECODERS AGREE
--------------------------------------------------------------------------------
The expected hashes were established twice over, on 2026-08-28:

  1. the dav1d CLI built from the vendored source by linux/build.sh:
         dav1d -i <vec> --muxer md5 -o - --filmgrain 1

  2. ffmpeg 7.1.5, through BOTH of its AV1 decoders - libdav1d and libaom-av1,
     which are separate implementations by different people:
         ffmpeg -v error -c:v libdav1d   -i <vec> -pix_fmt <fmt> -f md5 -
         ffmpeg -v error -c:v libaom-av1 -i <vec> -pix_fmt <fmt> -f md5 -
     (<fmt> = yuv420p, or yuv420p10le for 03, or yuv444p for 04)

The two md5 conventions turned out to hash exactly the same bytes: dav1d's md5
muxer walks each plane row by row, Y then U then V, little-endian 16-bit words
for high bit depth, with no stride padding, and ffmpeg's `-f md5` hashes its
rawvideo output, which is the same byte sequence. All three decoders produced
identical hashes on all six streams, so ONE hash per stream is both the expected
value and the cross-check. (ffmpeg applies film grain by default, which is why
it agrees with the `--filmgrain 1` lines.)

If a future ffmpeg disagrees with dav1d on a stream, the dav1d value stays the
expected one and the disagreement is a finding to chase, not a hash to update.

REGENERATING THE EXPECTED HASHES
--------------------------------------------------------------------------------
Only ever needed if the streams themselves are regenerated or new ones added.

  1. cd ../linux && MODE=generate-expected ./build.sh x64
     Decodes every .ivf here with the freshly built CLI, both ways, and writes
     ../output/generated-expected.md5. It compares nothing.

  2. Cross-check with a decoder that is not dav1d, exactly as above, before
     trusting the numbers. A hash produced only by the implementation under test
     is not a conformance check; it is a photograph of whatever it did.

  3. Copy the lines you want into EXPECTED.md5, keeping the comments, and say in
     the header when and how they were established.

  4. Re-run all three Linux builds. They must all pass, and their hashes must be
     identical to each other - the whole point is that x64, arm64 and riscv64
     produce the same pictures.

FILES
--------------------------------------------------------------------------------
  README.txt                  this document
  generate-test-vectors.sh    the exact ffmpeg commands, as code
  EXPECTED.md5                the hashes the gate enforces
  *.ivf                       the six streams
