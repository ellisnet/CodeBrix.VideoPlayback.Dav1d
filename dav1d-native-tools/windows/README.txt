================================================================================
dav1d-native-tools/windows - building dav1d.dll for win-x64 and win-arm64
================================================================================

STATUS
--------------------------------------------------------------------------------
FIRST REAL RUN: 2026-08-29, on Windows 11 Pro x64 with Visual Studio Professional
2026 (18.9). These scripts were written on Linux on 2026-08-28 and had never been
executed before that day.

  win-x64     BUILT AND PASSED THE COMPLETE GATE. Every check ran on the build
              machine, including all seven conformance decodes.

  win-arm64   CROSS-BUILT from the same x64 machine (-Route CrossFromX64). It
              passed every check an x64 host is able to perform - architecture,
              exports, dependencies - and could NOT run the smoke test or the
              conformance decodes, because an x64 machine cannot execute ARM64
              code. Those two are UNRUN, not passed, and the script exits 1 to
              say so. See FINISHING A CROSS-BUILT win-arm64 below.

  -Route Native (build on an ARM64 Windows machine) has STILL never been run.

Four things needed fixing on that first run. All four fixes are in this folder
and are described in WHAT THE FIRST REAL RUN ESTABLISHED. Three of them are
subtle enough that a well-meaning tidy-up would reintroduce them, so the comments
that explain them are load-bearing - please do not compress them away.


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
machine - in a few years this will be a different machine. The versions in
brackets are what the 2026-08-29 build actually used, recorded so a future run
can tell "newer than what worked" from "older than what worked".

  1. Visual Studio 2022 or newer, or the standalone Build Tools, with:

       - Workload:  "Desktop development with C++"
       - Component: "MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools"
                    (only for win-arm64; on VS 2026 the equivalent component
                    yields the 14.5x toolset - the scripts check the filesystem
                    for an arm64 cl.exe, not a version number, so either works)
       - Component: "C++ Clang tools for Windows"
                    (only for win-arm64 - see WHY clang-cl below)

     [used: Visual Studio Professional 2026, 18.9.12120.119, MSVC 14.51.36231,
      clang-cl 22.1.3, Windows SDK 10.0.26100]

     Install through the Visual Studio Installer, or:
       winget install Microsoft.VisualStudio.2022.BuildTools

     To add the two extra components to an installation you already have, either
     use the Installer's Modify button, or from an ELEVATED prompt:

       "C:\Program Files (x86)\Microsoft Visual Studio\Installer\setup.exe" modify
         --installPath "<path to the instance>"
         --add Microsoft.VisualStudio.Component.VC.Llvm.Clang
         --add Microsoft.VisualStudio.Component.VC.Tools.ARM64
         --quiet --norestart

     THE COMPONENTS MUST BE IN THE INSTANCE THE BUILD ACTUALLY USES. The scripts
     select an instance with `vswhere -latest` and then check THAT instance's
     filesystem. If you have several Visual Studios, adding clang to the wrong
     one changes nothing - see TROUBLESHOOTING.

     The scripts locate Visual Studio themselves and set up the compiler
     environment on their own, so you can run them from any PowerShell prompt.

  2. Python 3.9 or newer, with pip.   [used: 3.14.7, pip 26.2.1]

       winget install Python.Python.3.12
     Verify: python --version  and  pip --version

  3. meson and ninja, at the versions pinned in ..\linux\pins.env:

       pip install --user meson==1.12.0 ninja==1.13.0

     [used: meson 1.12.0, ninja 1.13.0 - the wheel reports itself as
      "1.13.0.git.kitware.jobserver-pipe-1", which is that version]

     --user avoids needing an elevated prompt when Python lives under
     C:\Program Files. Check that the resulting Scripts directory is on PATH -
     for a per-user install it is %APPDATA%\Python\PythonXXX\Scripts.

     Verify: meson --version   ninja --version
     (Those are the values pinned on 2026-08-28. Read pins.env rather than
     trusting this line - pins.env is the single source of truth, for all three
     platforms.)

  4. nasm 2.14 or newer - FOR win-x64 ONLY.   [used: 3.02]

     dav1d's x86 SIMD is NASM syntax. Without nasm, meson does not fail: it
     quietly builds a C-only library several times slower, which defeats the
     purpose of using dav1d at all. (The script refuses to run without it, so
     this can only bite you if that check is bypassed.)

       winget install NASM.NASM --source winget

     --source winget matters: without it winget may first demand you accept the
     msstore source agreement, which fails outright in a non-interactive shell.
     The installer is an NSIS package and raises a UAC prompt; you do not need
     to start the terminal elevated, only to approve that prompt.

     IT DOES NOT PUT ITSELF ON PATH. As of 2026-08-29 winget installs it
     per-user to %LOCALAPPDATA%\bin\NASM and touches neither the user nor the
     machine PATH. Add it yourself, from PowerShell, no elevation needed:

       [Environment]::SetEnvironmentVariable('PATH',
         [Environment]::GetEnvironmentVariable('PATH','User') + ';' +
         "$env:LOCALAPPDATA\bin\NASM", 'User')

     then open a NEW terminal. DO NOT use  setx PATH "%PATH%;..."  - %PATH% in a
     normal shell expands to the machine PATH and the user PATH concatenated, so
     that command silently copies the entire system PATH into your user PATH,
     and setx truncates the result at 1024 characters. The .NET call above
     touches only the user value and has no length limit.

     ON NASM 3.x. 3.02 is a major version newer than the 2.15.03 used for
     linux-x64, and newer than dav1d 1.5.4 itself. It was chosen deliberately,
     to match what macos/ used for osx-x64, and it assembled dav1d's x86 SIMD
     without complaint: all seven conformance decodes matched. dav1d's own floor
     check (meson.build, out[2].version_compare('<2.14')) compares numerically
     and accepts it. If a future nasm ever does break the build, the conformance
     gate is what will catch it - do not silence it.

     Verify: nasm -v
     NOT needed for win-arm64: nasm assembles x86 only.

  5. PowerShell 5.1 (in the box) or PowerShell 7+.   [used: 7.6.5]

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

  EXIT CODES. 0 means every check ran and passed. Non-zero means it did not -
  and for  -Route CrossFromX64  a non-zero exit is the NORMAL, EXPECTED result,
  because two of the five checks cannot run on an x64 host. Read the summary the
  script prints at the end rather than the exit code alone.

  Timings on the 2026-08-29 machine, from a cold scratch copy:
    win-x64    about 28 seconds (119 ninja targets)
    win-arm64  about 15 seconds (fewer targets: no x86 asm to assemble)

  Output, git-ignored:
    ..\output\<rid>\dav1d.dll          the file the package ships
    ..\output\<rid>\LICENSE-Dav1d.txt            dav1d's COPYING, verbatim
    ..\output\<rid>\BUILD-INFO.txt     toolchain, pins, size, sha256, conformance
    ..\output\<rid>\SHA256SUMS.txt     the DLL's hash on its own line
    ..\output\<rid>\dav1d.exe          CROSS ROUTE ONLY - the ARM64 CLI, kept so
                                       the gate can be finished on ARM64 hardware
    ..\output\staging\<rid>\dav1d.dll.zip
                                       compressed copy for moving to whichever
                                       machine assembles the package (.zip, not
                                       .xz, because Compress-Archive is in the
                                       box on Windows and xz is not)
    ..\output\staging\win-arm64\win-arm64-gate.zip
                                       CROSS ROUTE ONLY - dll + CLI together

  THERE IS NO dav1d.pdb. meson's --buildtype=release asks for no debug
  information, so MSVC emits none. The scripts warn about this rather than
  failing, and BUILD-INFO.txt records it honestly. If symbols are ever wanted
  for crash triage, build with --buildtype=debugoptimized - but note that is a
  pins.env change and would affect all three platforms, so think before doing it.


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
CONFIRMED on 2026-08-29: the cross build assembled every .S file with clang-cl
and gas-preprocessor was never invoked.

  -Route Native (default) - run on an ARM64 Windows machine.
      Preferred, because the gate can RUN what it built: the LoadLibrary smoke
      test and the conformance decode both need to execute ARM64 code. A build
      that has not been executed has not really been verified.
      NEVER YET RUN. Expect to fix something, as with the cross route.

  -Route CrossFromX64 - run on an x64 Windows machine.
      Uses vcvarsall x64_arm64 plus crossfile-win-arm64.txt, and additionally
      sets the CL environment variable - see crossfile-win-arm64.txt for why
      both are needed. It produces a DLL and runs the static checks, but it
      CANNOT run the smoke test or the conformance decode. The script reports
      both as FAILURES rather than skipping them quietly, and exits non-zero: an
      unrun check is not a passed check. It still writes BUILD-INFO.txt, with
      the incompleteness stated at the top, and stages the ARM64 dav1d.exe so
      the gate can be finished elsewhere.


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
     CONFIRMED on both binaries, 2026-08-29: KERNEL32.dll and nothing else.

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
     The staged DLL is copied next to the CLI before this runs. That is not
     tidiness - see THE PATH TRAP below.

  There is no glibc-floor equivalent on Windows: the Windows ABI is stable
  across versions and the static CRT removes the redistributable question, which
  is what item 3 checks instead.


