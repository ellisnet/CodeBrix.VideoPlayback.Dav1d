# =============================================================================================
# build-common.ps1 - shared machinery for the Windows dav1d builds
# =============================================================================================
#
#   FIRST RUN 2026-08-29 on Windows 11 Pro x64, Visual Studio Professional 2026 (18.9).
#   win-x64 passed the complete gate; win-arm64 cross-built and passed every check an x64 host
#   can run. The conformance step needed a real fix and it is marked in place below - the CLI
#   must decode with the DLL this build produced, not whichever dav1d.dll happens to be on
#   PATH. Read README.txt, "WHAT THE FIRST REAL RUN ESTABLISHED", before editing.
#
# Dot-source this from an architecture script; do not run it directly.
#
#     . "$PSScriptRoot\build-common.ps1"
#
# Everything it needs is in this repository: ..\dav1d (vendored source), ..\smoke-test.c,
# ..\test-vectors\ (conformance streams + EXPECTED.md5), ..\linux\pins.env (the pins, shared by
# all three platforms - see README.txt for why they live in the linux folder).
# =============================================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------
# Pins. One source of truth for every platform: ..\linux\pins.env. Only the keys that are not
# Linux-specific are used here.
# ---------------------------------------------------------------------------------------------
function Read-Pins {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "pins.env was not found at $Path. It is part of the repository - if it is missing after a clone, check the root .gitignore's blanket '*.env' rule (see README.txt, TROUBLESHOOTING)."
    }

    $pins = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $trimmed.Substring(0, $eq).Trim()
        $value = $trimmed.Substring($eq + 1).Trim()
        if ($value.Length -ge 2 -and $value.StartsWith('"') -and $value.EndsWith('"')) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $pins[$key] = $value
    }
    return $pins
}

# ---------------------------------------------------------------------------------------------
# Visual Studio discovery.
#
# IMPORTANT: component checks read the filesystem of the SELECTED installation, never
# `vswhere -requires`. A machine often has more than one VS instance (say VS Build Tools next to
# a full VS), and -requires searches ALL of them: it will happily report the ARM64 compiler as
# present because some OTHER instance has it, which says nothing about the one being used here.
# That exact conflation has produced mysterious build failures in this family's tooling before.
# ---------------------------------------------------------------------------------------------
function Find-VisualStudio {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw @"
vswhere.exe was not found at
    $vswhere
which means no Visual Studio 2017-or-newer installer is present. Install the Visual Studio 2022
Build Tools with the "Desktop development with C++" workload - see README.txt, PREREQUISITES.
"@
    }

    $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installPath) {
        throw 'No Visual Studio installation with the C++ toolset was found. Install the "Desktop development with C++" workload - see README.txt, PREREQUISITES.'
    }
    return ($installPath | Select-Object -First 1)
}

function Get-VcVarsAllPath {
    param([Parameter(Mandatory)][string] $VsInstallPath)

    $vcvarsall = Join-Path $VsInstallPath 'VC\Auxiliary\Build\vcvarsall.bat'
    if (-not (Test-Path -LiteralPath $vcvarsall)) {
        throw "vcvarsall.bat was not found in $VsInstallPath. The C++ workload is not installed in THIS instance."
    }
    return $vcvarsall
}

# Does the SELECTED installation actually have the ARM64 compiler? Checked on disk.
function Test-Arm64ToolsPresent {
    param([Parameter(Mandatory)][string] $VsInstallPath)

    $msvcRoot = Join-Path $VsInstallPath 'VC\Tools\MSVC'
    if (-not (Test-Path -LiteralPath $msvcRoot)) { return $false }
    foreach ($toolset in Get-ChildItem -LiteralPath $msvcRoot -Directory) {
        foreach ($hostDir in @('Hostx64', 'Hostarm64')) {
            $cl = Join-Path $toolset.FullName "bin\$hostDir\arm64\cl.exe"
            if (Test-Path -LiteralPath $cl) { return $true }
        }
    }
    return $false
}

function Test-ClangClPresent {
    param([Parameter(Mandatory)][string] $VsInstallPath)

    foreach ($hostDir in @('x64', 'ARM64')) {
        $clangCl = Join-Path $VsInstallPath "VC\Tools\Llvm\$hostDir\bin\clang-cl.exe"
        if (Test-Path -LiteralPath $clangCl) { return $true }
    }
    return $false
}

