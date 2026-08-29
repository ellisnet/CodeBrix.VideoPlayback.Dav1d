================================================================================
dav1d-native-tools/macos - building libdav1d.dylib for osx-arm64 and osx-x64
================================================================================

>>> STATUS: RUN AND VERIFIED <<<
--------------------------------------------------------------------------------
BOTH macOS SLICES WERE BUILT AND ADOPTED ON 2026-08-29. The scripts were written
on Linux on 2026-08-28 and had never been executed on a Mac; on their first real
run both built clean and passed the full gate, and NO script needed fixing. The
"expect to fix something" warning that used to stand here has been earned out.

  Build machine : Apple Silicon Mac, macOS 26.5.1, Apple clang 21.0.0
  Tools         : meson 1.12.0, ninja 1.13.2, nasm 3.02, Rosetta 2 present
  osx-arm64     : 5-7s,  798,304 bytes stripped, gate fully passed
  osx-x64       : ~24s, 1,679,264 bytes stripped, gate fully passed

Two things that run taught us, both harmless but both surprising the first time
you see them - read WHAT HAS AND HAS NOT BEEN VERIFIED before worrying about
either:
  * osx-x64 is NOT byte-reproducible. It alternates between two outputs that
    differ only in the 16-byte LC_UUID and the signature over it. osx-arm64 IS
    byte-identical across runs.
  * The osx-x64 link prints dozens of `ld: warning: no platform load command
    found in ... .obj, assuming: macOS`. Expected; the gate proves it is benign.

