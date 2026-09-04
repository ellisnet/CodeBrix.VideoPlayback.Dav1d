================================================================================
dav1d-native-tools/linux - building libdav1d.so for linux-x64, linux-arm64
                           and linux-riscv64
================================================================================

WHAT THIS IS
--------------------------------------------------------------------------------
Everything needed to build the three Linux native libraries this package ships,
from the dav1d source vendored in ../dav1d/. All three are built on ONE ordinary
Linux machine - arm64 and riscv64 run under qemu user-mode emulation - and each
one is verified before it is allowed to exist in output/.

The build reaches nothing outside this repository. It literally cannot: the
compile runs in a container started with `--network none`, and the first thing
container-build.sh prints is the proof that the container has no network
interface but loopback. The source it compiles, the conformance streams it
decodes and the expected hashes it checks are all files in this repository.

NOTHING HERE INSTALLS ANYTHING ON YOUR MACHINE. Every script checks what it
needs, and if something is missing it names it, prints the command that installs
it, and stops. Installing is your decision.


================================================================================
PREREQUISITES  (the complete list - two packages)
================================================================================
  1. A container engine - podman (preferred) or docker.

       sudo apt install podman

     Verify:       podman --version
     Used in 2026: podman 5.4.2

  2. qemu user-mode emulation and its binfmt registrations. Needed only for the
     architectures that are not your host's - on an x86_64 machine that means
     arm64 and riscv64.

       sudo apt install qemu-user-static

     (On Debian-based systems that package registers the handlers itself. If
     yours does not, `sudo apt install binfmt-support` as well.)

     Verify:       ls /proc/sys/fs/binfmt_misc | grep qemu
                   you want qemu-aarch64 and qemu-riscv64 in the list
     Used in 2026: qemu 10.0

     Alternative that installs nothing permanently - register the handlers from
     a container:
       sudo podman run --rm --privileged \
            docker.io/multiarch/qemu-user-static --reset -p yes

  3. Disk: about 5 GB for the three base images plus the three derived ones.
     The build itself needs very little; dav1d is a small project.

  NOT REQUIRED ON THE HOST, AND DELIBERATELY SO: meson, ninja, nasm, gcc, any
  cross-compiler. They live inside the container images, at pinned versions, so
  the build does not vary with whatever the workstation happens to have
  installed this year. If you want them on your machine for other reasons that
  is your business - this tooling neither needs nor uses them.

  ffmpeg is needed ONLY if you want to regenerate the conformance streams, which
  is not part of a build. See ../test-vectors/README.txt.


================================================================================
THE IMAGES, AND WHY THEY ARE PINNED BY DIGEST
================================================================================
  RID             base image                          glibc floor
  --------------  ----------------------------------  -----------
  linux-x64       quay.io/pypa/manylinux_2_28_x86_64   2.28
  linux-arm64     quay.io/pypa/manylinux_2_28_aarch64  2.28
  linux-riscv64   quay.io/pypa/manylinux_2_39_riscv64  2.39

WHY A CONTAINER, EVEN FOR X64. glibc symbol versioning is forward-only: a binary
compiled against the glibc on a current desktop distro refuses to load on
anything older. Building the "native" x64 library on the workstation would
quietly restrict the package to the newest distributions, and the failure would
only appear on a user's machine. The manylinux images are old userlands with
modern compilers, which is exactly the tool for this. The glibc number in the
image name IS the compatibility floor being chosen.

riscv64 has no older manylinux than 2_39, so its floor is glibc 2.39 - Debian 13
/ Ubuntu 24.04 and newer. In practice every riscv64 distribution anyone runs is
newer than that.

WHY DIGESTS. pins.env records a dated tag AND a sha256 digest for each image,
and the Containerfiles resolve the digest. A tag can be moved; a digest cannot.
Without that, an upstream retag would silently change the compiler under a
rebuild and nobody would know why a binary changed. The tag is kept beside it so
a human can see which image generation it is.

The three digests are the same ones CodeBrix.Audio's native tooling pins, so
both packages' Linux binaries come from an identical base userland.