================================================================================
WHAT THE FIRST REAL RUN ESTABLISHED (2026-08-29)
================================================================================
Four fixes, in the order they were hit. Each one is commented at the site of the
fix as well as described here.

1. THE PATH TRAP - the conformance CLI decoded with the wrong dav1d.dll.
   Fixed in build-common.ps1.

   meson puts dav1d.dll in <build>\src but dav1d.exe in <build>\tools. Nothing
   sits beside the executable, so Windows' DLL search order falls past the
   executable's own directory and reaches PATH. On the build machine PATH
   contained GStreamer, which ships its own dav1d.dll at API 6.8.0. The API-7
   CLI asked it for entry points it does not have and the process died with
   0xC0000139 STATUS_ENTRYPOINT_NOT_FOUND before reaching main.

   The gate therefore reported seven conformance MISMATCHES with an empty
   decoded hash - which reads like a broken decoder and is nothing of the kind.
   The library being tested was fine; it was never consulted.

   THE FIX: copy the staged DLL - the exact file the package ships - next to the
   CLI before the decodes. The executable's own directory is first in the search
   order and cannot be displaced by PATH. Do not replace this with a PATH edit:
   PATH is what caused the bug. The gate also now distinguishes "no output at
   all" from "wrong hash" and prints the CLI's exit code, so the next person is
   told it is a launch failure rather than sent hunting for a decoder bug.

   This one is not specific to GStreamer. Any dav1d.dll on PATH - from ffmpeg,
   OBS, VLC, a media SDK - would do the same thing.

