# =============================================================================================
# build-win-arm64.ps1 - build dav1d.dll for the win-arm64 runtime identifier
# =============================================================================================
#
#   FIRST RUN 2026-08-29: the CrossFromX64 route BUILT CLEANLY and passed every check that an
#   x64 host can perform. The Native route has still never been executed - no ARM64 Windows
#   machine has run this yet. Three fixes were needed on that first run, all of them in the
#   cross route's toolchain plumbing; crossfile-win-arm64.txt documents two of them at length
#   and they are easy to undo by "tidying". Read it before editing.
#
# TWO ROUTES
#
#   -Route Native        (default)  Run this ON AN ARM64 WINDOWS MACHINE. meson sees a native
#                                   build, and - the part that matters - the gate can actually
#                                   RUN the built binaries: the LoadLibrary smoke test and the
#                                   conformance decode both need to execute ARM64 code.
#
#   -Route CrossFromX64             Run on an x64 Windows machine using vcvarsall x64_arm64 and
#                                   crossfile-win-arm64.txt. It produces a DLL, but the smoke
#                                   test and the conformance decode CANNOT run, and this script
#                                   reports them as FAILURES rather than skipping them quietly.
#                                   Use it to produce a binary, then verify that binary on ARM64
#                                   hardware before shipping it.
#
# WHY clang-cl EITHER WAY
#   dav1d's AArch64 assembly is GNU-assembler syntax. With MSVC's cl, meson routes it through
#   gas-preprocessor.pl + armasm64 - which needs Perl and a script from outside this repository,
#   exactly what this tooling exists to avoid. clang-cl assembles those .S files directly, and
#   dav1d's meson.build knows it (see the use_gaspp condition in meson.build). So this script
#   requires Visual Studio's "C++ Clang tools for Windows" component and uses clang-cl.
#
# USAGE
#     cd dav1d-native-tools\windows
#     .\build-win-arm64.ps1                        # on an ARM64 Windows machine
#     .\build-win-arm64.ps1 -Route CrossFromX64    # on an x64 Windows machine
#
# Output: ..\output\win-arm64\  (dav1d.dll, dav1d.pdb, LICENSE-Dav1d.txt, BUILD-INFO.txt, SHA256SUMS.txt)
# =============================================================================================

