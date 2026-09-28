Android NuGet-consumer validation (separate from the desktop solution)
==================================================================

This app references the PACKED Dav1d NuGet, the published Android audio backend
and Opus package. It has no ProjectReference to the codec and no explicit native
library items. The .NET Android SDK must select both libraries from NuGet and
put them into the APK. No changes to Skia or Authoring are involved.

Requires .NET 10, the Android workload, Android SDK 36/build-tools, and JDK 21.
Install those tools separately. The app targets net10.0-android36.1 with minimum
API 33 and android-arm64;android-x64. Its Release configuration uses the SDK's
default trimming and profiled AOT. The manifest is intentionally debuggable in
both configurations so adb run-as can collect results; this is a test app.

From the repository root, build a unique local package version and consume it:

  dotnet build src/CodeBrix.VideoPlayback.Dav1d -c Release \
    -p:PackageVersion=0.0.0-android-validation.1
  dotnet build tests/CodeBrix.VideoPlayback.Dav1d.AndroidTests -c Debug \
    -p:Dav1dPackageVersion=0.0.0-android-validation.1 \
    -p:RestoreAdditionalProjectSources="$PWD/src/CodeBrix.VideoPlayback.Dav1d/bin/Release" \
    -p:AndroidSdkDirectory="$HOME/Android/Sdk" \
    -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk-amd64

Increase the test package's suffix for each changed build; NuGet caches package
versions. Do not reuse a cached version after changing the codec. The app
requires Dav1dPackageVersion explicitly so it cannot silently test an old
published package. Restore obtains the normal dependencies from nuget.org.

Run against an explicitly authorized ARM64 device and repeat on an x64 device:

  python3 dav1d-native-tools/android/test-apk.py \
    tests/CodeBrix.VideoPlayback.Dav1d.AndroidTests/bin/Debug/net10.0-android36.1/com.codebrix.dav1d.tests-Signed.apk \
    --adb "$HOME/Android/Sdk/platform-tools/adb" --serial DEVICE_SERIAL \
    --result dav1d-native-tools/output/android-arm64/MANAGED-DEBUG.json

Use the android-x64 result directory for x64. Repeat build/run with Release in
place of Debug. No emulator is started or selected. The script installs/replaces
only com.codebrix.dav1d.tests, checks that both APK libraries match the repo's
shipped bytes, verifies 16 KB ZIP alignment for uncompressed dav1d entries,
launches the activity and waits up to 180 seconds for PASS/FAIL. JSON evidence
contains the APK/native hashes, device ABI/API/page size, and each check result.
An optional `adb -s DEVICE_SERIAL uninstall com.codebrix.dav1d.tests` removes
the test app after testing.

Checks use public APIs and the existing synthetic IVF/WebM assets:
  * native version 1.5.4 / API 7.0.0, loaded by Android's native loader;
  * all seven conformance hashes, twice with Flush between runs;
  * four native decoder workers, forced collections while compressed inputs
    and pictures can still be referenced by native code;
  * zero-copy plane pointers, 64-byte alignment, BGRA conversion;
  * retained frame unchanged after decoder disposal, disposal on another thread,
    and zero live pool buffers after the last reference is released;
  * video-only playback and AV1 with Vorbis/Opus through the initialized Android
    audio backend, including pause, seek and end-of-stream. Audio is muted in
    the session while its device-backed clock still paces playback.

This exercises decoding and the session's frame mailbox. It does not supply a
visual presenter or test the Skia or Authoring packages. The ordinary desktop
xUnit suite remains in the main solution; this app is deliberately separate so
desktop contributors do not need the Android workload.

Validation record - 2026-09-28 UTC
--------------------------------
Devices: Samsung SM-G781U1 (ARM64) and HP 87FE laptop running Android-x86 (x64).
Both run Android 13/API 33 with 4096-byte pages. No emulator was used.
SDK: .NET 10 Android 36.1.69; runtime .NET 10.0.12. Both Debug and Release
builds succeeded with zero warnings/errors. APK minSdkVersion was 33 and both
ABI libraries matched the shipped .so bytes. The dependency graph contained
Audio.Core and Audio.Android, without the desktop CodeBrix.Audio package.

Dav1d checks passed in Debug and trimmed/profiled-AOT Release on both devices:
7/7 conformance cases (each decoded twice), GC stress, zero-copy and retained
frame lifetimes, BGRA conversion, and video-only playback including pause/seek.
Full Debug integration also passed with both Opus and Vorbis audio. Release
AV1 + Opus playback passed with the original dependencies. After selecting
fixed local Audio.Core 1.0.271.274, FULL Release integration passed on both
devices, including AV1 + Vorbis playback, pause, seek and drain. That run used
packed Dav1d 1.0.271.248 and the unchanged published Android/Opus dependencies
listed below. Records: MANAGED-RELEASE-FIXED-CORE.json in each device's output
directory.

Vorbis Release regression: fixed in Audio.Core, pending publication
-----------------------------------------------------------------
The original full Release run crashed with SIGSEGV when Vorbis decoding began.
It reproduced on BOTH architectures with these published dependencies:
  VideoPlayback.MitLicenseForever 1.0.271.97
  Audio.Core.MitLicenseForever 1.0.269.1270
  Audio.Android.ApacheLicenseForever 1.0.270.1181
  Audio.Opus.BsdLicenseForever 1.0.269.1352

Use test-apk.py --audio-only with the Release APK to reproduce WITHOUT loading
or calling dav1d. That diagnostic first plays silent PCM successfully, then
extracts the Vorbis track's packets from the existing WebM and calls Audio.Core's
IPacketSoundDecoder.DecodePacket directly. The process crashes during that
standalone decode too. A further Core-only Android app in the sibling
CodeBrix.Audio repository reproduced the crash with NO backend or codec package
referenced. Variable negative offsets in the managed Vorbis MDCT lost their
sign on the optimized Mono path. Five offsets now explicitly use signed
native-sized arithmetic. The Core-only numerical regression checks and full
playback with Audio.Android both pass on ARM64 and x64 with the fixed package.

Core 1.0.271.274 is a LOCAL validation build, not a published dependency pin.
Neither Audio.Android nor Opus required a source or native-library change.
Publish fixed Core and select it in the app, directly or through an updated
platform-package dependency. Until then, the default published dependencies
above still reproduce the Release crash. To validate a local Core build, copy
its nupkg and the Dav1d nupkg into one local feed directory and build with:

  dotnet build tests/CodeBrix.VideoPlayback.Dav1d.AndroidTests -c Release \
    -p:Dav1dPackageVersion=DAV1D_VERSION \
    -p:AudioCoreVersion=FIXED_CORE_VERSION \
    -p:RestoreAdditionalProjectSources=/path/to/combined-local-feed \
    -p:AndroidSdkDirectory="$HOME/Android/Sdk" \
    -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk-amd64

Then run test-apk.py normally on each authorized device. Verify the resolved
Core version in obj/project.assets.json; the app also writes its loaded Core
assembly version to progress.txt. No platform package pins need to change for
this local test.

Use test-apk.py --codec-only to run the Dav1d and video-only checks independently.
The default remains the full integration suite: a Vorbis crash is reported as
FAIL, never silently skipped. The app writes progress.txt before/after each
stage, and the runner includes that progress in a failed JSON report. Local
records are under dav1d-native-tools/output/android-<arch>/MANAGED-*.json.

16 KB ELF alignment passed on both native libraries; execution on a device with
16 KB pages has not been verified.