================================================================================
THE DERIVED IMAGE - THE "INSTALL THE TOOLS" STEP
================================================================================
The base images have gcc, binutils and CPython, but not meson, ninja or nasm.
Containerfile.<arch> adds exactly those, at the versions pinned in pins.env, and
nothing else:

  Containerfile.x86_64    + nasm (dnf), + meson, ninja (pip)   -> codebrix-dav1d-build-x86_64
  Containerfile.aarch64   + meson, ninja (pip)                 -> codebrix-dav1d-build-aarch64
  Containerfile.riscv64   + meson, ninja (pip)                 -> codebrix-dav1d-build-riscv64

  meson 1.12.0 and ninja 1.13.0 (pinned 2026-08-28: the newest pip could install
  that day). dav1d needs meson >= 0.54, so there is plenty of headroom.
  nasm from the image's package manager - 2.15.03 on AlmaLinux 8, above dav1d's
  >= 2.14 floor, which the Containerfile checks rather than assumes.

Building one of these images IS the "install the tools on the build machine"
step that the rule allows, written down as a file instead of as a paragraph
somebody has to follow by hand. It is the only step that uses the network.
build.sh builds an image automatically the first time it needs it and reuses it
afterwards; to force a rebuild:

    FORCE_IMAGE_REBUILD=1 ./build.sh

Each Containerfile ends by PROVING its toolchain rather than trusting a version
string:
  * x86_64  - nasm's version is compared against the floor.
  * aarch64 - `as` is asked to assemble dotprod and i8mm instructions.
  * riscv64 - `as` is asked to assemble RVV 1.0 (`.option arch, +v`). This one
    matters most: see the RISC-V note in TROUBLESHOOTING.


================================================================================
USAGE
================================================================================
    cd dav1d-native-tools/linux
    ./build.sh                  # all three RIDs
    ./build.sh x64              # or arm64 / riscv64

  Environment variables:
    CONTAINER_ENGINE=docker ./build.sh          force an engine
    FORCE_IMAGE_REBUILD=1 ./build.sh            rebuild the derived image first
    MODE=generate-expected ./build.sh x64       write ../output/generated-expected.md5
                                                instead of checking hashes. Only
                                                for establishing new expected
                                                values - see
                                                ../test-vectors/README.txt.

  Measured on a 24-core x86_64 laptop, 2026-08-28 (wall clock inside the
  container, excluding the one-off derived-image build):
    linux-x64      11 seconds
    linux-arm64    67 seconds   (qemu-user emulation)
    linux-riscv64  69 seconds   (qemu-user emulation)
  ...run sequentially, which is how `./build.sh` with no argument runs them:
  about two and a half minutes for all three. Run in parallel they took 85 and
  86 seconds, contending for cores.
  Emulation costs far less here than one might fear: dav1d is a small project,
  most of the work is compiling, and only the gate actually executes emulated
  code. There is no reason to find real arm64 or riscv64 hardware to BUILD on.

  DO NOT RUN TWO COPIES OF build.sh AT ONCE. The builds themselves are
  independent, but both rewrite ../output/SHA256SUMS at the end, so a run that
  finishes while another is still writing its .xz will record a hash of a
  half-written file. (Seen on 2026-08-28: the arm64 run hashed an empty
  riscv64 .xz. The later run corrected it, but do not rely on that.) If you do
  run them in parallel, finish with `./build.sh x64` - it is 11 seconds and
  rewrites SHA256SUMS over the settled tree - and then verify:
      cd ../output && sha256sum -c SHA256SUMS

  Output, git-ignored (see ../output/README.txt):
    ../output/<rid>/libdav1d.so           stripped - the file the package ships
    ../output/<rid>/LICENSE-Dav1d.txt               dav1d's COPYING, verbatim
    ../output/<rid>/BUILD-INFO.txt        toolchain, pins, sizes, sha256, glibc
                                          floor, conformance hashes
    ../output/<rid>/unstripped/libdav1d.so   for crash triage; never shipped
    ../output/staging/<rid>/libdav1d.so.xz   compressed, for moving between
                                          machines
    ../output/SHA256SUMS                  every artefact, one line each


