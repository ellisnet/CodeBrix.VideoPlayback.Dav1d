#!/usr/bin/env bash
# ==============================================================================================
# build-osx-arm64.sh - build libdav1d.dylib for the osx-arm64 runtime identifier
# ==============================================================================================
#
#   RUN AND VERIFIED ON macOS 2026-08-29 (macOS 26.5.1, Apple clang 21.0.0). Built clean on the
#   first attempt and passed the full gate; no fix to this script was needed. Four consecutive
#   from-scratch runs produced a BYTE-IDENTICAL dylib, so this slice is reproducible.
#   Takes about 5-7 seconds.
#
# USAGE
#     cd dav1d-native-tools/macos
#     ./build-osx-arm64.sh
#
# A native build on an Apple Silicon Mac. The osx-x64 slice comes from the SAME machine via
# build-osx-x64.sh and a meson cross file; an Intel Mac is not needed.
#
# Output: ../output/osx-arm64/
# It installs nothing: anything missing is named with the command that installs it.
# ==============================================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=build-common.sh
. "$SCRIPT_DIR/build-common.sh"

RID=osx-arm64
ARCH=arm64
OUT="$TOOLS_DIR/output/$RID"
SCRATCH=/tmp/dav1d-src-$RID
BUILD=/tmp/dav1d-build-$RID
LIB_NAME=libdav1d.dylib
STARTED_AT="$(date -u '+%Y-%m-%d %H:%M:%S UTC')"
START_SECONDS="$SECONDS"

echo "=============================================================================="
echo " dav1d $DAV1D_VERSION ($DAV1D_DESCRIBE) - $RID"
echo "=============================================================================="
echo " started : $STARTED_AT"
echo " host    : $(uname -m), macOS $(sw_vers -productVersion 2>/dev/null || echo unknown)"
echo " source  : $SRC_DIR  (vendored - nothing is downloaded)"
echo

# ----------------------------------------------------------------------------------------------
# 1. Prerequisites
# ----------------------------------------------------------------------------------------------
echo "--- prerequisites ---"
check_common_prerequisites
if [ "$(uname -m)" != "arm64" ]; then
    echo "ERROR: this script builds the arm64 slice natively and must run on an Apple Silicon Mac." >&2
    echo "       This machine reports $(uname -m)." >&2
    exit 1
fi
# nasm is not needed here: it assembles x86 only. build-osx-x64.sh does need it.
CC_VERSION="$(cc --version | first_line)"
echo "  cc      : $CC_VERSION"
echo "  meson   : $(meson --version) (pinned $MESON_VERSION)"
echo "  ninja   : $(ninja --version) (pinned $NINJA_VERSION)"
echo "  min macOS: $MACOS_MIN_VERSION"
echo

# ----------------------------------------------------------------------------------------------
# 2. Source + build
# ----------------------------------------------------------------------------------------------
echo "--- source ---"
copy_source_to_scratch "$SCRATCH"
echo

echo "--- building ---"
rm -rf "$BUILD"
# MACOSX_DEPLOYMENT_TARGET is what clang stamps into the Mach-O load command. Without it the
# BUILD MACHINE's OS version goes in, and the dylib is refused by dyld on every older Mac.
export MACOSX_DEPLOYMENT_TARGET="$MACOS_MIN_VERSION"
# shellcheck disable=SC2086
meson setup "$BUILD" "$SCRATCH" $DAV1D_MESON_OPTIONS
ninja -C "$BUILD"
echo

# ----------------------------------------------------------------------------------------------
# 3. Collect. meson produces libdav1d.<api>.dylib plus an unversioned symlink; the package ships
#    ONE unversioned regular file called libdav1d.dylib, because LibraryImport("dav1d") probes
#    that name.
# ----------------------------------------------------------------------------------------------
echo "--- collecting ---"
BUILT_REAL="$(find "$BUILD/src" -name 'libdav1d*.dylib' -type f | sort | first_line)"
[ -n "$BUILT_REAL" ] || { echo "ERROR: no libdav1d dylib was produced." >&2; exit 1; }
BUILT_CLI="$BUILD/tools/dav1d"
[ -x "$BUILT_CLI" ] || { echo "ERROR: the dav1d CLI was not built; the conformance gate needs it." >&2; exit 1; }
echo "  built : $(basename "$BUILT_REAL")"

rm -rf "$OUT"
mkdir -p "$OUT/unstripped"
cp "$BUILT_REAL" "$OUT/unstripped/$LIB_NAME"
cp "$BUILT_REAL" "$OUT/$LIB_NAME"
chmod 0755 "$OUT/$LIB_NAME" "$OUT/unstripped/$LIB_NAME"
SIZE_UNSTRIPPED="$(stat -f %z "$OUT/unstripped/$LIB_NAME")"