2. THE STATIC LINKER GOT /MACHINE:x64 ON AN ARM64 CROSS BUILD.
   Fixed in build-win-arm64.ps1 (it sets CL) - explained at length in
   crossfile-win-arm64.txt.

   meson derives the /MACHINE: flag it hands llvm-lib from the target it
   DETECTS, and it detects that by running the compiler bare and reading its
   "Target:" banner. A --target in the cross file's c_args reaches compilation
   but not that probe. So an x64-hosted clang-cl produced correct arm64 objects
   and then tried to archive them as x64:

       input_input.c.obj: file machine type arm64 conflicts with
                          library machine type x64

   THE FIX: set the CL environment variable, which clang-cl honours, so the
   probe itself reports an AArch64 target.

3. ...AND THEN GOT /MACHINE:arm, WHICH IS 32-BIT ARM.
   Same files.

   meson canonicalises the detected triple with an if/elif chain that tests for
   the literal substring 'aarch64' BEFORE 'arm'. 'arm64-pc-windows-msvc'
   contains no 'aarch64', so it fell through to the 32-bit branch:

       input_input.c.obj: file machine type arm64 conflicts with
                          library machine type arm

   THE FIX: spell the triple aarch64-pc-windows-msvc. clang treats the two
   spellings as the same target and echoes back whichever it was given; meson
   does not. Do not "tidy" it to arm64.

   A note on the road not taken: putting --target in the cross file's [binaries]
   entry (c = ['clang-cl', '--target=...']) also fixes the detection, and was
   tried first. It breaks the Windows resource compiler instead - meson invokes
   that through a wrapper taking ONE value for --cl, so the second element leaks
   past it to rc.exe:  fatal error RC1106: invalid option. The exelist must stay
   a single binary.