================================================================================
THE MESON OPTIONS
================================================================================
    --buildtype=release -Ddefault_library=shared -Dbitdepths=8,16
    -Denable_asm=true -Denable_tools=true -Denable_tests=false
    -Denable_examples=false -Dxxhash_muxer=disabled

  bitdepths=8,16       both decoder halves. 16 covers 10- and 12-bit content;
                       without it a 10-bit stream simply fails to decode.
  enable_asm=true      the hand-written SIMD, which is the entire reason to use
                       dav1d rather than a C decoder.
  enable_tools=true    builds the dav1d CLI. The conformance gate needs it. It
                       is NOT shipped in the package - it stays in the build
                       directory and is discarded with the container.
  enable_tests=false   also stops meson from consulting
                       subprojects/checkasm.wrap, which would want to clone the
                       checkasm harness over a network the container does not
                       have.
  xxhash_muxer=disabled  the xxh3 muxer needs an xxhash.h that upstream expects
                       you to supply yourself. On 'auto' the result would depend
                       on what happened to be installed in the build
                       environment; disabled, every build is identical. The md5
                       muxer the gate uses is always built in.

  There is no cross-compiling here, and dav1d's shipped crossfiles in
  ../dav1d/package/crossfiles/ are not used. Each build runs in a container of
  its own architecture (emulated where necessary), so meson sees a native build,
  configures the native assembler, and runs the built binaries during the gate -
  which a cross-build could not do without a separate exe wrapper. The
  crossfiles remain in the vendored snapshot for anyone who wants that route.


================================================================================
THE VERIFICATION GATE
================================================================================
A build that fails ANY of these exits non-zero, removes its staged .xz, and must
not be adopted. A binary that compiles is not necessarily a binary that works.

  1. Architecture - `file` must report x86-64 / ARM aarch64 / UCB RISC-V to
     match the RID. Catches the wrong file being published under the wrong RID.

  2. Required exports - all 17 entry points the managed binding declares:
     the 13 decoder functions (dav1d_version, dav1d_version_api,
     dav1d_default_settings, dav1d_open, dav1d_parse_sequence_header,
     dav1d_send_data, dav1d_get_picture, dav1d_apply_grain, dav1d_flush,
     dav1d_close, dav1d_get_event_flags, dav1d_get_decode_error_data_props,
     dav1d_get_frame_delay) plus the four data/picture lifetime helpers
     (dav1d_data_wrap, dav1d_data_create, dav1d_data_unref,
     dav1d_picture_unref). A missing symbol here is a crash in the field.
     This list must stay in step with the [LibraryImport] declarations on the
     managed side, and is duplicated in ../smoke-test.c, the Windows scripts and
     the macOS scripts.

  3. Dependencies - `ldd -r` must report no undefined symbols, and the only
     NEEDED libraries may be libc / libm / libpthread / libdl (/ librt) and the
     dynamic loader. Anything else would mean the package demands a library be
     installed on the user's machine. dav1d genuinely has no other dependencies.
     On glibc 2.34+ images libpthread and libdl are folded into libc, so a
     shorter list there is correct, not suspicious.

  4. glibc floor - the highest GLIBC_x.y symbol version referenced, which IS the
     oldest system the binary loads on. CHECKED against pins.env, not merely
     reported: <= 2.28 for x64 and arm64, <= 2.39 for riscv64. A build that
     drifted onto a newer base image would otherwise silently drop older
     distributions. (dav1d uses so little of libc that the floors actually come
     out well below the ceiling - 2.14 on x64 in the 2026-08-28 build.)

  5. dlopen smoke test - ../smoke-test.c loads the freshly built library the way
     .NET does (dlopen + per-symbol lookup, no link-time dependency), resolves
     all 17 entry points, checks dav1d_version() and that dav1d_version_api()
     reports API major 7, then opens and closes a real decoder context - which
     is what actually starts the worker threads and runs CPU-feature detection,
     where a badly built library falls over. For the emulated architectures this
     runs under qemu inside the container, so arm64 and riscv64 are genuinely
     executed, not merely compiled.

  6. Conformance - the dav1d CLI built from this same source decodes every
     stream in ../test-vectors/ with `--muxer md5` and the hashes must equal
     ../test-vectors/EXPECTED.md5. This is the check that says the DECODER is
     right. The hashes are identical across all three RIDs by construction: if
     the NEON or RVV code produced different pictures from the C or SSE code,
     this is where it would show. See ../test-vectors/README.txt for how the
     expected values were established (three independent decoders agreed).


================================================================================
TROUBLESHOOTING
================================================================================
"neither podman nor docker found"
    Install one (see PREREQUISITES). The script will not install it for you.

"exec format error" / every command in the container dies immediately
    The binfmt handler for that architecture is not registered. See
    PREREQUISITES item 2.  ls /proc/sys/fs/binfmt_misc | grep qemu

Image pull fails / the tag no longer exists
    quay.io/pypa retires old dated tags. Pick a current tag from
    https://quay.io/organization/pypa, put it in pins.env WITH its digest, and
    say in the commit message which glibc floor that changes. Any manylinux_2_28
    or newer image works for x64/arm64.

