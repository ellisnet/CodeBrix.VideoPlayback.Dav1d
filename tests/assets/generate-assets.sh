#!/usr/bin/env bash
# ==============================================================================
# generate-assets.sh - builds the end-to-end playback assets for the test suite
# ==============================================================================
#
# These are NOT the conformance vectors. Those live in dav1d-native-tools/test-
# vectors, they are checked against fixed md5s, and they are IVF - a bare
# bitstream in the simplest possible container. The files this script writes are
# ordinary WebM files with video AND audio in them, and they exist to answer a
# different question: does a whole VideoPlaybackSession - container, demuxer,
# clock, decoder, presenter - play a real file when the dav1d decoder is
# registered?
#
# PROVENANCE: these are not third-party content. Every file is encoded here from
# an ffmpeg lavfi SYNTHETIC GENERATOR - testsrc2 for the picture, sine for the
# sound. There is no camera footage, no music, no sample clip and nothing anyone
# else holds a copyright in. They are original content of this repository,
# covered by the repository's own licence, and they are deliberately NOT listed
# in THIRD-PARTY-NOTICES.txt, because listing them there would be a false
# statement about where they came from.
#
# The generated files are COMMITTED, so running this script is not part of any
# build and the test suite does not require ffmpeg. Re-run it only to change
# what the assets contain.
#
# Requires: ffmpeg with libaom-av1, libopus and libvorbis. On this laptop that
# is Debian 13's ffmpeg 7.1.5. See ASSETS.txt for what each file holds.
# ==============================================================================
set -euo pipefail

cd "$(dirname "$0")"

command -v ffmpeg > /dev/null 2>&1 || { echo "ERROR: ffmpeg is not on PATH." >&2; exit 1; }

ENCODERS="$(ffmpeg -hide_banner -encoders 2>/dev/null)"
for required in libaom-av1 libopus libvorbis; do
    case "$ENCODERS" in
        *"$required"*) ;;
        *) echo "ERROR: this ffmpeg has no $required encoder." >&2; exit 1;;
    esac
done

FF="ffmpeg -hide_banner -loglevel error -y"

# A short AV1 + Opus WebM. Opus is the codec the bespoke container recommends and
# the one an application is most likely to meet in a WebM file from elsewhere.
# The video is what these tests are about; the audio track is there so the file
# is a real two-track container rather than a video-only special case.
$FF -f lavfi -i "testsrc2=size=160x96:rate=12" \
    -f lavfi -i "sine=frequency=440:sample_rate=48000" \
    -frames:v 24 -shortest \
    -pix_fmt yuv420p -c:v libaom-av1 -crf 40 -cpu-used 8 -usage realtime -threads 1 \
    -c:a libopus -b:a 32k -ac 1 \
    -cues_to_front 1 \
    av1-opus.webm

# The same picture with Vorbis audio. Vorbis is decoded by CodeBrix.Audio itself,
# so the audible test can play this file without any extra codec package - which
# keeps this repository's test project down to the one dependency the package
# itself has.
$FF -f lavfi -i "testsrc2=size=160x96:rate=12" \
    -f lavfi -i "sine=frequency=330:sample_rate=48000" \
    -frames:v 24 -shortest \
    -pix_fmt yuv420p -c:v libaom-av1 -crf 40 -cpu-used 8 -usage realtime -threads 1 \
    -c:a libvorbis -b:a 64k -ac 1 \
    -cues_to_front 1 \
    av1-vorbis.webm

# A video-only WebM, for the case where a session has nothing to synchronise to
# and runs on its own monotonic clock.
$FF -f lavfi -i "testsrc2=size=160x96:rate=12" \
    -frames:v 24 \
    -pix_fmt yuv420p -c:v libaom-av1 -crf 40 -cpu-used 8 -usage realtime -threads 1 \
    -cues_to_front 1 \
    av1-video-only.webm

echo "Wrote:"
ls -l av1-opus.webm av1-vorbis.webm av1-video-only.webm