4. A CROSS BUILD RECORDED NOTHING.
   Fixed in build-win-arm64.ps1.

   The script exited at the gate, before writing BUILD-INFO.txt, SHA256SUMS.txt
   or the staged archive. So the one route that most needs a paper trail - the
   one whose output has to travel to another machine to be finished - produced a
   DLL with no recorded hash, nothing to copy into BUILD-PROVENANCE.txt, and no
   ARM64 dav1d.exe with which to finish the gate.

   THE FIX: an incomplete cross build now writes its record, with the
   incompleteness stated in a "Gate status" field at the top of BUILD-INFO.txt,
   and stages the CLI alongside the library. It still exits 1. A build that
   fails for any OTHER reason still writes nothing, because that would be a bad
   binary rather than an unfinished one.

ALSO ESTABLISHED, no fix needed:

  * The meson options in pins.env are accepted unchanged by MSVC and clang-cl.
  * -Db_vscrt=static_from_buildtype does take: KERNEL32.dll is the only
    dependent of both DLLs.
  * meson does name the MSVC output dav1d.dll under <build>\src, and the CLI
    <build>\tools\dav1d.exe, as the scripts assumed.
  * The dumpbin /exports parser handled the real output; the fallback path was
    not needed.
  * vcvarsall.bat x64 and x64_arm64 both behave as documented on VS 2026.
  * clang-cl detects DOTPROD, I8MM, SVE and SVE2 as available, so the ARM64
    binary contains the full SIMD set rather than a baseline subset.
  * dav1d builds cleanly with nasm 3.02 and produces conforming output.

  * WIN-X64 IS NOT BYTE-REPRODUCIBLE, and this is expected. Two builds from the
    same source, minutes apart, differed in exactly 4 bytes out of 2,002,432:

        offset 272          the COFF TimeDateStamp
        offsets 352-353     the PE CheckSum, which changes because the
                            timestamp it covers changed
        offset 1952788      the export directory's own timestamp

    All code and data were identical. link.exe stamps the wall clock into the
    image; there is a /Brepro flag that replaces the timestamp with a content
    hash, but it is not passed, so DO NOT expect the sha256 in
    ..\BUILD-PROVENANCE.txt to reproduce. Compare sizes and the gate result
    instead, and if you want certainty, diff the two files and confirm only
    those four offsets differ. (Compare with macos/README.txt, where osx-x64 is
    reproducible except for its LC_UUID - a different cause, same conclusion.)

STILL NOT VERIFIED:

  * -Route Native has never been executed. Everything it does differently from
    the cross route - vcvarsall arm64, clang-cl targeting ARM64 by default from
    an ARM64 developer environment, CC/CXX rather than a cross file - is still
    an assumption.
  * Whether an ARM64 build produces a dav1d.pdb. The x64 one does not, for
    reasons that apply equally, so probably not.
  * The cross-built win-arm64 binary has never been EXECUTED. That is the whole
    point of the section below.


