================================================================================
dav1d-native-tools/windows - building dav1d.dll for win-x64 and win-arm64
================================================================================

>>> READ THIS FIRST <<<
--------------------------------------------------------------------------------
THE SCRIPTS IN THIS FOLDER HAVE NOT YET BEEN EXECUTED ON WINDOWS. They were
written on Linux on 2026-08-28, from dav1d's own meson.build, meson_options.txt
and documentation, and from the way this family's other native tooling works.
Every command in them is a considered one, but "considered" is not "verified" -
see WHAT HAS AND HAS NOT BEEN VERIFIED near the end of this document for the
exact list of assumptions.

When you first run them, expect to fix something. Fix it IN THE SCRIPT, and
commit that, rather than working around it by hand: the whole point of this
folder is that the next person - who may be you in three years - can rebuild
these libraries from this repository alone.


WHAT THIS IS
--------------------------------------------------------------------------------
Everything needed to build the two Windows native libraries this package ships,
from the dav1d source vendored in ..\dav1d\. Nothing is downloaded: not the
source, not the conformance streams, not the expected hashes. The only things
that come from outside are the tools you install on the build machine, and every
one of them is listed below with the command that installs it.

  win-x64     built with MSVC (cl) + nasm on an x64 Windows machine
  win-arm64   built with clang-cl on an ARM64 Windows machine (preferred), or
              cross-compiled from an x64 machine (see the two routes below)


================================================================================
PREREQUISITES
================================================================================
Listed in full, including things that are probably already on a developer's
machine - in a few years this will be a different machine.

  1. Visual Studio 2022 (or newer), or the standalone Build Tools, with:

       - Workload:  "Desktop development with C++"
       - Component: "MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools"
                    (only for win-arm64)
       - Component: "C++ Clang tools for Windows"
                    (only for win-arm64 - see WHY clang-cl below)

     Install through the Visual Studio Installer, or:
       winget install Microsoft.VisualStudio.2022.BuildTools

     Verify: open "x64 Native Tools Command Prompt for VS 2022" and run  cl
     Community, Professional, Enterprise and the standalone Build Tools all
     work.

     The scripts locate Visual Studio themselves with vswhere and set up the
     compiler environment on their own, so you can run them from any PowerShell
     prompt.

  2. Python 3.9 or newer, with pip.

       winget install Python.Python.3.12
     Verify: python --version  and  pip --version

  3. meson and ninja, at the versions pinned in ..\linux\pins.env:

       pip install meson==1.12.0 ninja==1.13.0

     Verify: meson --version   ninja --version
     (Those are the values pinned on 2026-08-28. Read pins.env rather than
     trusting this line - pins.env is the single source of truth, for all three
     platforms.)

  4. nasm 2.14 or newer - FOR win-x64 ONLY.

     dav1d's x86 SIMD is NASM syntax. Without nasm, meson does not fail: it
     quietly builds a C-only library several times slower, which defeats the
     purpose of using dav1d at all.

       Download the win64 zip from
         https://www.nasm.us/pub/nasm/releasebuilds/
       (e.g. .../2.16.03/win64/nasm-2.16.03-win64.zip), extract it somewhere
       permanent such as C:\Tools\nasm, and add that folder to PATH:

         setx PATH "%PATH%;C:\Tools\nasm"

       then open a NEW terminal (setx does not affect the current one).
       Or, if you prefer a package manager:  winget install NASM.NASM
       - check afterwards that nasm.exe really is on PATH; some packages
       install it without touching PATH.

     Verify: nasm -v

     NOT needed for win-arm64: nasm assembles x86 only.

  5. PowerShell 5.1 (in the box) or PowerShell 7+.

  NOT required: Perl, gas-preprocessor.pl, MSYS2, Cygwin, git-bash. The ARM64
  build avoids all of them by using clang-cl - see below.


================================================================================
USAGE
================================================================================
    cd dav1d-native-tools\windows
    .\build-win-x64.ps1

    .\build-win-arm64.ps1                       # on an ARM64 Windows machine
    .\build-win-arm64.ps1 -Route CrossFromX64   # on an x64 Windows machine

  Each script sets up its own developer environment (vcvarsall x64, arm64 or
  x64_arm64), so no special command prompt is needed.

  Output, git-ignored:
    ..\output\<rid>\dav1d.dll          the file the package ships
    ..\output\<rid>\dav1d.pdb          debug symbols - the Windows equivalent of
                                       keeping an unstripped copy. NOT shipped.
    ..\output\<rid>\LICENSE            dav1d's COPYING, verbatim
    ..\output\<rid>\BUILD-INFO.txt     toolchain, pins, size, sha256, conformance
    ..\output\<rid>\SHA256SUMS.txt     the DLL's hash on its own line
    ..\output\staging\<rid>\dav1d.dll.zip
                                       compressed copy for moving to whichever
                                       machine assembles the package (.zip, not
                                       .xz, because Compress-Archive is in the
                                       box on Windows and xz is not)