If a future run does need a change, fix it IN THE SCRIPT and commit that, rather
than working around it by hand: the point of this folder is that these libraries
can be rebuilt from this repository alone, years from now.


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

     USED ON 2026-08-29: meson 1.12.0 (Homebrew happened to match the pin
     exactly) and ninja 1.13.2 (newer than the 1.13.0 pin - no effect observed).

  3. nasm - FOR osx-x64 ONLY.

       brew install nasm

     Verify: nasm -v   (dav1d needs 2.14 or newer)
     dav1d's x86 SIMD is NASM syntax. Without nasm, meson does not fail: it
     quietly builds a C-only library several times slower, which defeats the
     purpose of using dav1d. Not needed for the arm64 slice - its assembly goes
     through clang's own assembler.

     USED ON 2026-08-29: nasm 3.02, a major version newer than the 2.15.03 the
     Linux x64 build used. It assembled dav1d's x86 SIMD without complaint and
     the result passed conformance. It is, however, the source of both macOS
     quirks noted at the top of this file.

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
    ./build-osx-arm64.sh        # about 5-7 seconds
    ./build-osx-x64.sh          # about 24 seconds

  Each script is self-contained and idempotent: it removes its own scratch and
  build directories on every run, so re-running is always safe and never needs a
  manual clean. Neither installs anything.

  Output, git-ignored:
    ../output/<rid>/libdav1d.dylib          the file the package ships
    ../output/<rid>/libdav1d.dylib.dSYM     debug symbols - macOS's equivalent
                                            of an unstripped copy. NOT shipped.
    ../output/<rid>/unstripped/libdav1d.dylib   the pre-strip binary
    ../output/<rid>/LICENSE-Dav1d.txt                 dav1d's COPYING, verbatim
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
Everything that was listed here as an assumption on 2026-08-28 was settled by the
2026-08-29 run on macOS 26.5.1. Each one held:

  * meson does put the dylib at <build>/src/libdav1d.7.dylib and the CLI at
    <build>/tools/dav1d. (Upstream declares library('dav1d') in src/meson.build
    and executable('dav1d') in tools/meson.build, so the scripts' `find ... -type
    f` - which also skips meson's unversioned symlink - is right by construction,
    not by luck.)
  * `stat -f %z` is correct on macOS.
  * The LC_BUILD_VERSION parsing matches what current otool prints; it returns
    exactly "11.0" and compares equal. The LC_VERSION_MIN_MACOSX fallback was
    never needed.
  * The Rosetta 2 detection is right: `pgrep -q oahd` succeeds, and the
    /Library/Apple/usr/libexec/oah/libRosettaRuntime fallback also exists.
  * clang accepts `-arch x86_64` through the cross file's list form. As a bonus,
    dav1d's meson uses no cc.run() anywhere, so the cross file needing no
    needs_exe_wrapper setting is safe rather than merely untested.
  * Timings: osx-arm64 5-7s, osx-x64 ~24s. (Linux native was 11s.)
  * All 17 exports are present as _dav1d_* Mach-O symbols on both slices, and
    both slices reproduce all seven architecture-independent conformance hashes.

TWO THINGS THE RUN TURNED UP THAT NOBODY HAD PREDICTED
--------------------------------------------------------------------------------
1. osx-x64 IS NOT BYTE-REPRODUCIBLE. osx-arm64 IS.

   Measured over six consecutive from-scratch runs. osx-arm64 produced one
   sha256 every time. osx-x64 ALTERNATED between exactly two:
       runs 1, 3, 5 -> cb876e7513e4264337a5976302b8a07dc4e956fa2561140fd92b62ca52d11ee5
       runs 2, 4, 6 -> a823a1bc794c46c4500117faf062495d77cb844906859e1c32246508347a3f27

   The two differ in 76 bytes and nothing else:
       16 bytes - the LC_UUID payload, at file offset 1569-1584
       60 bytes - inside the ad-hoc code signature, which necessarily changes
                  because it hashes the LC_UUID
   Every byte of code and data is identical. Both variants pass the whole gate.

   CAUSE. The difference is upstream of the strip. The UNSTRIPPED link output
   differs by 712 bytes: one variant carries 19 extra local debug symbols
   (FGData, FGData.seed, FGData.num_y_points ... FGData_size) that nasm 3.02
   emits from the STRUCT macros in dav1d's x86 film-grain assembly, and the other
   does not. `strip -x` removes those local symbols either way - which is why the
   shipped files match - but ld64 has ALREADY derived the content-based LC_UUID
   from the pre-strip image by then.

   WHY IT IS LEFT ALONE. The honest options are to drop the UUID entirely
   (`-Wl,-no_uuid`), which would make crash reports from shipped x64 binaries
   much harder to symbolicate, or to chase nasm. Neither is worth it for 16 bytes
   that do not affect execution. What matters is that the claim in
   ../BUILD-PROVENANCE.txt is accurate: linux-* are byte-reproducible, osx-arm64
   is byte-reproducible, osx-x64 is reproducible EXCEPT for LC_UUID.

   If you rebuild osx-x64 and get a different sha256 from the one recorded in
   ../BUILD-PROVENANCE.txt, this is why. Confirm it is only the UUID before
   concluding anything is wrong:
       cmp -l old.dylib new.dylib | wc -l          # expect 76
       otool -l <dylib> | grep -A2 LC_UUID

2. THE osx-x64 LINK IS NOISY, AND THE NOISE IS BENIGN.

   The link prints one of these per nasm-produced object, dozens in all:
       ld: warning: no platform load command found in '...mc_sse.obj',
           assuming: macOS
   nasm's macho64 output carries no LC_BUILD_VERSION, so the linker says it is
   assuming macOS - which is correct. It does NOT weaken the deployment target:
   the gate independently checks the finished dylib and finds minos 11.0. Nothing
   to fix; do not silence it by relaxing the gate.


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

"ld: warning: no platform load command found in ... .obj, assuming: macOS"
    Expected on osx-x64, dozens of times, one per nasm object. Harmless - see
    item 2 of WHAT HAS AND HAS NOT BEEN VERIFIED. The gate checks the finished
    dylib's deployment target independently and it is 11.0.

The osx-x64 sha256 does not match ../BUILD-PROVENANCE.txt
    Expected, and not a problem, if the ONLY difference is the LC_UUID. See item
    1 of WHAT HAS AND HAS NOT BEEN VERIFIED for the two known hashes and how to
    confirm it. osx-arm64, by contrast, must match exactly - if that one differs,
    something real has changed and you should find out what before shipping it.

"could not find nasm" / the x64 build configures as C-only
    Homebrew's bin is not on PATH for this shell: eval "$(/opt/homebrew/bin/brew
    shellenv)". The script requires nasm and stops without it, so a C-only x64
    build should not be reachable through the script.


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
       cp ../output/<rid>/LICENSE-Dav1d.txt \
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

WHAT WAS ADOPTED ON 2026-08-29
--------------------------------------------------------------------------------
Both macOS slices are in the package tree as of 2026-08-29, copied exactly as
described above and re-verified after the copy (arch, install name, minos 11.0,
`codesign -v` valid, all 17 exports present):

  src/CodeBrix.VideoPlayback.Dav1d/runtimes/osx-arm64/native/libdav1d.dylib
      sha256 3d35c38606a565b530913f05f9cfc6e58fd5f3ce3a8fd52517f29070331482f2
      798,304 bytes
  src/CodeBrix.VideoPlayback.Dav1d/runtimes/osx-x64/native/libdav1d.dylib
      sha256 a823a1bc794c46c4500117faf062495d77cb844906859e1c32246508347a3f27
      1,679,264 bytes   (LC_UUID variant - see WHAT HAS AND HAS NOT BEEN
                         VERIFIED; the other variant is equally valid)

with dav1d's COPYING beside each one as LICENSE-Dav1d.txt. Copying preserves the ad-hoc
signature - verified with `codesign -v` after the copy, which is worth repeating
whenever these files are moved between machines, because some transports do not.

Still outstanding for this package: win-x64 and win-arm64. The three linux-*
binaries were built on 2026-08-29 but have not been adopted into runtimes/ yet.


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
