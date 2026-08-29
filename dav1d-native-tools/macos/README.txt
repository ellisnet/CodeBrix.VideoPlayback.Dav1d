================================================================================
dav1d-native-tools/macos - building libdav1d.dylib for osx-arm64 and osx-x64
================================================================================

>>> READ THIS FIRST <<<
--------------------------------------------------------------------------------
THE SCRIPTS IN THIS FOLDER HAVE NOT YET BEEN EXECUTED ON macOS. They were written
on Linux on 2026-08-28, from dav1d's own meson.build and documentation and from
the way this family's other native tooling works. Every command in them is a
considered one, but "considered" is not "verified" - see WHAT HAS AND HAS NOT
BEEN VERIFIED near the end for the exact list of assumptions.

When you first run them, expect to fix something. Fix it IN THE SCRIPT and
commit that, rather than working around it by hand: the point of this folder is
that these libraries can be rebuilt from this repository alone, years from now.


WHAT THIS IS
--------------------------------------------------------------------------------
Everything needed to build the two macOS native libraries this package ships,
from the dav1d source vendored in ../dav1d/. Nothing is downloaded: not the
source, not the conformance streams, not the expected hashes. The only things
from outside are the tools you install on the Mac, listed below with the command
that installs each one.

BOTH SLICES COME FROM ONE APPLE SILICON MAC.
  osx-arm64   native build                    ./build-osx-arm64.sh
  osx-x64     cross build, same machine       ./build-osx-x64.sh
              (clang -arch x86_64 via crossfile-x86_64.txt)

They stay TWO SEPARATE DYLIBS in two separate RID folders - deliberately not a
universal binary. The package's runtimes/osx-arm64/native/ and
runtimes/osx-x64/native/ folders each want their own file; a fat binary would
put both slices in both places and double the size of each for nothing.


================================================================================
PREREQUISITES
================================================================================
  1. Xcode Command Line Tools (clang, otool, nm, strip, install_name_tool,
     codesign, dsymutil).

       xcode-select --install

     Verify: cc --version     (should say "Apple clang")

  2. meson and ninja.

       brew install meson ninja

     Verify: meson --version   ninja --version
     The versions pinned in ../linux/pins.env (meson 1.12.0, ninja 1.13.0 as of
     2026-08-28) are what the Linux builds used. Homebrew will usually give you
     something newer; that is fine - dav1d needs meson >= 0.54 - but the build
     records the version it actually used in BUILD-INFO.txt, so a difference is
     never invisible. If you want the exact pinned versions:
         pip3 install meson==1.12.0 ninja==1.13.0

  3. nasm - FOR osx-x64 ONLY.

       brew install nasm

     Verify: nasm -v   (dav1d needs 2.14 or newer)
     dav1d's x86 SIMD is NASM syntax. Without nasm, meson does not fail: it
     quietly builds a C-only library several times slower, which defeats the
     purpose of using dav1d. Not needed for the arm64 slice - its assembly goes
     through clang's own assembler.

  4. Rosetta 2 - FOR osx-x64 ONLY, and only to VERIFY, not to build.

       softwareupdate --install-rosetta

     The gate has to RUN x86_64 code: the smoke test and the conformance decode.
     Without Rosetta those two checks cannot run on Apple Silicon, and
     build-osx-x64.sh reports them as FAILURES rather than skipping them
     quietly. Install Rosetta, or verify that binary on an Intel Mac before it
     ships.

  5. Homebrew itself, if you do not already have it: https://brew.sh


================================================================================
USAGE
================================================================================
    cd dav1d-native-tools/macos
    ./build-osx-arm64.sh
    ./build-osx-x64.sh

  Output, git-ignored:
    ../output/<rid>/libdav1d.dylib          the file the package ships
    ../output/<rid>/libdav1d.dylib.dSYM     debug symbols - macOS's equivalent
                                            of an unstripped copy. NOT shipped.
    ../output/<rid>/unstripped/libdav1d.dylib   the pre-strip binary
    ../output/<rid>/LICENSE                 dav1d's COPYING, verbatim
    ../output/<rid>/BUILD-INFO.txt          toolchain, pins, sizes, sha256,
                                            deployment target, conformance
    ../output/staging/<rid>/libdav1d.dylib.gz
                                            compressed copy for moving to
                                            whichever machine assembles the
                                            package (.gz because gzip is in the
                                            box on macOS and xz is not; the
                                            Linux staging folder uses .xz and
                                            the Windows one .zip for the same
                                            reason)