================================================================================
THE TWO ARM64 ROUTES, AND WHY clang-cl
================================================================================
dav1d's AArch64 SIMD is written in GNU-assembler syntax. Microsoft's assembler
(armasm64) does not speak it, so with cl the build has to route every .S file
through gas-preprocessor.pl - a Perl script from another project, fetched from
somewhere else. That is exactly the outside dependency this tooling exists to
avoid, and Perl is not on a normal Windows machine anyway.

clang-cl assembles those files directly, and dav1d knows it: meson.build only
reaches for gas-preprocessor when the compiler is NOT clang-cl (or when meson is
older than 0.58). So the ARM64 build here uses clang-cl - which arrives as a
Visual Studio component, not as another download - and needs no Perl at all.

  -Route Native (default) - run on an ARM64 Windows machine.
      Preferred, because the gate can RUN what it built: the LoadLibrary smoke
      test and the conformance decode both need to execute ARM64 code. A build
      that has not been executed has not really been verified.

  -Route CrossFromX64 - run on an x64 Windows machine.
      Uses vcvarsall x64_arm64 plus crossfile-win-arm64.txt. It produces a DLL
      and still runs the static checks (machine type, exports, dependents), but
      it CANNOT run the smoke test or the conformance decode. The script reports
      both as FAILURES rather than skipping them quietly, and exits non-zero: an
      unrun check is not a passed check. Copy the DLL to ARM64 hardware and
      finish the gate there before shipping it.


================================================================================
THE VERIFICATION GATE
================================================================================
The same gate as the Linux build, expressed with the tools Windows has. A build
that fails any check exits non-zero and must not be adopted.

  1. Machine type - dumpbin /headers must report x64 or ARM64, matching the RID.
     Catches the wrong file being published under the wrong RID, which is easy
     to do when one machine builds two targets.

  2. Required exports - dumpbin /exports must list all 17 entry points the
     managed binding declares: the 13 decoder functions (dav1d_version,
     dav1d_version_api, dav1d_default_settings, dav1d_open,
     dav1d_parse_sequence_header, dav1d_send_data, dav1d_get_picture,
     dav1d_apply_grain, dav1d_flush, dav1d_close, dav1d_get_event_flags,
     dav1d_get_decode_error_data_props, dav1d_get_frame_delay) plus the four
     data/picture lifetime helpers (dav1d_data_wrap, dav1d_data_create,
     dav1d_data_unref, dav1d_picture_unref). The list lives in build-common.ps1
     and must stay in step with ..\smoke-test.c, ..\linux\container-build.sh and
     the macOS scripts.

  3. Dependents - dumpbin /dependents may list KERNEL32.dll and nothing else.
     In particular NOTHING from the Visual C++ runtime: VCRUNTIME140.dll,
     MSVCP140.dll or any api-ms-win-crt-*.dll appearing there means
     -Db_vscrt=static_from_buildtype did not take, and the package would demand
     a Visual C++ Redistributable on every user's machine.

  4. LoadLibrary smoke test - ..\smoke-test.c is compiled with cl and run
     against the freshly built DLL. It loads the library the way .NET does
     (LoadLibrary + GetProcAddress, no link-time dependency), resolves all 17
     entry points, checks dav1d_version() and that dav1d_version_api() reports
     API major 7, then opens and closes a real decoder context - which starts
     the worker threads and runs CPU-feature detection.

  5. Conformance - the dav1d.exe built from this same source decodes every
     stream in ..\test-vectors\ with --muxer md5, and the hashes must equal
     ..\test-vectors\EXPECTED.md5. Those values were established by three
     independent decoders and are identical on every architecture; see
     ..\test-vectors\README.txt.

  There is no glibc-floor equivalent on Windows: the Windows ABI is stable
  across versions and the static CRT removes the redistributable question, which
  is what item 3 checks instead.


================================================================================
WHAT HAS AND HAS NOT BEEN VERIFIED
================================================================================
Verified (on Linux, from the vendored source itself):
  * The meson options are the ones dav1d accepts - they are the same options the
    three Linux builds used successfully on 2026-08-28, straight out of
    ..\linux\pins.env.
  * dav1d's meson.build really does skip gas-preprocessor for clang-cl with
    meson >= 0.58; the condition was read in the vendored source.
  * The 17 exported symbols really are exported by a release build (checked on
    all three Linux binaries).
  * The conformance vectors and their expected hashes are correct and
    architecture-independent (three decoders, three architectures agreed).

