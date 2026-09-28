================================================================================
Android dav1d native libraries - ARM64 and x64, Android 13/API 33 minimum
================================================================================

The source is ../dav1d, the same unmodified vendored snapshot as the desktop
builds (dav1d 1.5.4 plus commit 52b9d3d3, API 7.0.0). No patches are required.
build.py copies it to a temporary folder, builds and verifies each ABI, then
removes the scratch tree. No source, tool, subproject or test data is downloaded
during a build. Nothing in a dotnet build invokes this native build.

OUTPUT
------
  android-arm64 -> arm64-v8a, aarch64-linux-android33-clang
  android-x64   -> x86_64,   x86_64-linux-android33-clang

Both are ELF64 libdav1d.so with SONAME libdav1d.so and API 33 in their Android
ELF note. They depend only on Bionic libc.so, not a desktop libc or libc++.
Assembly optimizations and both 8-bit and high-bit-depth decoders are enabled.
Runtime CPU detection selects the SIMD implementations supported by the device.

Both link with -z max-page-size=16384 and -z common-page-size=16384. These
libraries support 4 KB and 16 KB load alignment; the application's other native
libraries and APK packaging must also support its device's page size. See:
  https://developer.android.com/guide/practices/page-sizes

TOOLS (install separately, before the offline build)
--------------------------------------------------
Linux x64 or macOS with the Android NDK host toolchain is required. The shipped
artifacts were built on Linux x64. macOS is supported by the script but has not
been used to produce these artifacts.

  * Python 3.9+; Python 3.13 was used.
  * NDK 30.0.16248370, pinned in pins.json. Install Android SDK command-line
    tools, then use sdkmanager (adjust its path to the SDK installation):
      sdkmanager --install 'ndk;30.0.16248370'
    Accept the SDK/NDK licences during tool installation. build.py never installs
    anything and requires the exact pinned NDK revision.
  * Meson 1.12.0 and Ninja 1.13.0 (vendor suffixes accepted). For example:
      python3 -m venv ~/.venvs/codebrix-dav1d-android
      ~/.venvs/codebrix-dav1d-android/bin/python -m pip install meson==1.12.0 ninja==1.13.0
      export PATH="$HOME/.venvs/codebrix-dav1d-android/bin:$PATH"
  * NASM >= 2.14 for x64; NASM 2.15.03 was used. On Debian:
      sudo apt-get install nasm
    Or on macOS: brew install nasm
  * patch, only if ../patches contains local *.patch files in a future build.
  * adb from Android SDK platform-tools, for the device gates.

BUILD (from repository root)
----------------------------
  python3 dav1d-native-tools/android/build.py \
    --ndk "$HOME/Android/Sdk/ndk/30.0.16248370"

Use --arch arm64 or --arch x64 to build just one, and --jobs to control parallel
compilation. CODEBRIX_ANDROID_NDK can supply the NDK path instead of --ndk.
Do not use Python -O: assertions implement the verification gates, and optimized
Python is explicitly rejected.

Each output/android-<arch>/ contains libdav1d.so, unstripped/libdav1d.so (DWARF
symbols), LICENSE-Dav1d.txt, the dav1d CLI, smoke-test, and BUILD-INFO.json.
The record includes hashes, build ID, tool versions, flags and static checks.
Runtime tests are deliberately marked UNRUN there: a cross-build is not a
device test. test-device.py supplies a separate DEVICE-RESULT.json.

The build runs verify.py on both twins: machine/ELF class, Android API note,
16 KB PT_LOAD alignment, SONAME, all managed imports exported, system-only
dependencies, no text relocations or RPATH/RUNPATH, and matching build IDs.
layout-check.c is cross-compiled against the built headers to assert native
structure sizes and field offsets used by the managed binding.

OFFLINE CONTAINER ROUTE USED FOR THE SHIPPED BINARIES
--------------------------------------------------
The existing linux/Containerfile.x86_64 supplies Python, Meson, Ninja and NASM.
Build that tool image once as described in linux/README.txt. With it cached:

  podman run --rm --network none \
    -v "$PWD:/repo:Z" \
    -v "$HOME/Android/Sdk/ndk/30.0.16248370:/ndk:ro" \
    localhost/codebrix-dav1d-build-x86_64:latest \
    /opt/python/cp313-cp313/bin/python /repo/dav1d-native-tools/android/build.py --ndk /ndk

The image uses the digest-pinned base from linux/pins.env. The Android build
still uses the NDK compiler and sysroot, not the container's desktop compiler.
Actual tool versions and artifact hashes are in ../BUILD-PROVENANCE.txt.
Byte-for-byte reproducibility across hosts/tool installations has not been
established; retain the unstripped twin from the SAME build as each adopted .so.

DEVICE GATES AND ADOPTION
------------------------
Always select the authorized device explicitly. No script starts an emulator
or chooses a default device. Run for BOTH ABIs, preferably on API 33:

  python3 dav1d-native-tools/android/test-device.py --serial DEVICE_SERIAL

Pass --adb /path/to/adb if it is not on PATH. The script checks the device ABI
and API, verifies the copied library's hash, runs smoke-test and all seven
EXPECTED.md5 decodes, and records model/API/page size/hash/results in
output/android-<arch>/DEVICE-RESULT.json. It cleans up its own unique device
temporary directory. Rebuilding invalidates the old device result.

After both static and device gates pass, copy each output library and licence
to src/CodeBrix.VideoPlayback.Dav1d/runtimes/android-<arch>/native/ and its
unstripped twin to dav1d-native-tools/unstripped/android-<arch>/. Update
unstripped/SHA256SUMS and BUILD-PROVENANCE.txt together. The CLI and test
executables never go in the NuGet package.

The managed NuGet-consumer app tests APK asset selection, native loading,
conformance, GC/native lifetimes, colour conversion, and video/audio playback:
  ../../tests/CodeBrix.VideoPlayback.Dav1d.AndroidTests/README.txt

VALIDATION OF THE ADOPTED BINARIES
--------------------------------
2026-09-28 UTC: native smoke and 7/7 conformance hashes passed on both real
Android 13/API 33 devices: Samsung SM-G781U1 (ARM64) and HP 87FE laptop (x64).
Both devices use 4 KB pages. Static ELF alignment for 16 KB passed for both;
execution on a 16 KB device remains unverified. No x64 emulator was used.
See BUILD-PROVENANCE.txt and MAINTAINER-README.txt for managed validation.
