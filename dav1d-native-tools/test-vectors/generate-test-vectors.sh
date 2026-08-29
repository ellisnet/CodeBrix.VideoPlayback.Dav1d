#!/usr/bin/env bash
# ==============================================================================
# generate-test-vectors.sh - (re)create the six AV1 conformance streams
# ==============================================================================
#
# THESE STREAMS ARE NOT THIRD-PARTY CONTENT. Every one is encoded here from an
# ffmpeg lavfi synthetic generator (testsrc2, mandelbrot, smptehdbars) - no
# camera footage, no sample clip, nothing downloaded, nothing anyone else holds
# a copyright in. They belong to this repository. See README.txt.
#
# YOU DO NOT NEED TO RUN THIS. The .ivf files are committed; the build's
# conformance gate reads them and EXPECTED.md5 straight out of the repository
# and never regenerates anything. This script exists so the streams can be
# recreated or extended, and so the exact command behind each file is recorded
# as code rather than as prose.
#
# RUNNING IT INVALIDATES EXPECTED.md5. Re-encoding with a different ffmpeg /
# libaom / SVT-AV1 build produces a different bitstream and therefore different
# decoded hashes. If you regenerate, you must also regenerate the expected
# hashes - see README.txt, "REGENERATING THE EXPECTED HASHES".
#
# Requires: ffmpeg with libaom-av1 and libsvtav1 encoders. Generated 2026-08-28
# with ffmpeg 7.1.5-0+deb13u1 (Debian 13).
# ==============================================================================

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

command -v ffmpeg > /dev/null 2>&1 || { echo "ERROR: ffmpeg is not on PATH." >&2; exit 1; }

# Captured into a variable rather than piped into grep -q on purpose: grep -q
# exits at the first match, ffmpeg dies of SIGPIPE, and with `set -o pipefail`
# that reads as "the check failed" even though the encoder is right there.
ENCODERS="$(ffmpeg -hide_banner -encoders 2>/dev/null)"
case "$ENCODERS" in *libaom-av1*) ;; *) echo "ERROR: this ffmpeg has no libaom-av1 encoder." >&2; exit 1;; esac
case "$ENCODERS" in *libsvtav1*)  ;; *) echo "ERROR: this ffmpeg has no libsvtav1 encoder." >&2; exit 1;; esac

FF="ffmpeg -hide_banner -loglevel error -y"

# 1. 8-bit 4:2:0, libaom, moving synthetic pattern. The baseline case.
$FF -f lavfi -i "testsrc2=size=320x180:rate=24" -frames:v 24 \
    -pix_fmt yuv420p -c:v libaom-av1 -crf 32 -cpu-used 8 -usage realtime -threads 1 \
    -f ivf 01-8bit-420-aom.ivf

# 2. 8-bit 4:2:0, SVT-AV1. A second encoder writes different tools into the
#    bitstream, so this is not a duplicate of 1. The source is the mandelbrot
#    zoom rather than a static test card: a still source encodes almost entirely
#    to skip blocks and would exercise very little of the decoder.
$FF -f lavfi -i "mandelbrot=size=320x180:rate=24" -frames:v 24 \
    -pix_fmt yuv420p -c:v libsvtav1 -crf 40 -preset 10 -threads 1 \
    -f ivf 02-8bit-420-svtav1.ivf

# 3. 10-bit 4:2:0, libaom. Exercises the 16-bit (HBD) half of the decoder,
#    which is a completely separate set of DSP functions from the 8-bit half.
$FF -f lavfi -i "testsrc2=size=320x180:rate=24" -frames:v 20 \
    -pix_fmt yuv420p10le -c:v libaom-av1 -crf 32 -cpu-used 8 -usage realtime -threads 1 \
    -f ivf 03-10bit-420-aom.ivf

# 4. 8-bit 4:4:4, libaom. Chroma at full resolution - a different subsampling
#    path through prediction, loop filter and film-grain code.
$FF -f lavfi -i "testsrc2=size=320x180:rate=24" -frames:v 20 \
    -pix_fmt yuv444p -c:v libaom-av1 -crf 32 -cpu-used 8 -usage realtime -threads 1 \
    -f ivf 04-8bit-444-aom.ivf

# 5. 8-bit 4:2:0 WITH FILM GRAIN metadata, SVT-AV1. The grain is signalled in
#    the bitstream; whether it is applied is a decoder decision, which is
#    exactly what makes this stream worth having (see README.txt).
$FF -f lavfi -i "testsrc2=size=320x180:rate=24" -frames:v 24 \
    -pix_fmt yuv420p -c:v libsvtav1 -crf 40 -preset 10 -threads 1 \
    -svtav1-params film-grain=8 \
    -f ivf 05-8bit-420-filmgrain-svtav1.ivf

# 6. 8-bit 4:2:0, 322x182 (neither dimension a multiple of 8) with a keyframe
#    every 8 frames. Exercises edge padding and repeated sequence-header /
#    keyframe handling in one file.
$FF -f lavfi -i "testsrc2=size=322x182:rate=24" -frames:v 24 \
    -pix_fmt yuv420p -c:v libaom-av1 -crf 32 -cpu-used 8 -usage realtime -threads 1 \
    -g 8 -keyint_min 8 \
    -f ivf 06-8bit-420-oddsize-keyframes-aom.ivf

echo "--- generated ---"
ls -l *.ivf
echo
echo "Total: $(du -ch *.ivf | tail -1 | cut -f1)"
echo
echo "EXPECTED.md5 is now stale. Regenerate it - see README.txt."
