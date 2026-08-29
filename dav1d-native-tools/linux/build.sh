#!/usr/bin/env bash
# ==============================================================================================
# build.sh - build the three Linux dav1d native libraries, from this repository only
# ==============================================================================================
#
#   ./build.sh                build all three (linux-x64, linux-arm64, linux-riscv64)
#   ./build.sh all            the same
#   ./build.sh x64            build linux-x64 only
#   ./build.sh arm64          build linux-arm64 only
#   ./build.sh riscv64        build linux-riscv64 only
#
# Options (environment variables):
#   CONTAINER_ENGINE=podman|docker   force an engine (default: podman, then docker)
#   FORCE_IMAGE_REBUILD=1            rebuild the derived build image even if it exists
#   MODE=generate-expected           decode the vectors and WRITE their hashes to
#                                    output/generated-expected.md5 instead of comparing them.
#                                    Only for establishing a new test-vectors/EXPECTED.md5;
#                                    a normal build must never use it.
#
# WHAT THIS DOES, IN ONE PARAGRAPH
#   For each architecture it makes sure a derived build image exists (base manylinux image +
#   meson + ninja + nasm - see Containerfile.<arch>; building it is the one step that uses the
#   network), then runs container-build.sh inside it WITH THE NETWORK DISABLED, mounting
#   dav1d-native-tools/ as /work. Everything compiled comes from this repository: the vendored
#   dav1d snapshot in dav1d/ and the conformance streams in test-vectors/.
#
# WHY A CONTAINER, EVEN FOR X64
#   glibc symbol versioning is forward-only: a binary built against the glibc on a current
#   desktop distro refuses to load on anything older, so building on the workstation would
#   quietly restrict the package to the newest distributions - and the failure would only show
#   up on a user's machine. The manylinux images fix the floor at glibc 2.28 (2.39 for riscv64,
#   where nothing older exists). Every Linux RID, x64 included, is built this way.
#
# THIS SCRIPT NEVER INSTALLS ANYTHING ON YOUR MACHINE. If a prerequisite is missing it names it,
# prints the command that installs it, and stops. See README.txt for the full list.
# ==============================================================================================

set -euo pipefail

trap 'rc=$?; echo "ERROR: build.sh failed (exit $rc) at line $LINENO: $BASH_COMMAND" >&2' ERR

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOOLS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"           # dav1d-native-tools/
OUTPUT_DIR="$TOOLS_DIR/output"

# shellcheck source=pins.env
. "$SCRIPT_DIR/pins.env"

MODE="${MODE:-build}"

# ----------------------------------------------------------------------------------------------
# Prerequisites - checked, never installed
# ----------------------------------------------------------------------------------------------
pick_engine() {
    if [ -n "${CONTAINER_ENGINE:-}" ]; then
        command -v "$CONTAINER_ENGINE" > /dev/null 2>&1 || {
            echo "ERROR: CONTAINER_ENGINE=$CONTAINER_ENGINE is not on PATH." >&2; exit 1; }
        echo "$CONTAINER_ENGINE"
        return
    fi
    if command -v podman > /dev/null 2>&1; then echo podman; return; fi
    if command -v docker > /dev/null 2>&1; then echo docker; return; fi
    cat >&2 <<'EOF'
ERROR: neither podman nor docker is installed.

  This script does not install anything. Install a container engine yourself:

    Debian-based Linux:  sudo apt install podman
    Fedora/RHEL:         sudo dnf install podman

  Then re-run. See README.txt (PREREQUISITES) for the complete list.
EOF
    exit 1
}

ENGINE="$(pick_engine)"

[ -f "$TOOLS_DIR/$DAV1D_DIR/meson.build" ] || {
    echo "ERROR: the vendored dav1d source is missing from $TOOLS_DIR/$DAV1D_DIR." >&2
    echo "       Nothing can be built without it, and it is not downloaded - it is part of this" >&2
    echo "       repository. Restore it from git." >&2
    exit 1; }

if [ "$MODE" != "generate-expected" ] && [ ! -f "$TOOLS_DIR/$TEST_VECTOR_EXPECTED" ]; then
    echo "ERROR: the expected-hash file $TEST_VECTOR_EXPECTED is missing." >&2
    echo "       The conformance gate cannot run without it, and a build that skips conformance" >&2
    echo "       is not a build worth shipping. See test-vectors/README.txt." >&2
    exit 1
