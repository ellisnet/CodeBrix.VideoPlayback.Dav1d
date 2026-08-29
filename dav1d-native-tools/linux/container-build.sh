#!/usr/bin/env bash
# ==============================================================================================
# container-build.sh - builds AND verifies libdav1d.so INSIDE the build container
# ==============================================================================================
#
# Do not run this on your workstation. build.sh runs it inside the derived image
# (codebrix-dav1d-build-<arch>) for the requested architecture, WITH THE NETWORK DISABLED:
#
#     podman run --network none ... codebrix-dav1d-build-<arch> bash /work/linux/container-build.sh
#
# That is not decoration. It is the mechanical proof of the rule this folder exists for: if any
# step ever tried to fetch a source file, a test vector or a dependency, it would fail here
# instead of quietly working on the machine that happened to have a network. Step 0 below prints
# the proof into the build log.
#
# It expects:
#   /work            dav1d-native-tools/, mounted read-write (only output/ is written)
#   $TARGET_RID      linux-x64 | linux-arm64 | linux-riscv64
#   the pins         passed through the environment by build.sh, which sources pins.env
#
# Everything it compiles is already in the repository: /work/dav1d is the vendored upstream
# snapshot, /work/test-vectors holds the conformance streams. There is nothing to download.
# ==============================================================================================

set -euo pipefail

# A failure inside this script used to be able to end the build with no message at all - see
# the note on `head -1` below. This trap guarantees a line is printed no matter what fails.
trap 'rc=$?; echo "ERROR: container-build.sh failed (exit $rc) at line $LINENO: $BASH_COMMAND" >&2' ERR

# ----------------------------------------------------------------------------------------------
# first_line: the first line of stdin, WITHOUT the `head -1` pitfall.
#
# `some_command | head -1` inside $(...) is a trap under `set -o pipefail`: head exits as soon as
# it has its line, the writer gets SIGPIPE, the pipeline reports 141, and `set -e` kills the
# script - silently, and only sometimes, because whether it happens depends on whether the
# writer's output fitted in the pipe buffer before head left. It passed on native x86_64 and
# killed both emulated builds stone dead, with no error message, on 2026-08-28.
# `sed -n 1p` reads its input to the end, so the writer never sees SIGPIPE.
# ----------------------------------------------------------------------------------------------
first_line() { sed -n '1p'; }

WORK=/work
SRC="$WORK/${DAV1D_DIR:-dav1d}"
PATCHES="$WORK/patches"
SCRATCH=/tmp/dav1d-src
BUILD=/tmp/dav1d-build
OUT="$WORK/output/$TARGET_RID"
LIB_NAME=libdav1d.so
STARTED_AT="$(date -u '+%Y-%m-%d %H:%M:%S UTC')"
START_SECONDS="$SECONDS"

MODE="${MODE:-build}"                       # build | generate-expected
VECTOR_DIR="$WORK/${TEST_VECTOR_DIR:-test-vectors}"
EXPECTED_FILE="$WORK/${TEST_VECTOR_EXPECTED:-test-vectors/EXPECTED.md5}"

echo "=============================================================================="
echo " dav1d $DAV1D_VERSION ($DAV1D_DESCRIBE) - $TARGET_RID"
echo "=============================================================================="
echo " started   : $STARTED_AT"
echo " container : $(grep PRETTY_NAME /etc/os-release 2>/dev/null | first_line | cut -d= -f2- | tr -d '"')"
echo " image     : ${DERIVED_IMAGE:-unknown}"
echo " base      : ${BASE_IMAGE_REF:-unknown}"
echo " arch      : $(uname -m)"
echo " mode      : $MODE"
echo

# ----------------------------------------------------------------------------------------------
# 0. Prove there is no network. With --network none the container has only the loopback
#    interface, so this is a fact about the sandbox, not a promise about the script.
# ----------------------------------------------------------------------------------------------
IFACES="$(ls /sys/class/net 2>/dev/null | tr '\n' ' ' | sed 's/ *$//')"
echo "--- network ---"
echo "  interfaces: ${IFACES:-none}"
if [ "$IFACES" = "lo" ] || [ -z "$IFACES" ]; then
    echo "  [ok] no external network interface - this build cannot reach outside the repository"
else
    echo "  [warn] this container HAS a network interface ($IFACES)."
    echo "         The build still fetches nothing, but the proof is weaker. build.sh normally"
    echo "         passes --network none; something overrode it."