NOT verified - assumptions these scripts make:
  * That meson names the MSVC output dav1d.dll and puts it under <build>\src,
    and the CLI at <build>\tools\dav1d.exe. The scripts search recursively
    rather than hard-coding a path, which should absorb a different layout, but
    the names themselves are assumed.
  * That -Db_vscrt=static_from_buildtype leaves KERNEL32.dll as the only entry
    in dumpbin /dependents. If a release build still lists an api-ms-win-crt
    DLL, the gate will say so - that is the check working, not a bug in it.
  * The exact column layout of dumpbin /exports. The parser tries a strict
    match and falls back to a containment test for this reason.
  * That vcvarsall.bat arm64 / x64_arm64 exist and behave as documented on the
    machine in question.
  * That clang-cl in a current Visual Studio targets ARM64 by default when run
    from an ARM64 developer environment. If it does not, pass the target
    explicitly the way crossfile-win-arm64.txt does.
  * That the ARM64 build produces a dav1d.pdb. The script warns rather than
    failing if it does not.
  * Timings: unknown. On Linux this build takes 11 seconds natively; Windows
    should be the same order of magnitude.


================================================================================
TROUBLESHOOTING
================================================================================
"vswhere.exe was not found"
    No Visual Studio 2017-or-newer installer is present. See PREREQUISITES 1.

"The ARM64 C++ build tools are not installed in THIS Visual Studio instance"
    Exactly what it says - and note the word THIS. The check reads the
    filesystem of the selected installation on purpose. `vswhere -requires`
    searches EVERY Visual Studio instance on the machine, so it will happily
    report the ARM64 component present because a different instance has it,
    which says nothing about the one the build is driven from. Do not
    "simplify" the check back to vswhere; this family's tooling has been caught
    by that before.

meson reports it cannot find a C compiler
    The developer environment did not import. Check that vcvarsall.bat exists
    at the path the script printed, and try the equivalent command prompt by
    hand ("x64 Native Tools Command Prompt for VS 2022") to see the real error.

The build succeeds but the DLL is much bigger, or much slower, than expected
    nasm was not on PATH for the x64 build, so meson built a C-only library.
    Check the top of the build log: the script prints nasm's version, and stops
    if it is missing - if you see it running without that line, the check was
    bypassed.

dumpbin /dependents lists VCRUNTIME140.dll or api-ms-win-crt-*.dll
    The static CRT setting did not take. Confirm the build was configured with
    -Db_vscrt=static_from_buildtype (the script always passes it) and that the
    build directory was not a stale one from an earlier configuration. Delete
    the build directory and re-run.

"smoke-test.exe is not recognized"
    Some environments set NoDefaultCurrentDirectoryInExePath, so cmd will not
    run an executable from the current directory. build-common.ps1 invokes it as
    .\smoke-test.exe for exactly that reason - keep the leading .\ .

A conformance hash mismatch
    Take it seriously: this build decodes differently from the reference. Do not
    edit EXPECTED.md5 to make it pass. Compare with ffmpeg's libdav1d and libaom
    decoders on the same file - the commands are in ..\test-vectors\README.txt.


================================================================================
ADOPTING A BUILT BINARY INTO THE PACKAGE
================================================================================
  1. Read ..\output\<rid>\BUILD-INFO.txt and satisfy yourself it is the build
     you think it is: dav1d commit, toolchain, conformance hashes.

  2. Copy the library and its licence into the package's runtimes tree:

       mkdir ..\..\src\CodeBrix.VideoPlayback.Dav1d\runtimes\<rid>\native
       copy ..\output\<rid>\dav1d.dll ..\..\src\CodeBrix.VideoPlayback.Dav1d\runtimes\<rid>\native\
       copy ..\output\<rid>\LICENSE   ..\..\src\CodeBrix.VideoPlayback.Dav1d\runtimes\<rid>\native\

     <rid> is win-x64 or win-arm64. The LICENSE file is not optional:
     BSD-2-Clause clause 2 requires the copyright notice to travel with a binary
     distribution, and this is how it travels.

     Keep the name dav1d.dll. LibraryImport("dav1d") probes exactly that.

  3. Do NOT copy dav1d.pdb into the package - it is for crash triage and would
     multiply the package size. Keep it wherever build artefacts are kept.

  4. Record the build in ..\BUILD-PROVENANCE.txt, copying the values straight
     out of BUILD-INFO.txt.

  5. Run the managed test suite before publishing.


================================================================================
FILES
================================================================================
  README.txt                 this document
  build-common.ps1           shared machinery: pins, VS discovery, the gate
  build-win-x64.ps1          win-x64
  build-win-arm64.ps1        win-arm64, native or cross
  crossfile-win-arm64.txt    meson cross file for the cross route only
  ..\linux\pins.env          the pins - shared by all three platforms, so there
                             is exactly one file to edit. It lives in the linux
                             folder because that is where the container build
                             sources it as a shell script; the Windows and macOS
                             scripts parse the same file.
  ..\smoke-test.c            the load-and-run verification program, shared by
                             all three platforms
  ..\test-vectors\           conformance streams and expected hashes
  ..\output\                 build results (git-ignored)
