#!/usr/bin/env bash
# ==============================================================================================
# build-common.sh - shared machinery for the macOS dav1d builds
# ==============================================================================================
#
#   RUN AND VERIFIED ON macOS 2026-08-29. Both slices built clean on the first attempt and passed
#   the full gate; no fix to this file was needed. See README.txt, "WHAT HAS AND HAS NOT BEEN
#   VERIFIED", for what that run established and for the two known quirks (nasm 3.02 struct
#   symbols, and the LC_UUID that makes osx-x64 not byte-reproducible).
#
# Sourced by build-osx-arm64.sh and build-osx-x64.sh; not meant to be run directly.
# ==============================================================================================

set -euo pipefail

trap 'rc=$?; echo "ERROR: the macOS dav1d build failed (exit $rc) at line $LINENO: $BASH_COMMAND" >&2' ERR

# The first line of stdin, without the `head -1` pitfall: `cmd | head -1` inside $(...) under
# `set -o pipefail` can report 141 because head exits first and the writer takes SIGPIPE. That
# killed two builds stone dead, silently, when this tooling was written. `sed -n 1p` reads its
# input to the end.
first_line() { sed -n '1p'; }

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"     # dav1d-native-tools/
PINS_FILE="$TOOLS_DIR/linux/pins.env"

# pins.env is a plain KEY=VALUE file and is the single source of truth for every platform. It
# lives in linux/ because that is where the container build sources it; sourcing it here is
# exactly what the Windows scripts do by parsing it.
[ -f "$PINS_FILE" ] || {
    echo "ERROR: $PINS_FILE is missing. It is part of the repository; if it vanished after a" >&2
    echo "       clone, check the root .gitignore's blanket '*.env' rule (see README.txt)." >&2
    exit 1; }
# shellcheck source=../linux/pins.env
. "$PINS_FILE"

SRC_DIR="$TOOLS_DIR/$DAV1D_DIR"
PATCH_DIR="$TOOLS_DIR/patches"
VECTOR_DIR="$TOOLS_DIR/$TEST_VECTOR_DIR"
EXPECTED_FILE="$TOOLS_DIR/$TEST_VECTOR_EXPECTED"
SMOKE_SOURCE="$TOOLS_DIR/smoke-test.c"

# ----------------------------------------------------------------------------------------------
# The minimum macOS version the built dylib will load on.
#
# WHY 11.0, FOR BOTH SLICES
#   With no explicit deployment target, clang stamps the BUILD MACHINE's OS version into the
#   Mach-O load command, so a dylib built on a current Mac is refused by dyld on every older one -
#   the same failure mode as the glibc floor on Linux, and just as invisible until a user hits it.
#   Fixing it explicitly is the only way to control it.
#
#   11.0 (Big Sur) is the oldest macOS that exists for Apple Silicon, so the arm64 slice cannot
#   sensibly go lower. Using the same number for the x86_64 slice keeps ONE macOS floor for the
#   whole package instead of two, and it is not a real restriction: the .NET runtime this package
#   is loaded by has a macOS floor of its own that is at least this high, so the native library
#   is never the component that decides how old a Mac can be. If that ever stops being true, the
#   x86_64 slice can be lowered on its own - it is a single number, checked by the gate.
MACOS_MIN_VERSION=11.0

# ----------------------------------------------------------------------------------------------
# Required exports: the 17 entry points the managed binding declares. Mach-O prefixes C symbols
# with an underscore, which is why they are matched as _<name> below.
# ----------------------------------------------------------------------------------------------
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

# The only dynamic dependency a correct build has. Anything else means the package would demand
# a library be installed on the user's Mac.
ALLOWED_DYLIBS="/usr/lib/libSystem.B.dylib"

GATE_FAILED=0
gate_pass() { echo "  [ok] $1"; }
gate_fail() { echo "  [FAIL] $1"; GATE_FAILED=1; }

# ----------------------------------------------------------------------------------------------
require_tool() {
    local tool="$1" hint="$2"
    command -v "$tool" > /dev/null 2>&1 || {
        echo "ERROR: $tool is not on PATH." >&2
        echo >&2
        echo "$hint" >&2
        echo >&2
        echo "This script installs nothing. See README.txt, PREREQUISITES." >&2
        exit 1; }
}