[CmdletBinding()]
param(
    [ValidateSet('Native', 'CrossFromX64')]
    [string] $Route = 'Native',

    [string] $BuildRoot = (Join-Path $env:TEMP 'dav1d-build-win-arm64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\build-common.ps1"

$toolsDir    = Split-Path -Parent $PSScriptRoot
$rid         = 'win-arm64'
$pins        = Read-Pins (Join-Path $toolsDir 'linux\pins.env')
$sourceDir   = Join-Path $toolsDir $pins['DAV1D_DIR']
$patchDir    = Join-Path $toolsDir 'patches'
$scratchDir  = Join-Path $env:TEMP 'dav1d-src-win-arm64'
$outDir      = Join-Path $toolsDir "output\$rid"
$vectorDir   = Join-Path $toolsDir $pins['TEST_VECTOR_DIR']
$expected    = Join-Path $toolsDir $pins['TEST_VECTOR_EXPECTED']
$smokeSource = Join-Path $toolsDir 'smoke-test.c'
$crossFile   = Join-Path $PSScriptRoot 'crossfile-win-arm64.txt'
$startedAt   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss') + ' UTC'
$stopwatch   = [System.Diagnostics.Stopwatch]::StartNew()
$hostIsArm64 = ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64')

Write-Host '=============================================================================='
Write-Host " dav1d $($pins['DAV1D_VERSION']) ($($pins['DAV1D_DESCRIBE'])) - $rid  [route: $Route]"
Write-Host '=============================================================================='
Write-Host "  started  : $startedAt"
Write-Host "  host     : $env:PROCESSOR_ARCHITECTURE, $((Get-CimInstance Win32_OperatingSystem).Caption)"
Write-Host "  source   : $sourceDir  (vendored - nothing is downloaded)"
Write-Host ''

if ($Route -eq 'Native' -and -not $hostIsArm64) {
    throw @"
-Route Native must run on an ARM64 Windows machine; this one reports
PROCESSOR_ARCHITECTURE=$env:PROCESSOR_ARCHITECTURE.

Either run this on ARM64 hardware, or use:
    .\build-win-arm64.ps1 -Route CrossFromX64
and be aware that the cross route cannot run the smoke test or the conformance decode - it
reports both as failures, and the resulting binary must be verified on ARM64 hardware before it
ships.
"@
}

# ---------------------------------------------------------------------------------------------
# 1. Prerequisites
# ---------------------------------------------------------------------------------------------
Write-Host '--- prerequisites ---'
$vsPath    = Find-VisualStudio
$vcvarsall = Get-VcVarsAllPath $vsPath
Write-Host "  Visual Studio: $vsPath"

# Read the filesystem of THIS installation - never `vswhere -requires`, which searches every
# instance on the machine and will report a component present because some other instance has it.
if (-not (Test-Arm64ToolsPresent $vsPath)) {
    throw @"
The ARM64 C++ build tools are not installed in THIS Visual Studio instance:
    $vsPath

Add them in the Visual Studio Installer: "MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools".
(If another Visual Studio instance on this machine has them, that does not help - this is the
instance the build is driven from.)
"@
}
if (-not (Test-ClangClPresent $vsPath)) {
    throw @"
clang-cl was not found in this Visual Studio instance:
    $vsPath

Add "C++ Clang tools for Windows" in the Visual Studio Installer. It is required: with MSVC's
cl, dav1d's AArch64 assembly has to go through gas-preprocessor.pl + armasm64, which needs Perl
and a script from outside this repository. clang-cl assembles it directly.
"@
}

$vcvarsArch = if ($Route -eq 'Native') { 'arm64' } else { 'x64_arm64' }
Import-DeveloperEnvironment -VcVarsAll $vcvarsall -ArchArgument $vcvarsArch

$clangClPath = Assert-OnPath 'clang-cl' 'Add "C++ Clang tools for Windows" in the Visual Studio Installer.'
$mesonPath = Assert-OnPath 'meson' @"
Install it with pip, at the version pinned in pins.env:
    pip install meson==$($pins['MESON_VERSION']) ninja==$($pins['NINJA_VERSION'])
"@
$ninjaPath = Assert-OnPath 'ninja' "Install it with pip:  pip install ninja==$($pins['NINJA_VERSION'])"
$dumpbinPath = Assert-OnPath 'dumpbin' 'dumpbin ships with the C++ toolset; it should be on PATH inside the developer environment.'

# No nasm on this architecture: nasm assembles x86 SIMD only. AArch64 assembly goes through
# clang-cl.
Write-Host "  clang-cl: $clangClPath"
Write-Host "  meson   : $((& meson --version)) (pinned $($pins['MESON_VERSION']))  [$mesonPath]"
Write-Host "  ninja   : $((& ninja --version)) (pinned $($pins['NINJA_VERSION']))  [$ninjaPath]"
Write-Host "  dumpbin : $dumpbinPath"
Write-Host "  nasm    : not needed on ARM64"
Write-Host ''

if (-not (Test-Path -LiteralPath $expected)) {
    throw "The expected-hash file $expected is missing. The conformance gate cannot run without it."
}

# ---------------------------------------------------------------------------------------------
# 2. Source + build
# ---------------------------------------------------------------------------------------------
Write-Host '--- source ---'
$patchesApplied = Copy-SourceToScratch -SourceDir $sourceDir -ScratchDir $scratchDir -PatchDir $patchDir
Write-Host "  patches applied: $patchesApplied"
Write-Host ''

Write-Host '--- building ---'
$extra = @()
if ($Route -eq 'Native') {
    # meson picks the compiler from CC/CXX. clang-cl on an ARM64 host targets ARM64 by default.
    $env:CC = 'clang-cl'
    $env:CXX = 'clang-cl'
}
else {
    # CL is MSVC's "extra arguments" environment variable, which clang-cl also honours. It is
    # what makes meson's COMPILER DETECTION see an ARM64 target: the probe runs clang-cl bare,
    # and an x64-hosted clang-cl otherwise reports x86_64-pc-windows-msvc, from which meson
    # derives /MACHINE:x64 and then fails to archive the arm64 objects it just produced. The
    # cross file cannot fix this on its own - see the long comment at the top of it for why the
    # obvious alternative (putting --target in the [binaries] exelist) breaks the resource
    # compiler instead. Both pieces are required.
    $env:CL = '--target=aarch64-pc-windows-msvc'
    Write-Host "  CL=$env:CL  (so meson's compiler probe reports an ARM64 target)"
    $extra += @('--cross-file', $crossFile)
}
Invoke-MesonBuild -ScratchDir $scratchDir -BuildDir $BuildRoot -MesonOptions $pins['DAV1D_MESON_OPTIONS'] -ExtraMesonArgs $extra
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 3. Collect
# ---------------------------------------------------------------------------------------------
Write-Host '--- collecting ---'
$builtDll = Get-ChildItem -LiteralPath (Join-Path $BuildRoot 'src') -Filter 'dav1d.dll' -Recurse |
            Select-Object -First 1
if (-not $builtDll) { throw "No dav1d.dll was produced under $BuildRoot\src." }
$builtCli = Get-ChildItem -LiteralPath (Join-Path $BuildRoot 'tools') -Filter 'dav1d.exe' -Recurse |
            Select-Object -First 1
if (-not $builtCli) { throw "The dav1d CLI was not built; the conformance gate needs it." }

if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Copy-Item -LiteralPath $builtDll.FullName -Destination (Join-Path $outDir 'dav1d.dll')
$builtPdb = Join-Path $builtDll.DirectoryName 'dav1d.pdb'
if (Test-Path -LiteralPath $builtPdb) {
    Copy-Item -LiteralPath $builtPdb -Destination (Join-Path $outDir 'dav1d.pdb')
    Write-Host '  dav1d.pdb kept beside the DLL (crash triage; not shipped)'
}
else {
    Write-Host '  [warn] no dav1d.pdb was produced - a crash dump from this build will be unreadable.'
}

Copy-Item -LiteralPath (Join-Path $sourceDir 'COPYING') -Destination (Join-Path $outDir 'LICENSE-Dav1d.txt')
Write-Host '  LICENSE-Dav1d.txt  : dav1d COPYING copied beside the binary'
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 4. The gate
# ---------------------------------------------------------------------------------------------
Write-Host '--- verifying ---'
$dllPath = Join-Path $outDir 'dav1d.dll'
$canRun = ($Route -eq 'Native')
$summary = Test-Dav1dBinary -DllPath $dllPath `
                            -ExpectedMachine 'ARM64' `
                            -SmokeTestSource $smokeSource `
                            -VectorDir $vectorDir `
                            -ExpectedFile $expected `
                            -CliPath $builtCli.FullName `
                            -CanRunTargetBinaries $canRun

# Distinguish "could not be checked here" from "checked and wrong". On the cross route the two
# NOT RUN entries are expected and are the only acceptable failures; anything else means the
# binary itself is bad and no record of it should be written.
$notRunOnly = ($script:GateFailures.Count -gt 0) -and
              (-not ($script:GateFailures | Where-Object { $_ -notlike 'smoke test NOT RUN*' -and $_ -notlike 'conformance NOT RUN*' }))
$crossIncomplete = (-not $canRun) -and $notRunOnly

if ($script:GateFailures.Count -gt 0 -and -not $crossIncomplete) {
    Write-Host ''
    Write-Host "VERIFICATION FAILED for $rid - see above. $outDir is left for inspection, but this"
    Write-Host 'build must not be adopted into the package, and no BUILD-INFO.txt is written for it.'
    exit 1
}
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 5. Record
# ---------------------------------------------------------------------------------------------
$sha = Get-Sha256 $dllPath
$stopwatch.Stop()

# A cross build is recorded, but it is recorded as UNFINISHED. Writing nothing at all was the
# original behaviour and it was worse: the run produced a DLL with no sha256, no BUILD-INFO and
# no staged archive, so there was nothing to put in BUILD-PROVENANCE.txt and nothing convenient
# to carry to the ARM64 machine. Recording it with the truth at the top is more useful than
# recording nothing, as long as the file can never be mistaken for a passed gate.
$gateStatus = if ($crossIncomplete) {
@"
*** INCOMPLETE - DO NOT SHIP ON THE STRENGTH OF THIS FILE ***
                   Cross-built on an x64 host. The static checks passed, but the
                   smoke test and the conformance decodes COULD NOT RUN here:
                   an x64 machine cannot execute ARM64 code. They are unrun, not
                   passed. Finish the gate on ARM64 hardware (see README.txt,
                   FINISHING A CROSS-BUILT win-arm64 ON ARM64 HARDWARE) and
                   update this file and ..\..\BUILD-PROVENANCE.txt with the
                   result before the binary is shipped.
"@
} else {
    'complete - every check ran and passed on this machine'
}

$pdbPath = Join-Path $outDir 'dav1d.pdb'
$debugSymbols = if (Test-Path -LiteralPath $pdbPath) { 'dav1d.pdb beside it (not shipped)' }
                else { 'none - meson --buildtype=release asks for no debug info, so link.exe produced no .pdb' }

$buildInfo = @"
dav1d native library - build information
==============================================================================
RID              : $rid
Route            : $Route
Gate status      : $gateStatus
Built            : $startedAt
Build duration   : $([int]$stopwatch.Elapsed.TotalSeconds)s
Built by         : dav1d-native-tools\windows\build-win-arm64.ps1

Build machine
------------------------------------------------------------------------------
OS               : $((Get-CimInstance Win32_OperatingSystem).Caption) ($env:PROCESSOR_ARCHITECTURE)
Visual Studio    : $vsPath
Compiler         : clang-cl ($clangClPath)
meson            : $((& meson --version)) (pinned $($pins['MESON_VERSION']))
ninja            : $((& ninja --version)) (pinned $($pins['NINJA_VERSION']))

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
dav1d            : $($pins['DAV1D_VERSION'])  ($($pins['DAV1D_DESCRIBE']))
Commit           : $($pins['DAV1D_COMMIT'])
API version      : $($pins['DAV1D_API_VERSION'])
Patches applied  : $patchesApplied

Configuration
------------------------------------------------------------------------------
meson options    : $($pins['DAV1D_MESON_OPTIONS']) -Db_vscrt=static_from_buildtype
                   (static CRT: no Visual C++ Redistributable is required)
AArch64 assembly : assembled by clang-cl directly (no gas-preprocessor, no Perl)

Result
------------------------------------------------------------------------------
File             : dav1d.dll
Size             : $((Get-Item -LiteralPath $dllPath).Length) bytes
SHA256           : $sha
Debug symbols    : $debugSymbols
Licence beside it: LICENSE-Dav1d.txt (a verbatim copy of dav1d's COPYING, BSD-2-Clause)

Conformance (dav1d.exe built from this same source, --muxer md5)
------------------------------------------------------------------------------
$(if ($summary) { $summary -join "`n" } else { '  NOT RUN - see Gate status above.' })
"@

Set-Content -LiteralPath (Join-Path $outDir 'BUILD-INFO.txt') -Value $buildInfo -Encoding UTF8
Set-Content -LiteralPath (Join-Path $outDir 'SHA256SUMS.txt') -Value "$sha  dav1d.dll" -Encoding UTF8

# Stage the CLI as well as the library on the cross route. Finishing the gate on ARM64 hardware
# means running the conformance decodes, and those need the ARM64 dav1d.exe built from this same
# source - which otherwise exists only in the temporary build directory and is thrown away.
$stagingDir = Join-Path $toolsDir "output\staging\$rid"
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
Compress-Archive -Path $dllPath -DestinationPath (Join-Path $stagingDir 'dav1d.dll.zip') -Force
if ($crossIncomplete) {
    Copy-Item -LiteralPath $builtCli.FullName -Destination (Join-Path $outDir 'dav1d.exe') -Force
    Compress-Archive -Path @($dllPath, (Join-Path $outDir 'dav1d.exe')) `
                     -DestinationPath (Join-Path $stagingDir 'win-arm64-gate.zip') -Force
    Write-Host "  staged the ARM64 dav1d.exe too, so the gate can be finished on ARM64 hardware"
}

Write-Host '--- done ---'
Write-Host "  $dllPath"
Write-Host "  sha256 $sha"
Write-Host "  staged $stagingDir\dav1d.dll.zip"
Write-Host "  $([int]$stopwatch.Elapsed.TotalSeconds)s"

if ($crossIncomplete) {
    Write-Host ''
    Write-Host '=============================================================================='
    Write-Host " $rid IS BUILT BUT NOT FULLY VERIFIED"
    Write-Host '=============================================================================='
    Write-Host '  Passed here : architecture, required exports, system-only dependencies'
    Write-Host '  NOT RUN     : LoadLibrary smoke test, conformance decodes'
    Write-Host '                (an x64 machine cannot execute ARM64 code - unrun, not passed)'
    Write-Host ''
    Write-Host "  Carry $stagingDir\win-arm64-gate.zip to an ARM64 Windows"
    Write-Host '  machine and follow README.txt, "FINISHING A CROSS-BUILT win-arm64 ON ARM64'
    Write-Host '  HARDWARE". Exiting non-zero: an unrun check is not a passed check.'
    exit 1
}
Write-Host ''
Write-Host 'To adopt this binary into the package, follow ADOPTING A BUILT BINARY in README.txt.'