# ---------------------------------------------------------------------------------------------
# Import a developer environment into THIS PowerShell process.
#
# vcvarsall.bat is a batch file, so it can only set variables in a cmd.exe it owns. The standard
# trick is to run it, dump the resulting environment, and copy it back - which is what this does.
# $ArchArgument is what vcvarsall takes: x64, arm64, or x64_arm64 (cross from an x64 host).
# ---------------------------------------------------------------------------------------------
function Import-DeveloperEnvironment {
    param(
        [Parameter(Mandatory)][string] $VcVarsAll,
        [Parameter(Mandatory)][string] $ArchArgument
    )

    Write-Host "  developer environment: vcvarsall.bat $ArchArgument"
    $output = & "$env:COMSPEC" /s /c "`"$VcVarsAll`" $ArchArgument >nul 2>&1 && set"
    if ($LASTEXITCODE -ne 0) {
        throw "vcvarsall.bat $ArchArgument failed (exit $LASTEXITCODE). The toolset for that target is probably not installed in this Visual Studio instance."
    }
    foreach ($line in $output) {
        $eq = $line.IndexOf('=')
        if ($eq -lt 1) { continue }
        $name = $line.Substring(0, $eq)
        $value = $line.Substring($eq + 1)
        Set-Item -Path "Env:$name" -Value $value
    }
}

function Assert-OnPath {
    param(
        [Parameter(Mandatory)][string] $Tool,
        [Parameter(Mandatory)][string] $InstallHint
    )
    $found = Get-Command $Tool -ErrorAction SilentlyContinue
    if (-not $found) {
        throw @"
$Tool is not on PATH.

$InstallHint

This script does not install anything for you. See README.txt, PREREQUISITES.
"@
    }
    return $found.Source
}

# ---------------------------------------------------------------------------------------------
# Build. The vendored source is copied to a scratch directory and built there, so ..\dav1d is
# never written to and stays a verifiable, unmodified upstream snapshot. Patches (there are none
# as of 2026-08-28) are applied to the copy.
# ---------------------------------------------------------------------------------------------
function Copy-SourceToScratch {
    param(
        [Parameter(Mandatory)][string] $SourceDir,
        [Parameter(Mandatory)][string] $ScratchDir,
        [Parameter(Mandatory)][string] $PatchDir
    )

    if (Test-Path -LiteralPath $ScratchDir) { Remove-Item -LiteralPath $ScratchDir -Recurse -Force }
    Copy-Item -LiteralPath $SourceDir -Destination $ScratchDir -Recurse
    Write-Host "  copied $SourceDir -> $ScratchDir"

    $applied = @()
    if (Test-Path -LiteralPath $PatchDir) {
        foreach ($patch in Get-ChildItem -LiteralPath $PatchDir -Filter '*.patch' | Sort-Object Name) {
            $git = Get-Command git -ErrorAction SilentlyContinue
            if (-not $git) {
                throw "patches/$($patch.Name) needs applying and neither git nor patch is available. Install Git for Windows, which ships both."
            }
            Write-Host "  applying $($patch.Name)"
            & git -C $ScratchDir apply -p1 $patch.FullName
            if ($LASTEXITCODE -ne 0) {
                throw "patch $($patch.Name) did not apply. Fix it; patches are never applied best-effort."
            }
            $applied += $patch.Name
        }
    }
    if ($applied.Count -eq 0) { return 'none' }
    return ($applied -join ' ')
}

function Invoke-MesonBuild {
    param(
        [Parameter(Mandatory)][string] $ScratchDir,
        [Parameter(Mandatory)][string] $BuildDir,
        [Parameter(Mandatory)][string] $MesonOptions,
        [string[]] $ExtraMesonArgs = @()
    )

    if (Test-Path -LiteralPath $BuildDir) { Remove-Item -LiteralPath $BuildDir -Recurse -Force }

    # -Db_vscrt=static_from_buildtype links the C runtime statically in a release build, so the
    # shipped DLL needs no Visual C++ Redistributable on the user's machine. Without it, a
    # perfectly good build fails to load on a clean Windows install with an error that says
    # nothing useful about the cause.
    $arguments = @('setup', $BuildDir, $ScratchDir) +
                 ($MesonOptions -split '\s+') +
                 @('-Db_vscrt=static_from_buildtype') +
                 $ExtraMesonArgs

    Write-Host "  meson $($arguments -join ' ')"
    & meson @arguments
    if ($LASTEXITCODE -ne 0) { throw "meson setup failed (exit $LASTEXITCODE)." }

    & ninja -C $BuildDir
    if ($LASTEXITCODE -ne 0) { throw "ninja failed (exit $LASTEXITCODE)." }
}

# ---------------------------------------------------------------------------------------------
# The gate. Same checks as the Linux build, expressed with the tools Windows has.
# ---------------------------------------------------------------------------------------------
$script:RequiredSymbols = @(
    # the 13 the decoder binding calls
    'dav1d_version',
    'dav1d_version_api',
    'dav1d_default_settings',
    'dav1d_open',
    'dav1d_parse_sequence_header',
    'dav1d_send_data',
    'dav1d_get_picture',
    'dav1d_apply_grain',
    'dav1d_flush',
    'dav1d_close',
    'dav1d_get_event_flags',
    'dav1d_get_decode_error_data_props',
    'dav1d_get_frame_delay',
    # the four data/picture lifetime helpers the binding also needs
    'dav1d_data_wrap',
    'dav1d_data_create',
    'dav1d_data_unref',
    'dav1d_picture_unref'
)

# Only these may appear in `dumpbin /dependents`. KERNEL32 is Windows itself. Anything from the
# Visual C++ runtime (VCRUNTIME140.dll, MSVCP140.dll, api-ms-win-crt-*.dll) means the static-CRT
# setting did not take, and the package would demand a redistributable on every user's machine.
$script:AllowedDependents = @('KERNEL32.dll')

$script:GateFailures = New-Object System.Collections.Generic.List[string]

function Add-GatePass { param([string] $Message) Write-Host "  [ok] $Message" }
function Add-GateFail { param([string] $Message) Write-Host "  [FAIL] $Message"; $script:GateFailures.Add($Message) }

function Invoke-Dumpbin {
    param([Parameter(Mandatory)][string[]] $Arguments)
    $output = & dumpbin @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "dumpbin $($Arguments -join ' ') failed (exit $LASTEXITCODE)." }
    return $output
}

function Test-Dav1dBinary {
    param(
        [Parameter(Mandatory)][string] $DllPath,
        [Parameter(Mandatory)][string] $ExpectedMachine,   # 'x64' or 'ARM64'
        [Parameter(Mandatory)][string] $SmokeTestSource,
        [Parameter(Mandatory)][string] $VectorDir,
        [Parameter(Mandatory)][string] $ExpectedFile,
        [Parameter(Mandatory)][string] $CliPath,
        [bool] $CanRunTargetBinaries = $true
    )

    # --- machine type -------------------------------------------------------------------------
    $headers = Invoke-Dumpbin @('/nologo', '/headers', $DllPath)
    $machineLine = $headers | Select-String -Pattern 'machine \(' | Select-Object -First 1
    if ($machineLine -and $machineLine.ToString() -match [regex]::Escape($ExpectedMachine)) {
        Add-GatePass "architecture: $($machineLine.ToString().Trim())"
    }
    else {
        Add-GateFail "architecture mismatch: expected $ExpectedMachine, dumpbin says '$($machineLine)'"
    }

    # --- exports ------------------------------------------------------------------------------
    $exportText = (Invoke-Dumpbin @('/nologo', '/exports', $DllPath)) -join "`n"
    $missing = @()
    foreach ($symbol in $script:RequiredSymbols) {
        if ($exportText -notmatch "(?m)^\s*\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+$([regex]::Escape($symbol))\s*$") {
            # fall back to a plain containment test - the column layout of dumpbin /exports has
            # changed between toolset versions before
            if ($exportText -notmatch "\b$([regex]::Escape($symbol))\b") { $missing += $symbol }
        }
    }
    if ($missing.Count -gt 0) {
        Add-GateFail "missing exports: $($missing -join ' ')"
    }
    else {
        Add-GatePass "all $($script:RequiredSymbols.Count) required symbols exported"
    }

    # --- dependents ---------------------------------------------------------------------------
    $dependents = @()
    $inList = $false
    foreach ($line in Invoke-Dumpbin @('/nologo', '/dependents', $DllPath)) {
        $text = $line.ToString().Trim()
        if ($text -like 'Image has the following dependencies*') { $inList = $true; continue }
        if ($inList) {
            if ($text -eq '') { if ($dependents.Count -gt 0) { break } else { continue } }
            if ($text -like 'Summary*') { break }
            $dependents += $text
        }
    }
    $unexpected = $dependents | Where-Object { $script:AllowedDependents -notcontains $_ }
    if ($unexpected) {
        Add-GateFail "unexpected dynamic dependencies: $($unexpected -join ' ') (allowed: $($script:AllowedDependents -join ' ')). Anything from the VC runtime means -Db_vscrt=static_from_buildtype did not take."
    }
    else {
        Add-GatePass "dependencies are system-only: $($dependents -join ' ')"
    }

    # --- dlopen (LoadLibrary) smoke test ------------------------------------------------------
    if ($CanRunTargetBinaries) {
        Write-Host '  --- LoadLibrary smoke test ---'
        Push-Location $env:TEMP
        try {
            & cl /nologo /O2 /Fe:smoke-test.exe $SmokeTestSource | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "compiling smoke-test.c failed (exit $LASTEXITCODE)." }
            # The leading .\ is required: some environments set NoDefaultCurrentDirectoryInExePath.
            & .\smoke-test.exe $DllPath
            if ($LASTEXITCODE -eq 0) { Add-GatePass 'smoke test' } else { Add-GateFail 'smoke test' }
        }
        finally { Pop-Location }
    }
    else {
        Add-GateFail 'smoke test NOT RUN - this build targets an architecture this machine cannot execute. Run the gate on the target hardware before shipping this binary. (Reported as a failure on purpose: an unrun check is not a passed check.)'
    }

    # --- conformance --------------------------------------------------------------------------
    Write-Host '  --- conformance ---'
    $summary = New-Object System.Collections.Generic.List[string]
    if (-not $CanRunTargetBinaries) {
        Add-GateFail 'conformance NOT RUN - the built dav1d.exe cannot execute on this machine. Run it on the target hardware before shipping.'
    }
    else {
        # THE CLI MUST DECODE WITH THE DLL WE JUST BUILT, NOT WHATEVER IS ON PATH.
        #
        # meson puts dav1d.dll in <build>\src but dav1d.exe in <build>\tools, so nothing sits
        # beside the executable and Windows' DLL search falls through to PATH. That is not
        # hypothetical: on the machine this was first run on (2026-08-29), GStreamer was on PATH
        # shipping its own dav1d.dll at API 6.8.0. The API-7 CLI asked it for entry points it did
        # not have and died with 0xC0000139 STATUS_ENTRYPOINT_NOT_FOUND before reaching main, so
        # every decode returned an EMPTY string and the gate reported seven hash mismatches that
        # had nothing to do with the binary being tested.
        #
        # Copying the STAGED dll (the exact file the package ships) next to the CLI fixes it for
        # good: the executable's own directory is first in the default search order and cannot be
        # displaced by PATH. Do not replace this with a PATH edit - PATH is what caused the bug.
        $cliDir = Split-Path -Parent $CliPath
        Copy-Item -LiteralPath $DllPath -Destination (Join-Path $cliDir 'dav1d.dll') -Force
        Write-Host "  staged dll copied beside the CLI so PATH cannot supply a different dav1d.dll"

        $checked = 0
        $passed = 0
        foreach ($line in Get-Content -LiteralPath $ExpectedFile) {
            $trimmed = $line.Trim()
            if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
            $fields = $trimmed.Split('|')
            if ($fields.Count -lt 3) { continue }
            $vector = $fields[0]
            $flags = $fields[1].Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries)
            $want = $fields[2]
            $vectorPath = Join-Path $VectorDir $vector
            if (-not (Test-Path -LiteralPath $vectorPath)) {
                Add-GateFail "expected-hash file names a vector that is not in the repository: $vector"
                continue
            }
            $got = (& $CliPath -i $vectorPath --muxer md5 -o - @flags 2>$null | Out-String).Trim()
            $cliExit = $LASTEXITCODE
            $checked++
            if ($got -eq $want) {
                $passed++
                Write-Host "    [ok]   $vector ($($fields[1])) $got"
                $summary.Add("  $vector ($($fields[1])) = $got")
            }
            elseif ($got -eq '') {
                # No output at all means the CLI did not decode - it failed to START. Say so,
                # rather than reporting it as a hash mismatch: the two have completely different
                # causes and sending someone hunting for a decoder bug is a waste of a day.
                # 0xC0000139 is STATUS_ENTRYPOINT_NOT_FOUND, 0xC0000135 STATUS_DLL_NOT_FOUND -
                # both mean the wrong dav1d.dll was loaded.
                Add-GateFail ("conformance: $vector ($($fields[1])) produced NO OUTPUT - the CLI exited with " +
                              ("0x{0:X8}" -f [uint32]($cliExit -band 0xFFFFFFFF)) +
                              " without decoding. This is a launch failure, not a wrong hash.")
                $summary.Add("  $vector ($($fields[1])) = <no output, CLI exit 0x$('{0:X8}' -f [uint32]($cliExit -band 0xFFFFFFFF))>   *** EXPECTED $want ***")
            }
            else {
                Add-GateFail "conformance: $vector ($($fields[1])) decoded to $got, expected $want"
                $summary.Add("  $vector ($($fields[1])) = $got   *** EXPECTED $want ***")
            }
        }
        if ($checked -eq 0) { Add-GateFail 'the expected-hash file contained no usable entries' }
        elseif ($passed -eq $checked) { Add-GatePass "$passed/$checked conformance decodes matched" }
        else { Write-Host "  [FAIL] only $passed of $checked conformance decodes matched" }
    }

    return $summary
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
