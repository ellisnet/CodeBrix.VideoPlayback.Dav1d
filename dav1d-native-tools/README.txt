================================================================================
dav1d-native-tools - everything needed to build the dav1d native libraries
================================================================================

THE RULE THIS FOLDER EXISTS FOR
--------------------------------------------------------------------------------
Jeremy, 2026-08-28:

    "dav1d-native-tools must contain EVERYTHING needed to build the native dav1d
    libraries for Windows (x64 + ARM64), Linux (x64 + ARM64 + RISC-V 64) and
    macOS (Intel + Apple Silicon): all scripts and source code and a README file
    for each platform about how to generate the native libraries. The only thing
    that can't be in the repo are the tools that must be installed on the build
    machines (ninja etc.), and those must be detailed, with install
    instructions, in the platform READMEs. Builds happen from OUR repo, never
    from a cloned copy of the upstream dav1d repo, and the build process never
    reaches outside our repo - so the libraries can be rebuilt in the future
    from this repo alone, with no dependency that may no longer exist."

Everything below follows from that sentence.


HOW THE RULE IS ENFORCED, NOT JUST STATED
--------------------------------------------------------------------------------
The Linux build runs inside a container started with `--network none`. The first
thing it prints is the proof: the container has no network interface but
loopback. If any step ever tried to fetch a source file, a test vector or a
dependency, it would fail there instead of quietly working on whichever machine
happened to have a connection. The Windows and macOS scripts fetch nothing
either, but Linux is where the rule is mechanically demonstrated on every run.

Two things upstream would ordinarily fetch are switched off deliberately, not
left to chance: -Denable_tests=false stops meson consulting
subprojects/checkasm.wrap (which would git-clone a test harness), and
-Dxxhash_muxer=disabled stops it looking for an xxhash.h that upstream expects
you to supply yourself. See dav1d/UPSTREAM.txt.


FOLDER MAP
--------------------------------------------------------------------------------
  README.txt              this document
  BUILD-PROVENANCE.txt    what was actually built, when, by what, with what
                          hashes - one section per runtime identifier
  smoke-test.c            the load-and-run verification program. One file, no
                          dependencies, no build system; all three platforms
                          compile and run it as part of their gate
  .gitignore              re-includes this folder's contents from the root
                          .gitignore (which ignores names that occur inside the
                          vendored source) and ignores output/. Its first rule
                          is load-bearing - read the comment before editing it

  dav1d/                  THE VENDORED UPSTREAM SOURCE. An unmodified snapshot,
                          including its COPYING and its own crossfiles.
                          UPSTREAM.txt records the URL, tag, commit, dates and
                          the exact copy command. Never edited - if a build ever
                          needs a change it goes in patches/
  patches/                local changes to the vendored source, applied at build
                          time to a scratch copy. EMPTY as of 2026-08-28 -
                          no patch is needed on any platform
  test-vectors/           six small AV1 streams and the decode hashes every
                          build must reproduce. Synthetic, generated here, NOT
                          third-party content - see its README.txt

  linux/                  linux-x64, linux-arm64, linux-riscv64
                          pins.env, build.sh, container-build.sh,
                          Containerfile.<arch>, README.txt
  windows/                win-x64, win-arm64
                          build-common.ps1, build-win-x64.ps1,
                          build-win-arm64.ps1, crossfile-win-arm64.txt,
                          README.txt
  macos/                  osx-arm64, osx-x64
                          build-common.sh, build-osx-arm64.sh,
                          build-osx-x64.sh, crossfile-x86_64.txt, README.txt

  output/                 build results (git-ignored except its README.txt)


WHICH README TO READ
--------------------------------------------------------------------------------
  Building on Linux    -> linux/README.txt
  Building on Windows  -> windows/README.txt
  Building on a Mac    -> macos/README.txt

Each one lists the tools to install on that machine, with the exact command,
and nothing else is needed.

  >>> As of 2026-08-28 only the three LINUX builds have actually been run.
      The Windows and macOS scripts were written on Linux and have never been
      executed on their platforms. Each of those READMEs says so at the top and
      lists exactly which assumptions are unverified. <<<


THE SEVEN RUNTIME IDENTIFIERS
--------------------------------------------------------------------------------
  RID             built by                              shipped file
  --------------  ------------------------------------  ------------------
  linux-x64       linux/build.sh x64                    libdav1d.so
  linux-arm64     linux/build.sh arm64                  libdav1d.so
  linux-riscv64   linux/build.sh riscv64                libdav1d.so
  win-x64         windows\build-win-x64.ps1             dav1d.dll
  win-arm64       windows\build-win-arm64.ps1           dav1d.dll
  osx-arm64       macos/build-osx-arm64.sh              libdav1d.dylib
  osx-x64         macos/build-osx-x64.sh                libdav1d.dylib

All seven names are UNVERSIONED on purpose: LibraryImport("dav1d") on .NET
probes for exactly those names and does not follow sonames. The soname
(libdav1d.so.7 / API 7.0.0) lives inside the file and is recorded in every
BUILD-INFO.txt.

Every RID folder also gets a LICENSE file - a verbatim copy of dav1d's COPYING.
That is not a nicety: BSD-2-Clause clause 2 requires the copyright notice to be
reproduced with a binary distribution, and shipping it beside the binary in
runtimes/<rid>/native/ is how this package satisfies it.


THE VENDORED COMMIT
--------------------------------------------------------------------------------
  dav1d 1.5.4 plus one commit - git describe: 1.5.4-1-g52b9d3d3
  commit 52b9d3d3ec525f5a20849145fa0e879d585f4911
  ("aarch64: Always use PIC versions of the movrel macro", authored 2026-07-09)
  API version 7.0.0.  BSD-2-Clause.

The extra commit past the tag is an AArch64 shared-library correctness fix that
matters for the ARM64 binaries. dav1d/UPSTREAM.txt explains the choice and
records how to verify the snapshot against upstream.


LICENCES AND NOTICES
--------------------------------------------------------------------------------
../THIRD-PARTY-NOTICES.txt, at the root of this repository, inventories every
copyright holder and every licence that appears in the vendored snapshot - the
BSD-2-Clause that covers dav1d itself, plus the ISC and NetBSD-2-clause and
public-domain files that upstream includes - with the full text of each licence
and the file paths it covers. Read it before changing what this folder vendors.

The conformance streams in test-vectors/ are NOT third-party content and are
deliberately absent from that file: they are synthetic streams generated here
with ffmpeg from lavfi test patterns, and they belong to this repository.


A ONE-MINUTE TOUR
--------------------------------------------------------------------------------
    cd linux
    ./build.sh x64          # about 11 seconds; builds, verifies, stages
    cat ../output/linux-x64/BUILD-INFO.txt
    cd ../output && sha256sum -c SHA256SUMS