fi

host_machine="$(uname -m)"

arch_rid()      { case "$1" in x64) echo linux-x64;; arm64) echo linux-arm64;; riscv64) echo linux-riscv64;; esac; }
arch_platform() { case "$1" in x64) echo linux/amd64;; arm64) echo linux/arm64;; riscv64) echo linux/riscv64;; esac; }
arch_native()   { case "$1" in x64) echo x86_64;; arm64) echo aarch64;; riscv64) echo riscv64;; esac; }
arch_image()    {
    case "$1" in
        x64)     echo "${IMAGE_X64%%:*}@$IMAGE_X64_DIGEST" ;;
        arm64)   echo "${IMAGE_ARM64%%:*}@$IMAGE_ARM64_DIGEST" ;;
        riscv64) echo "${IMAGE_RISCV64%%:*}@$IMAGE_RISCV64_DIGEST" ;;
    esac
}
arch_tag()      {
    case "$1" in x64) echo "$IMAGE_X64";; arm64) echo "$IMAGE_ARM64";; riscv64) echo "$IMAGE_RISCV64";; esac
}

check_emulation() {
    local arch="$1" want
    want="$(arch_native "$arch")"
    [ "$want" = "$host_machine" ] && return 0

    # Non-native architecture: the kernel needs a binfmt_misc handler registered for it,
    # otherwise the container starts and every command inside it dies with "exec format error".
    # grep WITHOUT -q on purpose: -q exits at the first match, `ls` then dies of SIGPIPE, and
    # under `set -o pipefail` that reads as "no handler registered".
    if [ -d /proc/sys/fs/binfmt_misc ] && ls /proc/sys/fs/binfmt_misc 2>/dev/null | grep -i "qemu-$want" > /dev/null; then
        echo "  emulation : qemu-$want binfmt handler registered"
        return 0
    fi
    cat >&2 <<EOF

ERROR: building $arch on a $host_machine host needs qemu user-mode emulation, and no
       binfmt handler for qemu-$want is registered.

  Set it up ONE of these ways (neither is done for you):

    1. Install the static qemu binaries and their binfmt registrations:
         sudo apt install qemu-user-static binfmt-support

    2. Or register the handlers from a container, installing nothing permanently:
         sudo $ENGINE run --rm --privileged docker.io/multiarch/qemu-user-static --reset -p yes

  Verify with:  ls /proc/sys/fs/binfmt_misc | grep qemu

EOF
    exit 1
}

ensure_image() {
    local arch="$1" narch derived base platform
    narch="$(arch_native "$arch")"
    derived="$DERIVED_IMAGE_PREFIX-$narch"
    base="$(arch_image "$arch")"
    platform="$(arch_platform "$arch")"

    if [ "${FORCE_IMAGE_REBUILD:-0}" != "1" ] && "$ENGINE" image exists "$derived" 2>/dev/null; then
        echo "  image     : $derived (already built - reusing)"
        return 0
    fi

    echo "  image     : $derived - building it now."
    echo "              THIS IS THE ONE STEP THAT USES THE NETWORK: it installs meson"
    echo "              ${MESON_VERSION}, ninja ${NINJA_VERSION}$( [ "$narch" = "x86_64" ] && echo " and nasm" ) into the"
    echo "              digest-pinned base image. The library build afterwards runs with"
    echo "              --network none."
    "$ENGINE" build \
        --platform "$platform" \
        -f "$SCRIPT_DIR/Containerfile.$narch" \
        -t "$derived" \
        --build-arg BASE_IMAGE="$base" \
        --build-arg MESON_VERSION="$MESON_VERSION" \
        --build-arg NINJA_VERSION="$NINJA_VERSION" \
        --build-arg PYTHON_IN_IMAGE="$PYTHON_IN_IMAGE" \
        $( [ "$narch" = "x86_64" ] && echo "--build-arg NASM_MIN_VERSION=$NASM_MIN_VERSION" ) \
        $( [ "$narch" = "riscv64" ] && echo "--build-arg BINUTILS_MIN_FOR_RVV=$BINUTILS_MIN_FOR_RVV" ) \
        "$SCRIPT_DIR"
}