================================================================================
THE MINIMUM macOS VERSION, AND WHY IT IS 11.0
================================================================================
A Mach-O binary records the oldest macOS it will run on. With no explicit
deployment target, clang stamps in the version of the machine doing the
building - so a dylib built on a current Mac is refused by dyld on every older
one. It is the same failure mode as the glibc floor on Linux, and just as
invisible until a user hits it. The only fix is to state the floor explicitly.

Both scripts export MACOSX_DEPLOYMENT_TARGET=11.0, and the gate then CHECKS the
built file really carries it (otool -l, LC_BUILD_VERSION minos) rather than
merely reporting it - so a build on a newer Mac cannot quietly raise the floor.

Why 11.0 (Big Sur):
  * It is the oldest macOS that exists for Apple Silicon, so the arm64 slice
    cannot sensibly go lower.
  * Using the same number for the x86_64 slice gives the package ONE macOS floor
    instead of two, which is one less thing to get wrong.
  * It costs nothing in practice: the .NET runtime that loads this library has a
    macOS floor of its own that is at least this high, so the native library is
    never the component deciding how old a Mac can be.
  * If that ever stops being true, the x86_64 slice can be lowered on its own.
    It is a single value in macos/build-common.sh, and the gate enforces it.


================================================================================
THE VERIFICATION GATE
================================================================================
The same gate as the Linux build, expressed with the tools macOS has. A build
that fails any check exits non-zero and must not be adopted.

  1. Architecture - `file` must report arm64 or x86_64, matching the RID. One
     machine builds both slices, so this is the check that catches the wrong
     file being published under the wrong RID.

  2. Required exports - `nm -gU` must list all 17 entry points the managed
     binding declares: the 13 decoder functions (dav1d_version,
     dav1d_version_api, dav1d_default_settings, dav1d_open,
     dav1d_parse_sequence_header, dav1d_send_data, dav1d_get_picture,
     dav1d_apply_grain, dav1d_flush, dav1d_close, dav1d_get_event_flags,
     dav1d_get_decode_error_data_props, dav1d_get_frame_delay) plus the four
     data/picture lifetime helpers (dav1d_data_wrap, dav1d_data_create,
     dav1d_data_unref, dav1d_picture_unref). Mach-O prefixes C symbols with an
     underscore, so they are matched as _dav1d_*. The list lives in
     build-common.sh and must stay in step with ../smoke-test.c,
     ../linux/container-build.sh and the Windows scripts.

  3. Install name - `otool -D` must report @rpath/libdav1d.dylib. A dylib whose
     install name is an absolute build-machine path loads only on the machine
     that built it.

  4. Dependencies - `otool -L` may list /usr/lib/libSystem.B.dylib and nothing
     else. dav1d genuinely has no other dependencies, so anything else means
     something was linked that the user would have to install.

  5. Deployment target - CHECKED against 11.0, not merely reported. See above.

  6. Code signature - `codesign -dv` must find one. Apple Silicon refuses to
     load an unsigned dylib outright. Note the ORDER in the scripts: strip, then
     install_name_tool, then codesign. The first two invalidate a signature, so
     signing has to be last - and the linker's own ad-hoc signature (which it
     applies to arm64 output but NOT to x86_64) is gone by then either way.

  7. dlopen smoke test - ../smoke-test.c is compiled for the target arch and run
     against the freshly built dylib. It loads the library the way .NET does
     (dlopen + dlsym, no link-time dependency), resolves all 17 entry points,
     checks dav1d_version() and that dav1d_version_api() reports API major 7,
     then opens and closes a real decoder context - which starts the worker
     threads and runs CPU-feature detection.

  8. Conformance - the dav1d CLI built from this same source decodes every
     stream in ../test-vectors/ with --muxer md5, and the hashes must equal
     ../test-vectors/EXPECTED.md5. Those values were established by three
     independent decoders and are identical on every architecture; see
     ../test-vectors/README.txt.

  For osx-x64 on Apple Silicon, checks 7 and 8 need Rosetta 2. Without it they
  are reported as FAILURES, never as passes or skips.


================================================================================
WHAT HAS AND HAS NOT BEEN VERIFIED
================================================================================
Verified (on Linux, from the vendored source itself):
  * The meson options are the ones dav1d accepts - the same options the three
    Linux builds used successfully on 2026-08-28, straight out of
    ../linux/pins.env.
  * The 17 exported symbols really are exported by a release build (checked on
    all three Linux binaries; on macOS they will carry a leading underscore).
  * The conformance vectors and their expected hashes are correct and
    architecture-independent (three decoders, three architectures agreed).
  * dav1d ships no macOS cross file of its own - crossfile-x86_64.txt here was
    written for this repository, not copied from upstream.

