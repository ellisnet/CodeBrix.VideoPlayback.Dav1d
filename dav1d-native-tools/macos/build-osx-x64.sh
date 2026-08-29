#!/usr/bin/env bash
# ==============================================================================================
# build-osx-x64.sh - build libdav1d.dylib for the osx-x64 runtime identifier
# ==============================================================================================
#
#   RUN AND VERIFIED ON macOS 2026-08-29 (macOS 26.5.1, Apple clang 21.0.0, nasm 3.02, Rosetta 2).
#   Built clean on the first attempt and passed the full gate; no fix to this script was needed.
#   Takes about 24 seconds.
#
#   NOT BYTE-REPRODUCIBLE, and that is understood rather than unexplained: consecutive runs
#   alternate between two outputs that differ ONLY in the 16-byte LC_UUID and the ad-hoc signature
#   covering it. The shipped code and data are identical either way. README.txt, "WHAT HAS AND HAS
#   NOT BEEN VERIFIED", has the measurement and the cause.
#
# USAGE
#     cd dav1d-native-tools/macos
#     ./build-osx-x64.sh
#
# Runs on the SAME Apple Silicon Mac as build-osx-arm64.sh, cross-compiling with
# crossfile-x86_64.txt. An Intel Mac is not needed. The two slices are kept as two separate
# dylibs in two separate RID folders - deliberately NOT a universal binary, because the package's
# runtimes/osx-x64/ and runtimes/osx-arm64/ folders each want their own file and a fat binary
# would put both slices in both places.
#
# ROSETTA 2 IS NEEDED TO VERIFY, not to build. The gate has to RUN x86_64 code - the smoke test
# and the conformance decode - and on Apple Silicon that means Rosetta:
#     softwareupdate --install-rosetta
# Without it this script reports those checks as FAILURES rather than skipping them quietly.
#
# Output: ../output/osx-x64/
# ==============================================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=build-common.sh
. "$SCRIPT_DIR/build-common.sh"

RID=osx-x64
ARCH=x86_64
OUT="$TOOLS_DIR/output/$RID"
SCRATCH=/tmp/dav1d-src-$RID
BUILD=/tmp/dav1d-build-$RID
CROSSFILE="$SCRIPT_DIR/crossfile-x86_64.txt"
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
# nasm assembles dav1d's x86 SIMD. Without it meson does not fail - it quietly builds a C-only
# library several times slower, which defeats the purpose of using dav1d.
require_tool nasm "Install it with Homebrew:   brew install nasm   (dav1d needs $NASM_MIN_VERSION or newer)"
[ -f "$CROSSFILE" ] || { echo "ERROR: $CROSSFILE is missing; it is part of the repository." >&2; exit 1; }

CC_VERSION="$(cc --version | first_line)"
echo "  cc      : $CC_VERSION"
echo "  meson   : $(meson --version) (pinned $MESON_VERSION)"
echo "  ninja   : $(ninja --version) (pinned $NINJA_VERSION)"
echo "  nasm    : $(nasm -v | first_line)"
echo "  min macOS: $MACOS_MIN_VERSION"

# Can this machine execute x86_64 code? On Apple Silicon that needs Rosetta 2.
CAN_RUN=no
if [ "$(uname -m)" = "x86_64" ]; then
    CAN_RUN=yes
elif /usr/bin/pgrep -q oahd 2>/dev/null || [ -f /Library/Apple/usr/libexec/oah/libRosettaRuntime ]; then
    CAN_RUN=yes
fi
if [ "$CAN_RUN" = "yes" ]; then
    echo "  rosetta : x86_64 code can run on this machine - the full gate will be applied"
else
    echo "  rosetta : NOT AVAILABLE. The build will run, but the smoke test and the conformance"
    echo "            decode cannot, and will be reported as FAILURES. Install Rosetta 2 with"
    echo "            'softwareupdate --install-rosetta' and re-run, or verify this binary on an"
    echo "            Intel Mac before shipping it."
fi
echo

# ----------------------------------------------------------------------------------------------
# 2. Source + build
# ----------------------------------------------------------------------------------------------
echo "--- source ---"
copy_source_to_scratch "$SCRATCH"
echo

echo "--- building ---"
rm -rf "$BUILD"
export MACOSX_DEPLOYMENT_TARGET="$MACOS_MIN_VERSION"
# shellcheck disable=SC2086
meson setup "$BUILD" "$SCRATCH" --cross-file "$CROSSFILE" $DAV1D_MESON_OPTIONS
ninja -C "$BUILD"
echo

