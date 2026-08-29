# =============================================================================================
# build-win-x64.ps1 - build dav1d.dll for the win-x64 runtime identifier
# =============================================================================================
#
#   >>> NOT YET EXECUTED ON WINDOWS. <<<
#   Written on Linux on 2026-08-28 from dav1d's own meson.build and documentation, and never
#   run on a Windows machine. Read README.txt, "WHAT HAS AND HAS NOT BEEN VERIFIED", first.
#   When you run it, fix what is wrong HERE rather than working around it by hand.
#
# USAGE (from any PowerShell prompt - the script sets up the compiler environment itself):
#
#     cd dav1d-native-tools\windows
#     .\build-win-x64.ps1
#
# Output: ..\output\win-x64\  (dav1d.dll, dav1d.pdb, LICENSE, BUILD-INFO.txt, SHA256SUMS.txt)
#
# It installs nothing. Anything missing is named, with the command that installs it, and the
# script stops.
# =============================================================================================

[CmdletBinding()]
param(
    # Where meson builds. Kept out of the repository so the vendored source and the output tree
    # stay clean.
    [string] $BuildRoot = (Join-Path $env:TEMP 'dav1d-build-win-x64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\build-common.ps1"

$toolsDir    = Split-Path -Parent $PSScriptRoot          # dav1d-native-tools\
$rid         = 'win-x64'
$pins        = Read-Pins (Join-Path $toolsDir 'linux\pins.env')
$sourceDir   = Join-Path $toolsDir $pins['DAV1D_DIR']
$patchDir    = Join-Path $toolsDir 'patches'
$scratchDir  = Join-Path $env:TEMP 'dav1d-src-win-x64'
$outDir      = Join-Path $toolsDir "output\$rid"
$vectorDir   = Join-Path $toolsDir $pins['TEST_VECTOR_DIR']
$expected    = Join-Path $toolsDir $pins['TEST_VECTOR_EXPECTED']
$smokeSource = Join-Path $toolsDir 'smoke-test.c'
$startedAt   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss') + ' UTC'
$stopwatch   = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host '=============================================================================='
Write-Host " dav1d $($pins['DAV1D_VERSION']) ($($pins['DAV1D_DESCRIBE'])) - $rid"
Write-Host '=============================================================================='
Write-Host "  started  : $startedAt"
Write-Host "  host     : $env:PROCESSOR_ARCHITECTURE, $((Get-CimInstance Win32_OperatingSystem).Caption)"
Write-Host "  source   : $sourceDir  (vendored - nothing is downloaded)"
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 1. Prerequisites, all of them, before anything is compiled.
# ---------------------------------------------------------------------------------------------
Write-Host '--- prerequisites ---'
$vsPath    = Find-VisualStudio
$vcvarsall = Get-VcVarsAllPath $vsPath
Write-Host "  Visual Studio: $vsPath"

Import-DeveloperEnvironment -VcVarsAll $vcvarsall -ArchArgument 'x64'

$clPath = Assert-OnPath 'cl' 'Install the Visual Studio 2022 Build Tools with the "Desktop development with C++" workload.'
$mesonPath = Assert-OnPath 'meson' @"
Install it with pip, at the version pinned in pins.env:
    pip install meson==$($pins['MESON_VERSION']) ninja==$($pins['NINJA_VERSION'])
"@
$ninjaPath = Assert-OnPath 'ninja' "Install it with pip:  pip install ninja==$($pins['NINJA_VERSION'])"
$nasmPath  = Assert-OnPath 'nasm' @"
nasm assembles dav1d's x86 SIMD, which is the entire reason to use dav1d rather than a C
decoder. Without it meson silently builds a much slower C-only library.
Download nasm $($pins['NASM_MIN_VERSION']) or newer from https://www.nasm.us/pub/nasm/releasebuilds/
(the win64 .zip), extract it, and put the folder on PATH.
"@
$dumpbinPath = Assert-OnPath 'dumpbin' 'dumpbin ships with the C++ toolset; it should be on PATH inside the developer environment.'

Write-Host "  cl      : $clPath"
Write-Host "  meson   : $((& meson --version)) (pinned $($pins['MESON_VERSION']))  [$mesonPath]"
Write-Host "  ninja   : $((& ninja --version)) (pinned $($pins['NINJA_VERSION']))  [$ninjaPath]"
Write-Host "  nasm    : $((& nasm -v))  [$nasmPath]"
Write-Host "  dumpbin : $dumpbinPath"
Write-Host ''

if (-not (Test-Path -LiteralPath $expected)) {
    throw "The expected-hash file $expected is missing. The conformance gate cannot run without it, and a build that skips conformance is not a build worth shipping."
}

# ---------------------------------------------------------------------------------------------
# 2. Source + build
# ---------------------------------------------------------------------------------------------
Write-Host '--- source ---'
$patchesApplied = Copy-SourceToScratch -SourceDir $sourceDir -ScratchDir $scratchDir -PatchDir $patchDir
Write-Host "  patches applied: $patchesApplied"
Write-Host ''

Write-Host '--- building ---'
Invoke-MesonBuild -ScratchDir $scratchDir -BuildDir $BuildRoot -MesonOptions $pins['DAV1D_MESON_OPTIONS']
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 3. Collect. meson names the MSVC shared library dav1d.dll (no lib prefix, no version suffix),
#    which is exactly the name LibraryImport("dav1d") probes for. The .pdb is the Windows
#    equivalent of keeping an unstripped copy: without it a crash dump is unreadable. It is kept
#    beside the DLL here and is NOT shipped in the package.
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

# BSD-2-Clause clause 2 is satisfied by shipping dav1d's own COPYING beside the binary.
Copy-Item -LiteralPath (Join-Path $sourceDir 'COPYING') -Destination (Join-Path $outDir 'LICENSE')
Write-Host '  LICENSE  : dav1d COPYING copied beside the binary'
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 4. The gate
# ---------------------------------------------------------------------------------------------
Write-Host '--- verifying ---'
$dllPath = Join-Path $outDir 'dav1d.dll'
$summary = Test-Dav1dBinary -DllPath $dllPath `
                            -ExpectedMachine 'x64' `
                            -SmokeTestSource $smokeSource `
                            -VectorDir $vectorDir `
                            -ExpectedFile $expected `
                            -CliPath $builtCli.FullName `
                            -CanRunTargetBinaries $true

if ($script:GateFailures.Count -gt 0) {
    Write-Host ''
    Write-Host "VERIFICATION FAILED for $rid. $outDir is left for inspection, but this build must"
    Write-Host 'not be adopted into the package.'
    exit 1
}
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 5. Record
# ---------------------------------------------------------------------------------------------
$sha = Get-Sha256 $dllPath
$stopwatch.Stop()

$buildInfo = @"
dav1d native library - build information
==============================================================================
RID              : $rid
Built            : $startedAt
Build duration   : $([int]$stopwatch.Elapsed.TotalSeconds)s
Built by         : dav1d-native-tools\windows\build-win-x64.ps1

Build machine
------------------------------------------------------------------------------
OS               : $((Get-CimInstance Win32_OperatingSystem).Caption) ($env:PROCESSOR_ARCHITECTURE)
Visual Studio    : $vsPath
cl               : $((& cl 2>&1 | Select-Object -First 1))
meson            : $((& meson --version)) (pinned $($pins['MESON_VERSION']))
ninja            : $((& ninja --version)) (pinned $($pins['NINJA_VERSION']))
nasm             : $((& nasm -v))

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
dav1d            : $($pins['DAV1D_VERSION'])  ($($pins['DAV1D_DESCRIBE']))
Commit           : $($pins['DAV1D_COMMIT'])
API version      : $($pins['DAV1D_API_VERSION'])
Patches applied  : $patchesApplied

Configuration
------------------------------------------------------------------------------
meson options    : $($pins['DAV1D_MESON_OPTIONS']) -Db_vscrt=static_from_buildtype
                   (static CRT: no Visual C++ Redistributable is required on the
                    user's machine)

Result
------------------------------------------------------------------------------
File             : dav1d.dll
Size             : $((Get-Item -LiteralPath $dllPath).Length) bytes
SHA256           : $sha
Debug symbols    : dav1d.pdb beside it (not shipped)
Licence beside it: LICENSE (a verbatim copy of dav1d's COPYING, BSD-2-Clause)

Conformance (dav1d.exe built from this same source, --muxer md5)
------------------------------------------------------------------------------
$($summary -join "`n")
"@

Set-Content -LiteralPath (Join-Path $outDir 'BUILD-INFO.txt') -Value $buildInfo -Encoding UTF8
Set-Content -LiteralPath (Join-Path $outDir 'SHA256SUMS.txt') -Value "$sha  dav1d.dll" -Encoding UTF8

# Staging: a compressed copy for moving the binary to whichever machine assembles the package.
# .zip rather than .xz because Compress-Archive is in the box on Windows and xz is not; the
# Linux staging folder uses .xz for the same reason in reverse.
$stagingDir = Join-Path $toolsDir "output\staging\$rid"
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
Compress-Archive -Path $dllPath -DestinationPath (Join-Path $stagingDir 'dav1d.dll.zip') -Force

Write-Host '--- done ---'
Write-Host "  $dllPath"
Write-Host "  sha256 $sha"
Write-Host "  staged $stagingDir\dav1d.dll.zip"
Write-Host "  $([int]$stopwatch.Elapsed.TotalSeconds)s"
Write-Host ''
Write-Host 'To adopt this binary into the package, follow ADOPTING A BUILT BINARY in README.txt.'