NOT verified - assumptions these scripts make:
  * That meson puts the dylib under <build>/src as libdav1d.<something>.dylib
    and the CLI at <build>/tools/dav1d. The scripts search rather than
    hard-coding a path, but the names are assumed.
  * That `stat -f %z` (BSD stat) is the right form - it is on macOS, but these
    scripts have never been run there.
  * That the LC_BUILD_VERSION parsing in build-common.sh matches what current
    otool prints. There is a fallback for the older LC_VERSION_MIN_MACOSX form.
  * The Rosetta 2 detection (an oahd process or the libRosettaRuntime file).
    If it gets this wrong the effect is a false FAILURE, not a false pass -
    which is the safe direction.
  * That clang accepts `-arch x86_64` through the meson cross file's binaries
    entry in the list form used in crossfile-x86_64.txt.
  * Timings: unknown. On Linux this build takes 11 seconds natively.


================================================================================
TROUBLESHOOTING
================================================================================
"meson: command not found" after brew install
    Homebrew's bin directory is not on PATH for this shell. On Apple Silicon
    that is /opt/homebrew/bin.  eval "$(/opt/homebrew/bin/brew shellenv)"

The build succeeds but the x64 dylib is much slower than expected
    nasm was not installed, so meson built a C-only library. The script requires
    nasm and stops without it - if you got here, the check was bypassed.

"minimum macOS is <version>, expected 11.0"
    The dylib was not built with MACOSX_DEPLOYMENT_TARGET. Almost always a stale
    build directory; both scripts remove theirs and export the variable on every
    run, so use the scripts rather than configuring meson by hand. Do NOT
    "fix" this by relaxing the check - the value it guards is the oldest macOS
    the shipped package will load on.

"no code signature"
    The explicit codesign call failed or was removed. It cannot be left to the
    linker: the linker ad-hoc signs arm64 output only, and stripping plus
    install_name_tool invalidate whatever signature exists. Sign last.

"smoke test NOT RUN" on osx-x64
    Rosetta 2 is not installed. softwareupdate --install-rosetta, then re-run.
    This is reported as a failure on purpose; an unrun check is not a passed
    check.

A conformance hash mismatch
    Take it seriously: this build decodes differently from the reference. Do not
    edit EXPECTED.md5 to make it pass. Compare with ffmpeg's libdav1d and libaom
    decoders on the same file - the commands are in ../test-vectors/README.txt.


================================================================================
ADOPTING A BUILT BINARY INTO THE PACKAGE
================================================================================
  1. Read ../output/<rid>/BUILD-INFO.txt and satisfy yourself it is the build
     you think it is: dav1d commit, toolchain, deployment target, conformance
     hashes.

  2. Copy the library and its licence into the package's runtimes tree:

       mkdir -p ../../src/CodeBrix.VideoPlayback.Dav1d/runtimes/<rid>/native
       cp ../output/<rid>/libdav1d.dylib \
          ../../src/CodeBrix.VideoPlayback.Dav1d/runtimes/<rid>/native/
       cp ../output/<rid>/LICENSE \
          ../../src/CodeBrix.VideoPlayback.Dav1d/runtimes/<rid>/native/

     <rid> is osx-arm64 or osx-x64. The LICENSE file is not optional:
     BSD-2-Clause clause 2 requires the copyright notice to travel with a binary
     distribution, and this is how it travels.

     Keep the name libdav1d.dylib - unversioned. LibraryImport("dav1d") probes
     exactly that.

  3. Do NOT copy the .dSYM or unstripped/ into the package. They are for crash
     triage and would multiply the package size.

  4. Record the build in ../BUILD-PROVENANCE.txt, copying the values straight
     out of BUILD-INFO.txt.

  5. Run the managed test suite before publishing.


================================================================================
FILES
================================================================================
  README.txt                this document
  build-common.sh           shared machinery: pins, prerequisites, the gate
  build-osx-arm64.sh        osx-arm64, native
  build-osx-x64.sh          osx-x64, cross-compiled on the same Mac
  crossfile-x86_64.txt      the meson cross file the x64 build uses
  ../linux/pins.env         the pins - shared by all three platforms, so there
                            is exactly one file to edit. It lives in the linux
                            folder because that is where the container build
                            sources it; these scripts source the same file.
  ../smoke-test.c           the dlopen verification program, shared by all three
                            platforms
  ../test-vectors/          conformance streams and expected hashes
  ../output/                build results (git-ignored)