fi
echo

# ----------------------------------------------------------------------------------------------
# 1. Toolchain. Everything here was installed by Containerfile.<arch> when the derived image was
#    built - the one documented, network-using step. Nothing is installed now.
# ----------------------------------------------------------------------------------------------
echo "--- toolchain ---"
for t in cc meson ninja; do
    command -v "$t" > /dev/null 2>&1 || { echo "ERROR: $t is missing from this image. Rebuild it: FORCE_IMAGE_REBUILD=1 ./build.sh" >&2; exit 1; }
done
CC_VERSION="$(cc --version | first_line)"
AS_VERSION="$(as --version | first_line)"
MESON_ACTUAL="$(meson --version)"
NINJA_ACTUAL="$(ninja --version)"
GLIBC_ACTUAL="$(ldd --version | first_line)"
if command -v nasm > /dev/null 2>&1; then NASM_ACTUAL="$(nasm -v | first_line)"; else NASM_ACTUAL="not installed (not needed on this architecture)"; fi
echo "  cc      : $CC_VERSION"
echo "  as      : $AS_VERSION"
echo "  meson   : $MESON_ACTUAL (pinned $MESON_VERSION)"
echo "  ninja   : $NINJA_ACTUAL (pinned $NINJA_VERSION)"
echo "  nasm    : $NASM_ACTUAL"
echo "  glibc   : $GLIBC_ACTUAL"
echo

# ----------------------------------------------------------------------------------------------
# 2. Source. The vendored tree is copied to scratch and built THERE, so /work/dav1d is never
#    written to and stays a verifiable, unmodified upstream snapshot. Patches (there are none
#    today) are applied to the copy.
# ----------------------------------------------------------------------------------------------
echo "--- source ---"
[ -f "$SRC/meson.build" ] || { echo "ERROR: no vendored dav1d source at $SRC" >&2; exit 1; }
rm -rf "$SCRATCH" "$BUILD"
cp -a "$SRC" "$SCRATCH"
echo "  copied $SRC -> $SCRATCH"