================================================================================
FINISHING A CROSS-BUILT win-arm64 ON ARM64 HARDWARE
================================================================================
A cross-built win-arm64 has passed three of the five checks. The remaining two -
the LoadLibrary smoke test and the seven conformance decodes - must be run on an
ARM64 Windows machine before the binary is shipped. Two ways, and they answer
different questions.

  A. VERIFY THE ACTUAL ARTEFACT (what the cross route is designed for)

     Copy ..\output\staging\win-arm64\win-arm64-gate.zip to the ARM64 machine
     and unpack it; it holds the DLL that was cross-built and the ARM64
     dav1d.exe built from the same source. You also need this repository, for
     the test vectors and smoke-test.c. Then, on the ARM64 machine:

       # 1. the smoke test - needs a C compiler (VS with the C++ workload)
       cl /nologo /O2 /Fe:smoke-test.exe path\to\dav1d-native-tools\smoke-test.c
       .\smoke-test.exe path\to\dav1d.dll

       # 2. conformance - put the DLL beside the CLI first, see THE PATH TRAP
       copy path\to\dav1d.dll .
       # then, for every non-comment line of ..\test-vectors\EXPECTED.md5,
       # which is  <vector>|<flags>|<expected md5>  :
       .\dav1d.exe -i ..\test-vectors\<vector> --muxer md5 -o - <flags>
       # and compare each printed hash with that line's third field.

     All seven must match. Then record the result in ..\BUILD-PROVENANCE.txt and
     replace the "Gate status" block in output\win-arm64\BUILD-INFO.txt.

  B. REBUILD NATIVELY (the stronger check, if the machine has the toolchain)

       .\build-win-arm64.ps1

     This runs the whole gate end to end and exits 0 if everything passes. It
     verifies THE TOOLCHAIN AND THE SOURCE rather than the specific file that
     was cross-built, so it is the better answer to "does dav1d work on ARM64?"
     and not an answer to "is this exact DLL good?". It needs Visual Studio with
     the C++ workload and clang-cl, plus meson, ninja and Python, on the ARM64
     machine. If you do this and it passes, prefer the natively built binary and
     retire the cross-built one - a fully gated build beats a partially gated
     one.


================================================================================
TROUBLESHOOTING
================================================================================
Conformance decodes produce an EMPTY hash, or the CLI exits with 0xC0000139
    Another dav1d.dll is being loaded from PATH. This is THE PATH TRAP above and
    the gate now prevents it by copying the staged DLL beside the CLI. If you
    see it anyway, something removed that copy. 0xC0000135
    (STATUS_DLL_NOT_FOUND) is the same class of problem. Confirm with:
        $env:PATH -split ';' | ForEach-Object {
          if (Test-Path "$_\dav1d.dll") { "$_\dav1d.dll" } }

"vswhere.exe was not found"
    No Visual Studio 2017-or-newer installer is present. See PREREQUISITES 1.

"The ARM64 C++ build tools are not installed in THIS Visual Studio instance"
or "clang-cl was not found in this Visual Studio instance"
    Exactly what they say - and note the word THIS. The check reads the
    filesystem of the selected installation on purpose. `vswhere -requires`
    searches EVERY Visual Studio instance on the machine, so it will happily
    report the component present because a different instance has it, which says
    nothing about the one the build is driven from. Do not "simplify" the check
    back to vswhere; this family's tooling has been caught by that before.
    To see which instance will be used:
        & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath

meson reports it cannot find a C compiler
    The developer environment did not import. Check that vcvarsall.bat exists
    at the path the script printed, and try the equivalent command prompt by
    hand ("x64 Native Tools Command Prompt for VS 2022") to see the real error.

"file machine type arm64 conflicts with library machine type x64" (or "arm")
    The cross build's target detection is broken again. See items 2 and 3 of
    WHAT THE FIRST REAL RUN ESTABLISHED, and the long comment in
    crossfile-win-arm64.txt. Almost always caused by someone moving --target out
    of the CL environment variable, or respelling aarch64 as arm64.

"fatal error RC1106: invalid option: --target=..."
    Someone put --target into the cross file's [binaries] entry. meson's
    resource-compiler wrapper takes one value for --cl. Keep the exelist a
    single binary and carry the target in CL. See crossfile-win-arm64.txt.

"lld-link: warning: ignoring unknown argument '--target=...'"
    Someone added c_link_args back to the cross file. Harmless, but it is noise
    that says nothing true - lld-link is a linker, not a compiler driver. The
    link gets its architecture from /MACHINE and from the objects.