# ----------------------------------------------------------------------------------------------
# 3. Collect
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

dsymutil "$OUT/unstripped/$LIB_NAME" -o "$OUT/$LIB_NAME.dSYM" || \
    echo "  [warn] dsymutil failed; crash reports from this build will be harder to read."

# strip, then install name, then sign - the first two invalidate a signature.
strip -x "$OUT/$LIB_NAME"
SIZE_STRIPPED="$(stat -f %z "$OUT/$LIB_NAME")"
install_name_tool -id "@rpath/$LIB_NAME" "$OUT/$LIB_NAME"

# The linker ad-hoc signs arm64 output on its own but leaves x86_64 UNSIGNED, so this call is
# what makes the x64 slice loadable at all on a modern Mac. The gate checks it took.
codesign --force --sign - "$OUT/$LIB_NAME"

cp "$SRC_DIR/COPYING" "$OUT/LICENSE-Dav1d.txt"
echo "  unstripped: $SIZE_UNSTRIPPED bytes -> stripped: $SIZE_STRIPPED bytes"
echo "  LICENSE-Dav1d.txt   : dav1d COPYING copied beside the binary"
echo

# ----------------------------------------------------------------------------------------------
# 4. The gate
# ----------------------------------------------------------------------------------------------
echo "--- verifying ---"
verify_dylib "$OUT/$LIB_NAME" "$ARCH" "$BUILT_CLI" "$CAN_RUN"

if [ "$GATE_FAILED" -ne 0 ]; then
    echo
    if [ "$CAN_RUN" != "yes" ]; then
        echo "Rosetta 2 is not installed, so the checks that have to RUN x86_64 code could not run."
        echo "They are counted as failures on purpose: an unrun check is not a passed check."
    fi
    echo "VERIFICATION INCOMPLETE OR FAILED for $RID - see above. $OUT is left for inspection."
    exit 1
fi
echo

# ----------------------------------------------------------------------------------------------
# 5. Stage + record
# ----------------------------------------------------------------------------------------------
SHA="$(shasum -a 256 "$OUT/$LIB_NAME" | cut -d' ' -f1)"
mkdir -p "$TOOLS_DIR/output/staging/$RID"
gzip -9 -c "$OUT/$LIB_NAME" > "$TOOLS_DIR/output/staging/$RID/$LIB_NAME.gz"
ELAPSED=$(( SECONDS - START_SECONDS ))

cat > "$OUT/BUILD-INFO.txt" <<EOF
dav1d native library - build information
==============================================================================
RID              : $RID
Built            : $STARTED_AT
Build duration   : ${ELAPSED}s
Built by         : dav1d-native-tools/macos/build-osx-x64.sh
                   (cross-compiled on Apple Silicon via crossfile-x86_64.txt)

Build machine
------------------------------------------------------------------------------
macOS            : $(sw_vers -productVersion 2>/dev/null || echo unknown) ($(uname -m))
Compiler         : $CC_VERSION  (-arch x86_64)
meson            : $(meson --version) (pinned $MESON_VERSION)
ninja            : $(ninja --version) (pinned $NINJA_VERSION)
nasm             : $(nasm -v | first_line)
x86_64 execution : $CAN_RUN (Rosetta 2 - needed to RUN the gate, not to build)

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
dav1d            : $DAV1D_VERSION  ($DAV1D_DESCRIBE)
Commit           : $DAV1D_COMMIT
API version      : $DAV1D_API_VERSION
Patches applied  : $PATCHES_APPLIED

Configuration
------------------------------------------------------------------------------
meson options    : $DAV1D_MESON_OPTIONS
Cross file       : macos/crossfile-x86_64.txt
Deployment target: MACOSX_DEPLOYMENT_TARGET=$MACOS_MIN_VERSION (checked by the gate)

Result
------------------------------------------------------------------------------
File             : $LIB_NAME  (unversioned - LibraryImport("dav1d") probes this name)
Install name     : @rpath/$LIB_NAME
Size (stripped)  : $SIZE_STRIPPED bytes
Size (unstripped): $SIZE_UNSTRIPPED bytes -> unstripped/$LIB_NAME
Debug symbols    : $LIB_NAME.dSYM (not shipped)
SHA256           : $SHA
Signature        : ad-hoc (codesign --sign -) - the linker does NOT sign x86_64 output
Licence beside it: LICENSE-Dav1d.txt (a verbatim copy of dav1d's COPYING, BSD-2-Clause)

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