(RISC-V) the build succeeds but the library is much slower than expected
    Check the image build log for "assembler accepts RVV 1.0: ok". dav1d's
    RISC-V DSP code is RVV 1.0 vector assembly, and meson only builds it if the
    assembler accepts `.option arch, +v` - GNU binutils >= 2.38 or clang >= 17.
    If it does not, meson does NOT fail; it quietly builds a C-only library.
    Containerfile.riscv64 assembles a two-line RVV file precisely so that this
    cannot happen silently, and fails the image build if the assembler is too
    old. If you ever have to fall back to -Denable_asm=false for riscv64, say so
    loudly in BUILD-INFO.txt, in ../BUILD-PROVENANCE.txt and in the release
    notes - a silently slower binary is worse than a missing one.
    (2026-08-28: binutils 2.41 in the Rocky Linux 10 based image; the probe
    passes and the vector code is built.)

Podman "permission denied" writing ../output/
    Rootless podman maps your user into the container and the :Z mount flag
    handles SELinux relabelling. On a system with an unusual security policy,
    try --userns=keep-id.

Slow emulated builds
    Expected - see the timings under USAGE. There is no need to find real arm64
    or riscv64 hardware to BUILD on; testing on real hardware is a separate and
    worthwhile thing.

"pins.env was not found" after a clone
    The repository-root .gitignore has a blanket '*.env' rule, and the root
    Visual Studio .gitignore also ignores directory names that occur inside the
    vendored dav1d tree (arm/, arm64/, Release/, Out/, ...). Both are handled by
    the "!*" re-include at the top of ../.gitignore - do not delete that line.
    Check with:  git check-ignore -v dav1d-native-tools/linux/pins.env

A hash mismatch in the conformance step
    Take it seriously: it means this build decodes differently from the
    reference. Do not update EXPECTED.md5 to make it pass. Compare against
    ffmpeg's libdav1d and libaom decoders on the same file (the commands are in
    ../test-vectors/README.txt) to see which one is the odd one out.


================================================================================
ADOPTING A BUILT BINARY INTO THE PACKAGE
================================================================================
  1. Read ../output/<rid>/BUILD-INFO.txt and satisfy yourself the build is the
     one you think it is: dav1d commit, image digest, glibc floor, conformance
     hashes.

  2. Copy the library and its licence into the package's runtimes tree:

       mkdir -p ../../src/CodeBrix.VideoPlayback.Dav1d/runtimes/<rid>/native
       cp ../output/<rid>/libdav1d.so \
          ../../src/CodeBrix.VideoPlayback.Dav1d/runtimes/<rid>/native/
       cp ../output/<rid>/LICENSE-Dav1d.txt \
          ../../src/CodeBrix.VideoPlayback.Dav1d/runtimes/<rid>/native/

     <rid> is linux-x64, linux-arm64 or linux-riscv64. The LICENSE-Dav1d.txt
     copy is not optional: BSD-2-Clause clause 2 requires the copyright notice to travel
     with a binary distribution, and this is where it travels.

     The file must keep the name libdav1d.so - unversioned. LibraryImport
     ("dav1d") probes exactly that name and does not follow sonames. The soname
     (libdav1d.so.7) stays inside the file and is recorded in BUILD-INFO.txt.

  3. Do NOT copy unstripped/ into the package. It is several times larger and
     is only for crash triage. It does have a home, though: copy
     ../output/<rid>/unstripped/libdav1d.so to ../unstripped/<rid>/ and extend
     ../unstripped/SHA256SUMS, in the SAME commit that adopts the binary. See
     ../unstripped/README.txt for the rule and the build-id check that proves
     the two are the same build.

  4. Record the build in ../BUILD-PROVENANCE.txt - copy the values straight out
     of BUILD-INFO.txt. That file is how anyone later can tell which binary came
     from what.

  5. Run the managed test suite before publishing.


================================================================================
FILES
================================================================================
  README.txt              this document
  pins.env                every version / digest / option pin (edit here only)
  build.sh                host entry point: images, emulation, orchestration
  container-build.sh      the build and the gate; runs inside the container
  Containerfile.x86_64    derived build image for linux-x64
  Containerfile.aarch64   derived build image for linux-arm64
  Containerfile.riscv64   derived build image for linux-riscv64
  ../smoke-test.c         the dlopen verification program, shared by all three
                          platforms
  ../test-vectors/        the conformance streams and their expected hashes
  ../output/              build results (git-ignored)