nasm is not on PATH even though winget said it installed
    winget installs it per-user to %LOCALAPPDATA%\bin\NASM and does not touch
    PATH. See PREREQUISITES 4, including why not to use setx.

The build succeeds but the DLL is much bigger, or much slower, than expected
    nasm was not on PATH for the x64 build, so meson built a C-only library.
    Check the top of the build log: the script prints nasm's version, and stops
    if it is missing - if you see it running without that line, the check was
    bypassed. For reference, a correct x64 build is about 2.00 MB and an ARM64
    one about 0.90 MB.

dumpbin /dependents lists VCRUNTIME140.dll or api-ms-win-crt-*.dll
    The static CRT setting did not take. Confirm the build was configured with
    -Db_vscrt=static_from_buildtype (the script always passes it) and that the
    build directory was not a stale one from an earlier configuration. Delete
    the build directory and re-run.

"smoke-test.exe is not recognized"
    Some environments set NoDefaultCurrentDirectoryInExePath, so cmd will not
    run an executable from the current directory. build-common.ps1 invokes it as
    .\smoke-test.exe for exactly that reason - keep the leading .\ .

The sha256 does not match BUILD-PROVENANCE.txt
    Expected on Windows. See the reproducibility note in WHAT THE FIRST REAL RUN
    ESTABLISHED: link.exe stamps a timestamp, so two builds of identical source
    differ in four bytes. Check the size and the gate result instead.

A conformance hash mismatch (with a real, non-empty hash)
    Take it seriously: this build decodes differently from the reference. Do not
    edit EXPECTED.md5 to make it pass. Rule out THE PATH TRAP first - an empty
    hash is a launch failure, not a mismatch - then compare with ffmpeg's
    libdav1d and libaom decoders on the same file; the commands are in
    ..\test-vectors\README.txt.

The script exits 1 but everything looks fine
    If it is -Route CrossFromX64, that IS the correct outcome. Read the block it
    prints at the end: the two unrun checks are counted as failures on purpose.


================================================================================
ADOPTING A BUILT BINARY INTO THE PACKAGE
================================================================================
  1. Read ..\output\<rid>\BUILD-INFO.txt and satisfy yourself it is the build
     you think it is: dav1d commit, toolchain, conformance hashes. If its
     "Gate status" field says INCOMPLETE, finish the gate first - see FINISHING
     A CROSS-BUILT win-arm64 above.

  2. Copy the library and its licence into the package's runtimes tree:

       mkdir ..\..\src\CodeBrix.VideoPlayback.Dav1d\runtimes\<rid>\native
       copy ..\output\<rid>\dav1d.dll ..\..\src\CodeBrix.VideoPlayback.Dav1d\runtimes\<rid>\native\
       copy ..\output\<rid>\LICENSE-Dav1d.txt   ..\..\src\CodeBrix.VideoPlayback.Dav1d\runtimes\<rid>\native\

     <rid> is win-x64 or win-arm64. The LICENSE file is not optional:
     BSD-2-Clause clause 2 requires the copyright notice to travel with a binary
     distribution, and this is how it travels.

     Keep the name dav1d.dll. LibraryImport("dav1d") probes exactly that.

  3. Do NOT copy dav1d.exe (staged for the cross route only) into the package.
     There is no dav1d.pdb to worry about with the current pins.

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
  crossfile-win-arm64.txt    meson cross file for the cross route only. Read its
                             header before editing it - it documents two traps
                             that a tidy-up would walk straight back into.
  ..\linux\pins.env          the pins - shared by all three platforms, so there
                             is exactly one file to edit. It lives in the linux
                             folder because that is where the container build
                             sources it as a shell script; the Windows and macOS
                             scripts parse the same file.
  ..\smoke-test.c            the load-and-run verification program, shared by
                             all three platforms
  ..\test-vectors\           conformance streams and expected hashes
  ..\output\                 build results (git-ignored)