# The .dSYM is macOS's separate debug-info bundle - the equivalent of keeping an unstripped copy,
# and what makes a crash report readable. Produced before stripping. Not shipped.
dsymutil "$OUT/unstripped/$LIB_NAME" -o "$OUT/$LIB_NAME.dSYM" || \
    echo "  [warn] dsymutil failed; crash reports from this build will be harder to read."

# Order matters: strip, then set the install name, then sign. Both stripping and
# install_name_tool invalidate a signature, so signing has to be last.
strip -x "$OUT/$LIB_NAME"
SIZE_STRIPPED="$(stat -f %z "$OUT/$LIB_NAME")"
install_name_tool -id "@rpath/$LIB_NAME" "$OUT/$LIB_NAME"

# Ad-hoc signature. Apple Silicon refuses to load an unsigned dylib outright. The linker ad-hoc
# signs arm64 output itself, but it has just been invalidated by the two commands above, so sign
# explicitly - and the gate checks that it worked.
codesign --force --sign - "$OUT/$LIB_NAME"

cp "$SRC_DIR/COPYING" "$OUT/LICENSE"
echo "  unstripped: $SIZE_UNSTRIPPED bytes -> stripped: $SIZE_STRIPPED bytes"
echo "  LICENSE   : dav1d COPYING copied beside the binary"
echo

# ----------------------------------------------------------------------------------------------
# 4. The gate
# ----------------------------------------------------------------------------------------------
echo "--- verifying ---"
verify_dylib "$OUT/$LIB_NAME" "$ARCH" "$BUILT_CLI" "yes"

if [ "$GATE_FAILED" -ne 0 ]; then
    echo
    echo "VERIFICATION FAILED for $RID. $OUT is left for inspection, but this build must not be"
    echo "adopted into the package."
    exit 1
fi
echo

# ----------------------------------------------------------------------------------------------
# 5. Stage + record
# ----------------------------------------------------------------------------------------------
SHA="$(shasum -a 256 "$OUT/$LIB_NAME" | cut -d' ' -f1)"
mkdir -p "$TOOLS_DIR/output/staging/$RID"
# gzip rather than xz: gzip is in the box on macOS and xz is not. The Linux staging folder uses
# .xz and the Windows one .zip for the same reason - each platform compresses with what it has.
gzip -9 -c "$OUT/$LIB_NAME" > "$TOOLS_DIR/output/staging/$RID/$LIB_NAME.gz"
ELAPSED=$(( SECONDS - START_SECONDS ))

cat > "$OUT/BUILD-INFO.txt" <<EOF
dav1d native library - build information
==============================================================================
RID              : $RID
Built            : $STARTED_AT
Build duration   : ${ELAPSED}s
Built by         : dav1d-native-tools/macos/build-osx-arm64.sh (native build)

Build machine
------------------------------------------------------------------------------
macOS            : $(sw_vers -productVersion 2>/dev/null || echo unknown) ($(uname -m))
Compiler         : $CC_VERSION
meson            : $(meson --version) (pinned $MESON_VERSION)
ninja            : $(ninja --version) (pinned $NINJA_VERSION)

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
dav1d            : $DAV1D_VERSION  ($DAV1D_DESCRIBE)
Commit           : $DAV1D_COMMIT
API version      : $DAV1D_API_VERSION
Patches applied  : $PATCHES_APPLIED

Configuration
------------------------------------------------------------------------------
meson options    : $DAV1D_MESON_OPTIONS
Deployment target: MACOSX_DEPLOYMENT_TARGET=$MACOS_MIN_VERSION (checked by the gate)

Result
------------------------------------------------------------------------------
File             : $LIB_NAME  (unversioned - LibraryImport("dav1d") probes this name)
Install name     : @rpath/$LIB_NAME
Size (stripped)  : $SIZE_STRIPPED bytes
Size (unstripped): $SIZE_UNSTRIPPED bytes -> unstripped/$LIB_NAME
Debug symbols    : $LIB_NAME.dSYM (not shipped)
SHA256           : $SHA
Signature        : ad-hoc (codesign --sign -)
Licence beside it: LICENSE (a verbatim copy of dav1d's COPYING, BSD-2-Clause)

Conformance (dav1d CLI built from this same source, --muxer md5)
------------------------------------------------------------------------------$CONFORMANCE_SUMMARY
EOF

echo "--- done ---"
echo "  $OUT/$LIB_NAME"
echo "  sha256 $SHA"
echo "  staged $TOOLS_DIR/output/staging/$RID/$LIB_NAME.gz"
echo "  ${ELAPSED}s"
echo
echo "To adopt this binary into the package, follow ADOPTING A BUILT BINARY in README.txt."