build_arch() {
    local arch="$1" rid narch derived base platform started elapsed
    rid="$(arch_rid "$arch")"
    narch="$(arch_native "$arch")"
    derived="$DERIVED_IMAGE_PREFIX-$narch"
    base="$(arch_image "$arch")"
    platform="$(arch_platform "$arch")"
    started="$SECONDS"

    echo
    echo "=============================================================================="
    echo " BUILD $rid"
    echo "=============================================================================="
    echo "  base tag  : $(arch_tag "$arch")"
    echo "  base pin  : $base"
    echo "  platform  : $platform"
    echo "  host      : $host_machine"
    check_emulation "$arch"
    ensure_image "$arch"
    echo "  network   : DISABLED for the build itself (--network none)"
    echo

    "$ENGINE" run --rm \
        --network none \
        --platform "$platform" \
        -v "$TOOLS_DIR":/work:Z \
        -e TARGET_RID="$rid" \
        -e DERIVED_IMAGE="$derived" \
        -e BASE_IMAGE_REF="$base" \
        -e MODE="$MODE" \
        -e DAV1D_DIR="$DAV1D_DIR" \
        -e DAV1D_VERSION="$DAV1D_VERSION" \
        -e DAV1D_DESCRIBE="$DAV1D_DESCRIBE" \
        -e DAV1D_COMMIT="$DAV1D_COMMIT" \
        -e DAV1D_API_VERSION="$DAV1D_API_VERSION" \
        -e DAV1D_MESON_OPTIONS="$DAV1D_MESON_OPTIONS" \
        -e MESON_VERSION="$MESON_VERSION" \
        -e NINJA_VERSION="$NINJA_VERSION" \
        -e GLIBC_MAX_X64="$GLIBC_MAX_X64" \
        -e GLIBC_MAX_ARM64="$GLIBC_MAX_ARM64" \
        -e GLIBC_MAX_RISCV64="$GLIBC_MAX_RISCV64" \
        -e ALLOWED_DEPS="$ALLOWED_DEPS" \
        -e TEST_VECTOR_DIR="$TEST_VECTOR_DIR" \
        -e TEST_VECTOR_EXPECTED="$TEST_VECTOR_EXPECTED" \
        "$derived" \
        bash /work/linux/container-build.sh

    elapsed=$(( SECONDS - started ))
    echo "  $rid finished in ${elapsed}s"
}

write_sha256sums() {
    # One checksum file covering every built RID, including the ones built on other machines
    # (Windows, macOS) if their output has been copied in. Paths are relative to output/.
    [ -d "$OUTPUT_DIR" ] || return 0
    ( cd "$OUTPUT_DIR" && \
      find . -type f \( -name 'libdav1d.so' -o -name 'libdav1d.dylib' -o -name 'dav1d.dll' -o -name '*.xz' \) \
        | sed 's|^\./||' | sort | xargs -r sha256sum > SHA256SUMS )
    echo
    echo "  output/SHA256SUMS:"
    sed 's/^/    /' "$OUTPUT_DIR/SHA256SUMS"
}

# ----------------------------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------------------------
TARGET="${1:-all}"

echo "container engine : $ENGINE"
echo "tools folder     : $TOOLS_DIR"
echo "dav1d            : $DAV1D_VERSION ($DAV1D_DESCRIBE), commit $DAV1D_COMMIT"
echo "meson / ninja    : $MESON_VERSION / $NINJA_VERSION (pinned)"
echo "mode             : $MODE"

case "$TARGET" in
    x64|arm64|riscv64) build_arch "$TARGET" ;;
    all)               for a in x64 arm64 riscv64; do build_arch "$a"; done ;;
    *) echo "usage: $0 [all|x64|arm64|riscv64]" >&2; exit 2 ;;
esac

write_sha256sums

echo
echo "=============================================================================="
echo " Outputs are in dav1d-native-tools/output/<rid>/"
echo "   libdav1d.so        stripped, the file the package ships"
echo "   LICENSE            dav1d's COPYING, shipped beside it (BSD-2-Clause clause 2)"
echo "   BUILD-INFO.txt     toolchain, pins, sizes, sha256, glibc floor, conformance"
echo "   unstripped/        keep for crash triage; never shipped"
echo " Compressed copies:  output/staging/<rid>/libdav1d.so.xz"
echo " To adopt them into the package, follow ADOPTING A BUILT BINARY in README.txt."
echo "=============================================================================="
