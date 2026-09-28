================================================================================
MAINTAINER-README: CodeBrix.VideoPlayback.Dav1d
Notes for people and agents MAINTAINING this repository - not for package
consumers, who want AGENT-README.txt instead
================================================================================


⚠️ THE PIN: THIS PACKAGE BUILDS AGAINST A PUBLISHED CodeBrix.VideoPlayback
================================================================================
The package reference in src/CodeBrix.VideoPlayback.Dav1d.csproj is THE record
of which CodeBrix.VideoPlayback.MitLicenseForever this binding was built and
tested against. This document deliberately does not repeat the number: it
changes at every core publish, and a copy written here went stale within a day
once already. Read the csproj.

Two rules govern it:

  * At the moment this package is published, the pin names a version that
    EXISTS ON NUGET.ORG - never a local pre-publish pack. Proof, every time:
    `dotnet restore --force -p:RestoreSources="https://api.nuget.org/v3/index.json"`
    (nuget.org alone, no folder feed) succeeds, obj/project.assets.json lists no
    local source, the build is 0 warnings, the suite is green, and the packed
    nuspec declares the published core as the one dependency. Moving any local
    feed aside and restoring again is the only evidence that the feed is not
    needed.
  * A core republish does NOT by itself require a Dav1d republish. The
    reference is a minimum (NuGet's `>=`), the binding uses only the core's
    decoder, frame-pool and frame seams, and a newer core resolves cleanly for
    every consumer - the family relies on that skew being tolerated. Bump the
    pin (and republish) when the binding needs something a newer core added,
    or when those seams change shape.

VERIFYING AGAINST AN UNPUBLISHED CodeBrix.VideoPlayback - THE PRE-PUBLISH METHOD
(kept for the next time this repository must build against work that has not
been published yet):

    cd ~/GitHome/CodeBrix.VideoPlayback
    dotnet pack src/CodeBrix.VideoPlayback/CodeBrix.VideoPlayback.csproj -c Release \
        -o <some local feed folder>

    # in THIS repository - the feed rides on the command line; no nuget.config
    # is committed and nothing about the arrangement leaks into the package:
    dotnet restore -p:RestoreSources="<feed folder>%3Bhttps://api.nuget.org/v3/index.json"
    dotnet build   -c Release
    dotnet test    -c Release

(The %3B is an escaped semicolon; MSBuild property values cannot carry a bare
one.) Before any publish after such a round: raise the pin to the PUBLISHED
version, restore with --force from nuget.org alone, and prove the feed is not
needed by moving it aside and restoring again. A restore that still succeeds
after the feed is gone is the only evidence that the pin is real.


PURPOSE AND SCOPE
================================================================================
One project, one package: AV1 decoding for CodeBrix.VideoPlayback, through a
binding over dav1d, with self-built native libraries for nine runtime identifiers.

    CodeBrix.VideoPlayback.Dav1d.slnx   the solution. Its "Solution Items"
                                        folder carries .gitignore,
                                        AGENT-README.txt, EXTRAS-README.txt,
                                        global.json, icon-codebrix-128.png,
                                        LICENSE, MAINTAINER-README.txt,
                                        README-INDEX.txt, README.md and
                                        THIRD-PARTY-NOTICES.txt; its "Tests"
                                        folder carries the test project
    global.json                         selects the test runner. Nothing else -
                                        it pins no SDK. See TESTING
    src/CodeBrix.VideoPlayback.Dav1d/   the binding, packable
    tests/CodeBrix.VideoPlayback.Dav1d.Tests/   the suite
    dav1d-native-tools/                 everything needed to BUILD the natives
    tests/assets/                       end-to-end playback files

The package's only dependency is CodeBrix.VideoPlayback.MitLicenseForever, which
brings CodeBrix.Audio.Core.MitLicenseForever with it. Applications choose the
desktop CodeBrix.Audio or Android CodeBrix.Audio.Android output backend. The
desktop test project explicitly references CodeBrix.Audio for its opt-in audio
test. No output backend, SkiaSharp or Opus dependency is added to this package.


HARD RULES FOR THIS REPOSITORY
================================================================================
* NET10 ONLY. Nullable reference types are OFF; a `?` never appears on a
  reference type anywhere in this repository.
* XML DOC COMMENTS on everything public. CS1591 is fixed at the source, never
  suppressed.
* THE NATIVE LIBRARIES ARE NOT BUILT BY ANY BUILD IN THIS REPOSITORY. They are
  built by dav1d-native-tools/, on the machine that can build them, and
  committed. A `dotnet build` never compiles C.
* NOTHING IS DOWNLOADED, at build time or test time. The dav1d source, the
  conformance streams, the expected hashes and the playback assets are all in the
  repository. That is the whole point of dav1d-native-tools/ (decision 25 of the
  programme plan) and it applies to the managed side too.
* THE BINDING NEVER NAMES A CONSUMING APPLICATION. The only application-shaped
  text anywhere is the sanctioned wording in the native-library failure message.
* ONE TYPE PER FILE, in sub-folders that match the namespaces; entry-point types
  at the project root.


BUILDING
================================================================================
    dotnet build -c Release        # 0 warnings, 0 errors, or it is not done

The library sets AllowUnsafeBlocks because LibraryImport's source generator
produces unsafe code and every structure the binding passes to dav1d is handled
through pointers. The test project sets it too, so tests can read plane memory
directly.

The nine runtimes/<rid>/native/ folders are packed into the NuGet package AND
copied into the build output - of this project and of anything that references it
as a project. That is deliberate: it means the test suite exercises exactly the
runtimes/<rid>/native/ layout the library's own probing has to find, rather than
a flattened one that would never fail.


TESTING
================================================================================
    dotnet test -c Release

or, since the test project builds an executable under the Microsoft Testing
Platform runner that global.json selects:

    ./tests/CodeBrix.VideoPlayback.Dav1d.Tests/bin/Release/net10.0/CodeBrix.VideoPlayback.Dav1d.Tests

One test is opt-in. The audible playback test opens the sound device, and runs
only when

    CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1

is set. Without it the test skips with a message saying why. A headless machine
must be able to run the whole suite green.

ONE test class touches process-wide state - the decoder registry - and carries
[Collection("Process-wide registries")] so that nothing runs beside it:
CodeBrixVideoPlaybackDav1dTests. The other seven classes carry no collection
attribute, because they register with a SESSION rather than with the process
(CodeBrixVideoPlaybackDav1d.Register(session)) or construct a decoder through
the factory directly. A new test that calls the parameterless Register(), or
Unregister(), belongs in that collection too.

THE TEST RUNNER IS Microsoft.Testing.Platform, selected by global.json at the
repository root. That file is two settings deep and is the whole of it:

    { "test": { "runner": "Microsoft.Testing.Platform" } }

It pins NO SDK version, so the newest installed .NET 10 SDK is still used.
Because the setting lives in global.json rather than in the test csproj it
applies to every `dotnet test` run anywhere in the repository, including CI.
Keep the file committed: without it `dotnet test` falls back to the older VSTest
bridge. You can tell which one ran - the Microsoft Testing Platform's output
ends in a "Test run summary:" block, while the bridge invokes MSBuild with
`--target:VSTest`.

The test project's package references are xunit.v3, xunit.runner.visualstudio,
Microsoft.NET.Test.Sdk and SilverAssertions.ApacheLicenseForever. There is NO
coverage collector - no coverlet.collector reference and no `--collect`
argument - so nothing writes to TestResults/ and no coverage report is produced
here.


PACKAGING / PUBLISHING
================================================================================
    dotnet build -c Release          # pack does not build the assembly it packs
    dotnet pack  -c Release -o <folder> --no-build

The version is date-stamped by the standard family block in the csproj: every
build produces a new one, and two builds in the same UTC minute produce the same
one, so never publish twice within a minute.

The package should contain:

    lib/net10.0/CodeBrix.VideoPlayback.Dav1d.dll   and its .xml
    runtimes/{win-x64,win-arm64,osx-x64,osx-arm64,
              linux-x64,linux-arm64,linux-riscv64,android-arm64,android-x64}/native/
        the native library and a copy of dav1d's COPYING as LICENSE-Dav1d.txt
    README.md  AGENT-README.txt  LICENSE  THIRD-PARTY-NOTICES.txt
    icon-codebrix-128.png

(The package ROOT carries this repository's own LICENSE, under that plain name -
it is the file NuGet shows for the package. Only the copies BESIDE the natives
carry the package-unique name, because only those land in a consumer's output
folder where they could collide.)
    one dependency: CodeBrix.VideoPlayback.MitLicenseForever

THE ONE csproj LINE WORTH CHECKING THE PACKAGE FOR. The natives are packed with

    <None Include="@(_Dav1dNativeLibrary)" Pack="true" PackagePath="runtimes\" />

and the package path is the BARE folder on purpose. NuGet appends the item's own
%(RecursiveDir)%(Filename)%(Extension) to a package path that names a folder, so
writing PackagePath="runtimes\%(RecursiveDir)" - which reads as though it ought
to be right - produces
runtimes/linux-x64/native/linux-x64/native/libdav1d.so, and a %(Link) is appended
in the same place with the same effect. Two further traps sit beside it: the
files must be kept out of the SDK's default item globs (DefaultItemExcludes),
because a metadata-less duplicate None item wins and the natives vanish from the
package entirely; and the output copy has to go through ContentWithTargetPath
rather than a second Content item, because NuGet's pack collects None and Content
together and de-duplicates by identity. After ANY change to those items, unzip
the package and look. The expected listing is above.

THE PER-NATIVE LICENCE FILE IS NAMED LICENSE-Dav1d.txt (Jeremy, 2026-08-29).
A file named plainly LICENSE beside a native collides in a consumer's OUTPUT
FOLDER with any other package shipping a same-named file there (CodeBrix.
PdfRasterizer does; whichever is copied last wins and one licence text goes
missing). The family convention is therefore: per-native licence files carry a
package-unique name. Every committed runtimes/<rid>/native/ file, every build
script under dav1d-native-tools/ (so future builds produce the new name), and
every document naming the file were renamed together on 2026-08-29. It was never
a compliance problem either way - the licence and the notices also sit in the
package root, which is what BSD-2-Clause clause 2 asks for - the rename removes
the output-folder ambiguity. (An earlier note here claimed CodeBrix.Audio also
ships such a file; that was checked against the published package and is wrong -
CodeBrix.Audio ships no per-native licence file.)


PROVENANCE / VENDORED SOURCES
================================================================================
UPSTREAM dav1d: 1.5.4 plus one commit, 52b9d3d3ec525f5a20849145fa0e879d585f4911
(2026-07-09, an aarch64 shared-library correctness fix). The full source snapshot
is in dav1d-native-tools/dav1d/ with its own UPSTREAM.txt and COPYING;
dav1d-native-tools/BUILD-PROVENANCE.txt records how each native was built.
Licence: BSD-2-Clause, "Copyright (c) 2018-2025, VideoLAN and dav1d authors".

The pre-strip twin of every shipped binary is COMMITTED under
dav1d-native-tools/unstripped/<rid>/ (its README.txt has the rule and the
verification recipe); they exist for crash triage and are shipped nowhere. All
seven are stored - the three Linux twins on 2026-09-01, the two macOS twins
(each a dylib plus its .dSYM bundle) on 2026-09-05, and both Android twins on
2026-09-28 UTC. Windows has none, by design.

The dav1d API version is 7.0.0. The binding checks that at start-up and refuses
anything else, because the structure layouts below are pinned to those headers
and would be wrong against another major version.

CONFORMANCE STREAMS AND PLAYBACK ASSETS ARE NOT THIRD-PARTY CONTENT. Both sets
were encoded here from ffmpeg lavfi synthetic generators and are deliberately
absent from THIRD-PARTY-NOTICES.txt - see dav1d-native-tools/test-vectors/
README.txt and tests/assets/ASSETS.txt, each of which explains why listing them
would be a false statement about their origin.


DESIGN NOTES
================================================================================

The allocator: how dav1d comes to write into the host's memory
--------------------------------------------------------------------------------
dav1d lets its caller supply the memory it decodes into, through a
Dav1dPicAllocator with an allocate and a release callback. What it asks of that
memory is: plane pointers aligned to 64 bytes, both dimensions rounded up to a
multiple of 128 samples, 64 bytes of slack after the allocation for the vector
code to over-read into, and the two chroma planes sharing one stride.

That is, word for word, the contract CodeBrix.VideoPlayback's
IVideoFrameBufferPool already promises. It was written to be this contract. So
Dav1dFrameAllocator does no reformatting at all: it maps the picture's layout and
bit depth to a VideoFrameBufferDescriptor, rents, and writes the pool buffer's
own plane pointers and strides into the Dav1dPicture. SupportsExternalBuffers is
true and means it.

PinnedFrameBufferPool leaves 64 bytes of slack after EACH plane rather than only
after the last, which is strictly more than dav1d requires. It does not do
dav1d's own trick of adding 64 bytes to a stride that is a multiple of 1024 to
avoid cache-set aliasing; that is a performance nicety, not a correctness
requirement, and it belongs in the pool if it is ever wanted.

The two callbacks are static methods marked [UnmanagedCallersOnly] and taken as
function pointers, not delegates: nothing has to be kept alive against collection
and the binding stays ahead-of-time friendly. Neither may let an exception reach
native code, so both catch everything - allocate answers ENOMEM, release swallows
it, on the grounds that losing a buffer is bad and crashing the process on
somebody else's frame thread is worse.

allocator_data on each picture is a GCHandle to the VideoFrameBuffer, so the
release callback knows which buffer to give back without a lookup. The cookie
both callbacks receive is a GCHandle to the Dav1dFrameAllocator itself.

That handle is COUNTED rather than simply freed when the decoder closes. dav1d
copies the allocator into every picture it allocates, so a picture can outlive
the decoder that produced it and its release callback still has to work. The
count starts at one - the decoder's own share - rises with every allocation, and
falls with every release and when the decoder is disposed; the handle goes when
it reaches zero.

The reference count: two counts, stacked
--------------------------------------------------------------------------------
A buffer must go home when NOBODY is reading it, and there are two parties who
might be: the application, holding a VideoFrame, and dav1d, holding the same
picture as a prediction reference for later frames. Neither count knows about the
other.

They are stacked. The managed VideoFrame count sits over exactly ONE
dav1d_picture_ref; dav1d's own count sits under it. When the managed count
reaches zero, dav1d_picture_unref runs; if that was dav1d's last reference too,
dav1d calls the release callback and the buffer goes back to the pool. If it was
not, the buffer correctly stays out until dav1d is finished.

The join is Dav1dPictureLease. VideoFrame returns its buffer to "the pool it was
created with", so the lease IS that pool: an IVideoFrameBufferPool whose Return
calls dav1d_picture_unref on the one picture it holds, and whose Rent throws
because a lease never gives buffers out. Leases are recycled by the decoder, and
each owns one Dav1dPicture in native memory. Return may arrive on any thread -
whichever drops the last reference - and does.

THE FRAME OBJECT GOES THROUGH THE LEASE TOO. Because a VideoFrame is created
with the LEASE as its pool rather than with the session's pool, the lease is also
what VideoFrame.Create asks for a frame object and what VideoFrame.Dispose hands
one back to. It forwards both straight on to the session's pool, so every lease
of a session shares one free list rather than keeping one each.

That forwarding is not a nicety. Until 2026-08-29 the recycling was reachable
only by type-testing for PinnedFrameBufferPool inside VideoFrame.Create, and for
this binding the answer was permanently no - the whole point of the lease is that
it stands between the frame and the session's pool - so every decoded picture
allocated a frame object. Measured at 128 bytes a frame, which is 7.7 KB a second
at 60 frames a second. CodeBrix.VideoPlayback then grew TakeFrame and ReturnFrame
as DEFAULT interface methods on IVideoFrameBufferPool (additive; existing
implementations were untouched), PinnedFrameBufferPool overrode them onto the
internal free list it already had, and the lease forwards. The measurement is now
zero bytes over 600 decoded frames, and
Dav1dZeroCopyTests.A_warm_decode_loop_allocates_nothing_at_all keeps it there.

Input: dav1d reads the packet where it lies
--------------------------------------------------------------------------------
A VideoPacket's memory is only valid for the duration of SendPacket, but dav1d
keeps a reference to bitstream data until it has finished parsing it - which may
be several calls later. So the bytes are copied ONCE, into a block from
Dav1dInputBufferPool, and dav1d_data_wrap points dav1d at that block with a free
callback. dav1d does not copy it again.

The blocks use GC.AllocateUninitializedArray<byte>(..., pinned: true), so their
addresses never move. CoreCLR uses its pinned object heap; Mono on Android uses
its pinned-array allocator. The free callback may run on any thread; the pool is thread-safe and, like
the allocator handle, defers its own teardown until dav1d has given everything
back.

The back-pressure loop
--------------------------------------------------------------------------------
dav1d_send_data answers DAV1D_ERR(EAGAIN) when it is already holding data, and
leaves the caller's Dav1dData exactly as it was; on success it zeroes it. So the
protocol is: offer, and if the answer is "try again", pull frames and offer THE
SAME value again. IVideoDecoder.SendPacket returning false says precisely that,
so the two contracts line up and nothing is invented.

The binding holds the wrapped packet until it is taken. A caller who offers a
DIFFERENT packet after a refusal - which the contract says not to do - gets the
held one sent and the new one taken as well, rather than silently dropped: the
one outcome nobody could debug.

EAGAIN is the ONLY negative value that is not an exception. Everything else
becomes a Dav1dException carrying the C errno name. Note that the errno numbers
are the ones of the platform dav1d was COMPILED for, and EAGAIN is 11 on Linux
and Windows but 35 on macOS - Dav1dErrorCodes has the per-platform table and a
test checks it on whichever platform it runs.

Draining
--------------------------------------------------------------------------------
dav1d_get_picture reads its drain flag and then SETS it, so the first call after
a send never enters the drain path. A host that pulls until "nothing yet" and
stops would therefore lose the tail of a frame-threaded stream. After Drain(),
the binding retries once past the first "nothing yet" and only then reports
false. Dispose drains and then closes, so buffers dav1d was still holding go back
to the pool in an orderly way; buffers behind frames the application still holds
stay valid, and their leases release them later.

Structure layouts
--------------------------------------------------------------------------------
Every structure is blittable and hand-written against the vendored headers, so
LibraryImport emits no marshalling code and a call is a direct transition. That
is fast and, if an offset is wrong, silently catastrophic - the result is not an
exception but a plausible number read out of the middle of another field.

So every size and offset is restated as a constant on Dav1dNativeLayout, and
Dav1dNativeLayoutTests compares those constants against what the managed
declarations actually produce. The constants came from a C program compiled
against dav1d-native-tools/dav1d/include/dav1d. To re-run it after re-vendoring
dav1d:

    cat > layout.c <<'END'
    #include <stdio.h>
    #include <stddef.h>
    #include "dav1d/dav1d.h"
    #define O(t,f) printf("%-24s %-28s %4zu\n", #t, #f, offsetof(t, f))
    #define S(t)   printf("SIZEOF %-24s %4zu\n", #t, sizeof(t))
    int main(void){ S(Dav1dSettings); O(Dav1dSettings, allocator); /* ... */ return 0; }
    END
    gcc -Idav1d-native-tools/dav1d/include layout.c -o layout && ./layout

Two structures - Dav1dSequenceHeader and Dav1dFrameHeader - are declared with
LayoutKind.Explicit and only the fields the binding reads. Both are large and
almost entirely coding state a player has no use for; an explicit layout states
plainly which bytes are depended on, and carries the FULL native size so
dav1d_parse_sequence_header has somewhere real to write.

Logging
--------------------------------------------------------------------------------
The decoder ALWAYS installs a logging hook, whether or not the application asked
for one, because dav1d's default is to write to standard error and a library has
no business doing that on an application's behalf. With no application logger the
messages are captured and dropped, except that the most recent one is folded into
a Dav1dException when decoding fails - it is usually the sentence that explains
the error code.

The messages are printf format strings with a va_list, and this binding does NOT
expand them: a va_list is __va_list_tag* on x86-64 System V, a 32-byte structure
on AArch64 Linux and a char* on macOS ARM64 and Windows, and there is no portable
way to hand one back to a formatting function from managed code. So a message
carrying values arrives with its conversions intact. Where a number really
matters - the frame-size limit - the binding states it in the exception itself
rather than relying on the log.

Colour
--------------------------------------------------------------------------------
dav1d numbers the primaries, transfer characteristic, matrix coefficients and
chroma sample position exactly as the AV1 specification does, and so does
CodeBrix.VideoPlayback, so those four are a straight cast. The range is not: AV1
has one "full range" flag, and the library distinguishes studio, full and "the
stream did not say". A stream that states nothing reads Unspecified, and
VideoColorInfo.Resolve turns that into the library's own choice - which for
standard-definition content is BT.601, not BT.709.


UNSTRIPPED macOS BINARIES - STORED (done 2026-09-05, open since 2026-09-01)
================================================================================
DONE. The osx-arm64 and osx-x64 pre-strip dylibs and .dSYM bundles, built on the
Mac on 2026-08-29, were copied from that machine's git-ignored output/ tree into
the committed dav1d-native-tools/unstripped/ on 2026-09-05, and SHA256SUMS and
unstripped/README.txt were extended to record them. Nothing remains to be done
on the Mac for this item; it is item J1 in
~/ClaudeHome/MASTER_LIST_videoplayback_remaining_work_2026-09-01.txt on the
Linux laptop (dev-machine notes, not part of this repository) and can be closed
there.

The osx-x64 sharp edge did NOT bite: that machine's output/ tree had not been
rebuilt since 2026-08-29, so the stored unstripped dylib is the same build as
the shipped one. Both proofs were run before the copy and again afterwards from
the committed location, and all of them passed:

    shasum -a 256 of each stored dylib equals its RID's "SHA256 unstripped"
    line in dav1d-native-tools/BUILD-PROVENANCE.txt -
      osx-arm64  3ea0c3c6f06e777845442df46eeea727cb1949ca2a4be709e8c783f06c0ceeb8
      osx-x64    ff18cfe1da5fba9b536f4f8853ff1dbfb52c1f776c03983ca6318db369a74177

    dwarfdump --uuid prints one LC_UUID per RID across all three of the stored
    dylib, its .dSYM and src/CodeBrix.VideoPlayback.Dav1d/runtimes/<rid>/
    native/libdav1d.dylib -
      osx-arm64  70E56A37-E2AB-304E-A4B2-C0C6EC4F3FE6
      osx-x64    134196EB-C570-3C80-AFAB-11581155E80C

Because the shipped osx-x64 binary was not replaced, this is a documentation
and debug-artifact commit only: NO republish of the package is needed.

The osx-x64 warning still stands for any FUTURE rebuild - nasm 3.02 makes that
link alternate between two legitimate LC_UUIDs (BUILD-PROVENANCE,
"Reproducibility"), so a rebuilt unstripped dylib may carry the other UUID and
be useless for triage. Never store such a file beside a shipped binary it does
not match: adopt the fresh build into runtimes/osx-x64/native/ AND store its
unstripped mate in the same commit (unstripped/README.txt, "THE RULE"), which
does mean a republish. osx-arm64's UUID is stable across runs.

Windows: NOTHING to store, by design - the release builds emitted no debug
information at all (see unstripped/README.txt). Do not go looking for .pdb
files.


ANDROID BUILD AND VALIDATION
================================================================================
The Android natives target API 33 using NDK 30.0.16248370, with 16 KB ELF load
alignment. android-arm64 and android-x64 are separate Bionic builds; desktop
Linux assets must never be reused for Android. Native source and patches follow
the same rules as the desktop builds. Read dav1d-native-tools/android/README.txt
for tool installation, offline builds, target ABI/layout checks and device gates.

The managed assembly stays net10.0. Dav1dLibrary recognizes Android before Linux
and uses NativeLibrary's existing platform-loader fallback to open the library
packaged into the APK. The NuGet runtimes convention supplies the native assets;
package consumers need no custom MSBuild targets or native copy instructions.

tests/CodeBrix.VideoPlayback.Dav1d.AndroidTests is intentionally outside the
desktop solution. It consumes a locally PACKED package and the published
Android audio backend, so it verifies actual NuGet-to-APK asset flow. Its README
contains commands and the Audio.Core dependency requirement. Ordinary desktop builds and
tests still need no Android workload or NDK.

2026-09-28 UTC validation on Android 13/API 33 hardware:
  ARM64: Samsung SM-G781U1; x64: HP 87FE laptop, Android-x86. Both 4 KB pages.
  Native smoke/open/close and all seven conformance hashes passed for both.
  Managed Debug and trimmed/profiled-AOT Release decoder checks passed for both,
  including all hashes twice, flush/drain, forced GC, zero-copy pointers,
  retained-frame disposal on another thread, BGRA conversion and video-only
  session playback/pause/seek. Debug audio integration passed with Vorbis/Opus;
  Release AV1 + Opus playback also passed on both devices. With local fixed
  Audio.Core 1.0.271.274, the FULL Release suite passed, including AV1 + Vorbis
  playback, pause, seek and drain, using packed Dav1d 1.0.271.248.
  Desktop regression: 90 tests, 89 passed, 1 documented audible-test skip.
  No x64 emulator was used. Actual 16 KB page execution remains unverified.

Published Audio.Core 1.0.269.1270 has an independent Release Vorbis SIGSEGV on
both devices. A Core-only reproducer isolated variable negative Unsafe.Add
offsets in its MDCT. The sibling CodeBrix.Audio repository fixes five offsets
with signed native-sized arithmetic; Audio.Android and Opus need no source
changes. Local Core 1.0.271.274 passes the full integration checks above.
Publish fixed Core before raising downstream dependency pins; consumers may
also reference it directly alongside the existing Android backend. The test
app accepts -p:AudioCoreVersion=... for this coordinated validation. Its README
records the exact dependencies, commands and remaining publication step.


WHAT REMAINS TO BE VERIFIED ON DESKTOP, AND WHERE
================================================================================
The suite below - the conformance hashes, the zero-copy path, the release
threads, the back-pressure loop, the probe, the 10-bit path, the frame-size
guard, the library resolver, the API version guard, and whole-file playback -
has been verified on TWO platforms so far:

    linux-x64       the original platform.
    osx-arm64       VERIFIED 2026-09-05 on macOS 26.5.1, Apple Silicon, .NET SDK
                    10.0.400. `dotnet build -c Release` 0 warnings / 0 errors;
                    `dotnet test -c Release` 90 tests, 89 passed, 0 failed, and
                    the single documented skip (the opt-in audible playback
                    test, CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS unset). The runner
                    reported net10.0|arm64, so this was the arm64 slice.
                    Two macOS-specific items were cleared with it:
                      * THE errno TABLE. Dav1dErrorCodes.UsesMacErrnoTable is
                        true here, so Try_again_is_the_platforms_own_negated_
                        EAGAIN asserted -35 rather than -11 and passed. That
                        branch had never been executed before.
                      * THE INSTALL NAME. otool -D on the shipped dylib prints
                        @rpath/libdav1d.dylib, for osx-x64 as well as osx-arm64,
                        so install_name_tool -id did its job on both.

Per §6.5 of the programme plan, each remaining device must run the SAME suite -
it is the per-RID verification, not a smoke test - and record the result:

    linux-arm64     a Pi-class board. NEON, DotProd and i8mm assembly paths.
    linux-riscv64   the RISC-V board. RVV assembly, detected at run time from
                    AT_HWCAP; qemu-user first, then real hardware.
    osx-x64         the Intel slice. It can run under Rosetta on the Apple
                    Silicon Mac, but NOT as things stand: that machine's x64
                    .NET tree (/usr/local/share/dotnet/x64) carries only SDK
                    8.0.401 / runtime 8.0.8, and this project targets net10.0.
                    Install an x64 .NET 10 runtime and the suite can be run
                    there with `arch -x86_64`. Rosetta itself is present.
    win-x64         a Windows x64 box. Also the place to check that
                    -Db_vscrt=static_from_buildtype really removed the need for a
                    VC redistributable.
    win-arm64       a Windows ARM64 box.

Two things to watch for specifically on the platforms not yet run:

  * THE errno TABLE. EAGAIN is 35 on macOS, not 11. Dav1dNativeLayoutTests checks
    the value for the platform it runs on. PROVEN on osx-arm64 on 2026-09-05
    (above); the check keys off the OS rather than the architecture, so osx-x64
    will exercise the same branch and is not needed to establish it.
  * THE STRUCTURE OFFSETS. They are the same on every platform this package ships
    for, because all nine use a 64-bit model in which int and enum are four bytes
    and a pointer is eight, and no declaration here contains a C long. The layout
    tests are cheap and run everywhere; they are the proof rather than the
    assumption.
================================================================================