PATCHES_APPLIED="none"
if [ -d "$PATCHES" ] && ls "$PATCHES"/*.patch > /dev/null 2>&1; then
    PATCHES_APPLIED=""
    for p in "$PATCHES"/*.patch; do
        echo "  applying $(basename "$p")"
        ( cd "$SCRATCH" && patch -p1 --forward --batch < "$p" ) \
            || { echo "ERROR: patch $(basename "$p") did not apply. Fix it; patches are never applied best-effort." >&2; exit 1; }
        PATCHES_APPLIED="$PATCHES_APPLIED $(basename "$p")"
    done
    PATCHES_APPLIED="${PATCHES_APPLIED# }"
fi
echo "  patches applied: $PATCHES_APPLIED"
echo

# ----------------------------------------------------------------------------------------------
# 3. Build. The options come from pins.env so a build log and the pins can never disagree.
# ----------------------------------------------------------------------------------------------
echo "--- configuring ---"
echo "  meson setup $DAV1D_MESON_OPTIONS"
# shellcheck disable=SC2086
meson setup "$BUILD" "$SCRATCH" $DAV1D_MESON_OPTIONS

echo
echo "--- building ---"
ninja -C "$BUILD" -j "$(nproc)"
echo

# ----------------------------------------------------------------------------------------------
# 4. Collect. meson produces libdav1d.so.<api> plus the versioned real file and an unversioned
#    symlink. The package ships ONE unversioned regular file called libdav1d.so, because
#    LibraryImport("dav1d") on .NET probes for exactly that name and does not follow sonames.
#    The soname stays inside the file, and is recorded.
# ----------------------------------------------------------------------------------------------
BUILT_REAL="$(find "$BUILD/src" -name 'libdav1d.so.*' -type f | sort | first_line)"
[ -n "$BUILT_REAL" ] || { echo "ERROR: no libdav1d.so.* was produced." >&2; exit 1; }
BUILT_CLI="$BUILD/tools/dav1d"
[ -x "$BUILT_CLI" ] || { echo "ERROR: the dav1d CLI was not built; the conformance gate needs it." >&2; exit 1; }

SONAME="$(objdump -p "$BUILT_REAL" | awk '/SONAME/ {print $2}')"
echo "--- collecting ---"
echo "  built    : $(basename "$BUILT_REAL")"
echo "  soname   : $SONAME"
echo "  cli      : $BUILT_CLI ($("$BUILT_CLI" --version 2>&1 | first_line))"

rm -rf "$OUT"
mkdir -p "$OUT/unstripped"

# The unstripped copy is kept for crash triage: a stripped .so gives useless backtraces.
cp "$BUILT_REAL" "$OUT/unstripped/$LIB_NAME"
cp "$BUILT_REAL" "$OUT/$LIB_NAME"
chmod 0755 "$OUT/$LIB_NAME" "$OUT/unstripped/$LIB_NAME"

SIZE_UNSTRIPPED="$(stat -c %s "$OUT/unstripped/$LIB_NAME")"
strip --strip-unneeded "$OUT/$LIB_NAME"
SIZE_STRIPPED="$(stat -c %s "$OUT/$LIB_NAME")"
echo "  unstripped: $SIZE_UNSTRIPPED bytes -> stripped: $SIZE_STRIPPED bytes"

# BSD-2-Clause clause 2 - "reproduce the above copyright notice ... in the documentation and/or
# other materials provided with the distribution" - is satisfied by shipping dav1d's own COPYING
# beside every binary. This is the step that puts it there; the package's runtimes/<rid>/native/
# folder is a straight copy of this directory.
cp "$SRC/COPYING" "$OUT/LICENSE-Dav1d.txt"
echo "  LICENSE-Dav1d.txt   : dav1d COPYING copied beside the binary"
echo

# ----------------------------------------------------------------------------------------------
# 5. THE GATE. A build that fails any check does not get staged, and this script exits non-zero.
# ----------------------------------------------------------------------------------------------
echo "--- verifying ---"
FAILED=0
fail() { echo "  [FAIL] $1"; FAILED=1; }
pass() { echo "  [ok] $1"; }

LIB="$OUT/$LIB_NAME"

# 5a. Architecture. Catches the wrong file being published under the wrong RID.
FILE_OUT="$(file -b "$LIB")"
case "$TARGET_RID" in
    linux-x64)     WANT_ARCH="x86-64" ;;
    linux-arm64)   WANT_ARCH="ARM aarch64" ;;
    linux-riscv64) WANT_ARCH="UCB RISC-V" ;;
    *)             WANT_ARCH="" ;;
esac
case "$FILE_OUT" in
    *"$WANT_ARCH"*) pass "architecture: $FILE_OUT" ;;
    *)              fail "architecture mismatch: expected '$WANT_ARCH', file says: $FILE_OUT" ;;
esac

# 5b. Required exports - the 17 entry points the managed binding declares. A missing one is a
#     run-time crash in the field.
REQUIRED_SYMBOLS="
dav1d_version
dav1d_version_api
dav1d_default_settings
dav1d_open
dav1d_parse_sequence_header
dav1d_send_data
dav1d_get_picture
dav1d_apply_grain
dav1d_flush
dav1d_close
dav1d_get_event_flags
dav1d_get_decode_error_data_props
dav1d_get_frame_delay
dav1d_data_wrap
dav1d_data_create
dav1d_data_unref
dav1d_picture_unref
"
EXPORTS="$(nm -D --defined-only "$LIB" | awk '{print $3}')"
MISSING=""
for sym in $REQUIRED_SYMBOLS; do
    printf '%s\n' "$EXPORTS" | grep -x "$sym" > /dev/null || MISSING="$MISSING $sym"
done
if [ -n "$MISSING" ]; then
    fail "missing exports:$MISSING"
else
    pass "all $(printf '%s\n' "$REQUIRED_SYMBOLS" | grep -c .) required symbols exported"
fi

# 5c. No dangling symbols, and only universal system libraries as dependencies. Anything else
#     would mean the package demands a library be installed on the user's machine.
LDD_OUT="$(ldd -r "$LIB" 2>&1 || true)"
UNDEFINED="$(printf '%s\n' "$LDD_OUT" | grep -i 'undefined symbol' || true)"
if [ -n "$UNDEFINED" ]; then
    fail "ldd -r reports undefined symbols:"
    printf '%s\n' "$UNDEFINED" | sed 's/^/         /'
else
    pass "ldd -r: no undefined symbols"
fi

DEPS="$(objdump -p "$LIB" | awk '/NEEDED/ {print $2}' | sort)"
UNEXPECTED=""
for d in $DEPS; do
    case " $ALLOWED_DEPS " in
        *" $d "*) ;;
        *) UNEXPECTED="$UNEXPECTED $d" ;;
    esac
done
if [ -n "$UNEXPECTED" ]; then
    fail "unexpected dynamic dependencies:$UNEXPECTED (allowed: $ALLOWED_DEPS)"
else
    pass "dependencies are system-only: $(printf '%s ' $DEPS)"
fi

# 5d. glibc floor - the oldest system this binary can load on. CHECKED, not merely reported:
#     a build that drifted onto a newer base image would otherwise silently drop older distros.
GLIBC_FLOOR="$(objdump -T "$LIB" | grep -oE 'GLIBC_[0-9]+\.[0-9]+' | sed 's/GLIBC_//' | sort -V -u | tail -1)"
GLIBC_FLOOR="${GLIBC_FLOOR:-none}"
case "$TARGET_RID" in
    linux-x64)     GLIBC_MAX="$GLIBC_MAX_X64" ;;
    linux-arm64)   GLIBC_MAX="$GLIBC_MAX_ARM64" ;;
    linux-riscv64) GLIBC_MAX="$GLIBC_MAX_RISCV64" ;;
esac
if [ "$GLIBC_FLOOR" = "none" ]; then
    pass "glibc floor: none referenced"
elif printf '%s\n%s\n' "$GLIBC_FLOOR" "$GLIBC_MAX" | sort -V -C; then
    pass "glibc floor: $GLIBC_FLOOR (allowed <= $GLIBC_MAX)"
else
    fail "glibc floor $GLIBC_FLOOR is NEWER than the allowed $GLIBC_MAX - this binary would not load on the systems the package promises"
fi

# 5e. dlopen smoke test - loads the library the way .NET does and drives the real entry points.
echo "  --- dlopen smoke test ---"
cc -O2 -o /tmp/smoke-test "$WORK/smoke-test.c" -ldl
if /tmp/smoke-test "$LIB" | sed 's/^/    /'; then
    pass "smoke test"
else
    fail "smoke test"
fi

# 5f. Conformance - the built CLI decodes every vendored vector and the md5 of the decoded
#     frames must equal the recorded value. This is the check that says the DECODER is right,
#     not merely that the file loads. Under qemu the emulated architectures run it too.
echo "  --- conformance ($TARGET_RID) ---"
CONFORMANCE_SUMMARY=""
if [ "$MODE" = "generate-expected" ]; then
    GEN="$WORK/output/generated-expected.md5"
    : > "$GEN"
    for v in "$VECTOR_DIR"/*.ivf; do
        b="$(basename "$v")"
        for fg in 1 0; do
            h="$("$BUILT_CLI" -i "$v" --muxer md5 -o - --filmgrain $fg 2>/dev/null | tr -d '\r\n')"
            echo "$b|--filmgrain $fg|$h" >> "$GEN"
            echo "    $b (--filmgrain $fg) -> $h"
        done
    done
    pass "hashes written to output/generated-expected.md5 (MODE=generate-expected: nothing was compared)"
else
    [ -f "$EXPECTED_FILE" ] || { fail "no expected-hash file at $EXPECTED_FILE - the conformance gate cannot run"; }
    if [ -f "$EXPECTED_FILE" ]; then
        VEC_COUNT=0
        while IFS='|' read -r vec flags want rest; do
            case "$vec" in ''|\#*) continue ;; esac
            v="$VECTOR_DIR/$vec"
            if [ ! -f "$v" ]; then
                fail "expected-hash file names a vector that is not in the repository: $vec"
                continue
            fi
            # shellcheck disable=SC2086
            got="$("$BUILT_CLI" -i "$v" --muxer md5 -o - $flags 2>/dev/null | tr -d '\r\n')"
            VEC_COUNT=$((VEC_COUNT + 1))
            if [ "$got" = "$want" ]; then
                echo "    [ok]   $vec ($flags) $got"
                CONFORMANCE_SUMMARY="$CONFORMANCE_SUMMARY
  $vec ($flags) = $got"
            else
                fail "conformance: $vec ($flags) decoded to $got, expected $want"
                CONFORMANCE_SUMMARY="$CONFORMANCE_SUMMARY
  $vec ($flags) = $got   *** EXPECTED $want ***"
            fi
        done < "$EXPECTED_FILE"
        if [ "$VEC_COUNT" -eq 0 ]; then
            fail "the expected-hash file contained no usable entries"
        else
            pass "$VEC_COUNT conformance decodes checked"
        fi
    fi
fi

if [ "$FAILED" -ne 0 ]; then
    echo
    echo "VERIFICATION FAILED for $TARGET_RID. output/$TARGET_RID is left in place for inspection,"
    echo "but nothing was staged and this build must not be adopted into the package."
    rm -rf "$WORK/output/staging/$TARGET_RID"
    exit 1
fi
echo

# ----------------------------------------------------------------------------------------------
# 6. Stage + record. output/staging/<rid>/libdav1d.so.xz is the compressed artefact for
#    transfer; output/<rid>/ is what gets copied into the package.
# ----------------------------------------------------------------------------------------------
SHA="$(sha256sum "$LIB" | cut -d' ' -f1)"
SHA_UNSTRIPPED="$(sha256sum "$OUT/unstripped/$LIB_NAME" | cut -d' ' -f1)"
mkdir -p "$WORK/output/staging/$TARGET_RID"
xz -9e -k -c "$LIB" > "$WORK/output/staging/$TARGET_RID/$LIB_NAME.xz"
SIZE_XZ="$(stat -c %s "$WORK/output/staging/$TARGET_RID/$LIB_NAME.xz")"
ELAPSED=$(( SECONDS - START_SECONDS ))

cat > "$OUT/BUILD-INFO.txt" <<EOF
dav1d native library - build information
==============================================================================
RID              : $TARGET_RID
Built            : $STARTED_AT
Build duration   : ${ELAPSED}s (wall clock inside the container)
Built by         : dav1d-native-tools/linux/build.sh -> container-build.sh
Network          : DISABLED during this build (interfaces: ${IFACES:-none})

Build machine
------------------------------------------------------------------------------
Derived image    : ${DERIVED_IMAGE:-unknown}
Base image       : ${BASE_IMAGE_REF:-unknown}
Container OS     : $(grep PRETTY_NAME /etc/os-release 2>/dev/null | first_line | cut -d= -f2- | tr -d '"')
Machine          : $(uname -m)$( [ "$(uname -m)" != "x86_64" ] && echo " (qemu-user emulated on an x86_64 host, unless built on real hardware)" )
Compiler         : $CC_VERSION
Assembler        : $AS_VERSION
nasm             : $NASM_ACTUAL
meson            : $MESON_ACTUAL (pinned $MESON_VERSION)
ninja            : $NINJA_ACTUAL (pinned $NINJA_VERSION)
Container glibc  : $GLIBC_ACTUAL

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
dav1d            : $DAV1D_VERSION  ($DAV1D_DESCRIBE)
Commit           : $DAV1D_COMMIT
API version      : $DAV1D_API_VERSION
Vendored at      : dav1d-native-tools/dav1d/ (see UPSTREAM.txt)
Patches applied  : $PATCHES_APPLIED

Configuration
------------------------------------------------------------------------------
meson options    : $DAV1D_MESON_OPTIONS

Result
------------------------------------------------------------------------------
File             : $LIB_NAME  (unversioned on purpose - LibraryImport("dav1d") probes this name)
SONAME           : $SONAME
Size (stripped)  : $SIZE_STRIPPED bytes
Size (unstripped): $SIZE_UNSTRIPPED bytes  -> unstripped/$LIB_NAME
Size (xz staged) : $SIZE_XZ bytes          -> ../staging/$TARGET_RID/$LIB_NAME.xz
SHA256           : $SHA
SHA256 unstripped: $SHA_UNSTRIPPED
glibc floor      : ${GLIBC_FLOOR} (allowed <= ${GLIBC_MAX:-n/a})
Dynamic deps     : $(printf '%s ' $DEPS)
Licence beside it: LICENSE-Dav1d.txt (a verbatim copy of dav1d's COPYING, BSD-2-Clause)

Conformance (dav1d CLI built from this same source, --muxer md5)
------------------------------------------------------------------------------$CONFORMANCE_SUMMARY
EOF

echo "--- done ---"
echo "  $OUT/$LIB_NAME"
echo "  sha256 $SHA"
echo "  staged $WORK/output/staging/$TARGET_RID/$LIB_NAME.xz ($SIZE_XZ bytes)"
echo "  ${ELAPSED}s"