check_common_prerequisites() {
    require_tool cc      'Install the Xcode Command Line Tools:   xcode-select --install'
    require_tool meson   "Install it with Homebrew:   brew install meson   (pinned version in pins.env: $MESON_VERSION)"
    require_tool ninja   "Install it with Homebrew:   brew install ninja   (pinned version in pins.env: $NINJA_VERSION)"
    require_tool otool   'Part of the Xcode Command Line Tools:   xcode-select --install'
    require_tool install_name_tool 'Part of the Xcode Command Line Tools:   xcode-select --install'
    require_tool codesign 'Part of the Xcode Command Line Tools:   xcode-select --install'

    [ -f "$EXPECTED_FILE" ] || {
        echo "ERROR: the expected-hash file $EXPECTED_FILE is missing. The conformance gate" >&2
        echo "       cannot run without it, and a build that skips conformance is not a build" >&2
        echo "       worth shipping." >&2
        exit 1; }
}

# ----------------------------------------------------------------------------------------------
# The vendored tree is copied to scratch and built there, so ../dav1d is never written to and
# stays a verifiable, unmodified upstream snapshot.
# ----------------------------------------------------------------------------------------------
copy_source_to_scratch() {
    local scratch="$1"
    rm -rf "$scratch"
    cp -a "$SRC_DIR" "$scratch"
    echo "  copied $SRC_DIR -> $scratch"

    PATCHES_APPLIED="none"
    if [ -d "$PATCH_DIR" ] && ls "$PATCH_DIR"/*.patch > /dev/null 2>&1; then
        PATCHES_APPLIED=""
        for p in "$PATCH_DIR"/*.patch; do
            echo "  applying $(basename "$p")"
            ( cd "$scratch" && patch -p1 --forward --batch < "$p" ) \
                || { echo "ERROR: patch $(basename "$p") did not apply. Fix it; patches are never applied best-effort." >&2; exit 1; }
            PATCHES_APPLIED="$PATCHES_APPLIED $(basename "$p")"
        done
        PATCHES_APPLIED="${PATCHES_APPLIED# }"
    fi
    echo "  patches applied: $PATCHES_APPLIED"
}

# ----------------------------------------------------------------------------------------------
# The gate. Same checks as the Linux build, expressed with the tools macOS has.
#   $1 dylib   $2 expected arch (arm64 | x86_64)   $3 the built CLI   $4 can we run target code
# ----------------------------------------------------------------------------------------------
verify_dylib() {
    local dylib="$1" want_arch="$2" cli="$3" can_run="$4"

    # 1. Architecture ---------------------------------------------------------------------------
    local file_out
    file_out="$(file -b "$dylib")"
    case "$file_out" in
        *"$want_arch"*) gate_pass "architecture: $file_out" ;;
        *)              gate_fail "architecture mismatch: expected $want_arch, file says: $file_out" ;;
    esac

    # 2. Required exports -----------------------------------------------------------------------
    local exports missing=""
    exports="$(nm -gU "$dylib" | awk '{print $NF}')"
    for sym in $REQUIRED_SYMBOLS; do
        printf '%s\n' "$exports" | grep -x "_$sym" > /dev/null || missing="$missing $sym"
    done
    if [ -n "$missing" ]; then
        gate_fail "missing exports:$missing"
    else
        gate_pass "all $(printf '%s\n' "$REQUIRED_SYMBOLS" | grep -c .) required symbols exported"
    fi

    # 3. Dependencies + install name ------------------------------------------------------------
    local deps unexpected="" install_name
    install_name="$(otool -D "$dylib" | sed -n '2p')"
    if [ "$install_name" = "@rpath/libdav1d.dylib" ]; then
        gate_pass "install name: $install_name"
    else
        gate_fail "install name is '$install_name', expected @rpath/libdav1d.dylib - .NET's resolver needs the @rpath form"
    fi

    deps="$(otool -L "$dylib" | tail -n +2 | awk '{print $1}' | grep -v '^@rpath/libdav1d.dylib$' || true)"
    for d in $deps; do
        case " $ALLOWED_DYLIBS " in
            *" $d "*) ;;
            *) unexpected="$unexpected $d" ;;
        esac
    done
    if [ -n "$unexpected" ]; then
        gate_fail "unexpected dynamic dependencies:$unexpected (allowed: $ALLOWED_DYLIBS)"
    else
        gate_pass "dependencies are system-only: $(printf '%s ' $deps)"
    fi

    # 4. Deployment target - CHECKED, not merely reported ---------------------------------------
    local minos
    minos="$(otool -l "$dylib" | awk '/LC_BUILD_VERSION/{f=1} f && /minos/{print $2; exit}')"
    if [ -z "$minos" ]; then
        # Older toolchains emit LC_VERSION_MIN_MACOSX instead.
        minos="$(otool -l "$dylib" | awk '/LC_VERSION_MIN_MACOSX/{f=1} f && /version/{print $2; exit}')"
    fi
    if [ "$minos" = "$MACOS_MIN_VERSION" ]; then
        gate_pass "minimum macOS: $minos"
    else
        gate_fail "minimum macOS is '$minos', expected $MACOS_MIN_VERSION - a build on a newer Mac must not silently raise the floor"
    fi

    # 5. Code signature -------------------------------------------------------------------------
    # An unsigned dylib is refused outright on Apple Silicon. The linker ad-hoc signs arm64
    # output on its own but leaves x86_64 unsigned, so both scripts sign explicitly and this
    # check confirms it happened.
    if codesign -dv "$dylib" > /dev/null 2>&1; then
        gate_pass "code signature present ($(codesign -dv "$dylib" 2>&1 | grep -i 'Signature' | first_line || echo 'ad-hoc'))"
    else
        gate_fail "no code signature - Apple Silicon refuses to load an unsigned dylib"
    fi

    # 6. dlopen smoke test ----------------------------------------------------------------------
    if [ "$can_run" = "yes" ]; then
        echo "  --- dlopen smoke test ---"
        cc -O2 -arch "$want_arch" -o /tmp/dav1d-smoke-test "$SMOKE_SOURCE"
        if /tmp/dav1d-smoke-test "$dylib" | sed 's/^/    /'; then
            gate_pass "smoke test"
        else
            gate_fail "smoke test"
        fi
    else
        gate_fail "smoke test NOT RUN - this machine cannot execute $want_arch code (Rosetta 2 missing?). Reported as a failure on purpose: an unrun check is not a passed check."
    fi

    # 7. Conformance ----------------------------------------------------------------------------
    echo "  --- conformance ---"
    CONFORMANCE_SUMMARY=""
    if [ "$can_run" != "yes" ]; then
        gate_fail "conformance NOT RUN - the built dav1d CLI cannot execute on this machine."
        return
    fi
    local count=0
    while IFS='|' read -r vec flags want rest; do
        case "$vec" in ''|\#*) continue ;; esac
        local v="$VECTOR_DIR/$vec" got
        if [ ! -f "$v" ]; then
            gate_fail "expected-hash file names a vector that is not in the repository: $vec"
            continue
        fi
        # shellcheck disable=SC2086
        got="$("$cli" -i "$v" --muxer md5 -o - $flags 2>/dev/null | tr -d '\r\n')"
        count=$((count + 1))
        if [ "$got" = "$want" ]; then
            echo "    [ok]   $vec ($flags) $got"
            CONFORMANCE_SUMMARY="$CONFORMANCE_SUMMARY
  $vec ($flags) = $got"
        else
            gate_fail "conformance: $vec ($flags) decoded to $got, expected $want"
            CONFORMANCE_SUMMARY="$CONFORMANCE_SUMMARY
  $vec ($flags) = $got   *** EXPECTED $want ***"
        fi
    done < "$EXPECTED_FILE"
    if [ "$count" -eq 0 ]; then
        gate_fail "the expected-hash file contained no usable entries"
    else
        gate_pass "$count conformance decodes checked"
    fi
}
