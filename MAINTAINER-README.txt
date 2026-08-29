================================================================================
MAINTAINER-README: CodeBrix.VideoPlayback.Dav1d
Notes for people and agents MAINTAINING this repository - not for package
consumers, who want AGENT-README.txt instead
================================================================================


⚠️ BEFORE THIS PACKAGE IS EVER PUBLISHED - READ THIS FIRST
================================================================================
The package reference in src/CodeBrix.VideoPlayback.Dav1d.csproj currently points
at a LOCAL PACK of CodeBrix.VideoPlayback that has not been published:

    <PackageReference Include="CodeBrix.VideoPlayback.MitLicenseForever"
                      Version="1.0.241.1148" />

That version exists only in a local folder feed on the machine this was built on.
It was produced by:

    cd ~/GitHome/CodeBrix.VideoPlayback
    dotnet pack src/CodeBrix.VideoPlayback/CodeBrix.VideoPlayback.csproj -c Release \
        -o ~/ClaudeHome/localfeed_codebrix_videoplayback_2026-08-29/

and restores in this repository are done with the folder feed added on the
command line, so no nuget.config is committed and nothing about this arrangement
leaks into the package or into anybody else's build:

    dotnet restore -p:RestoreSources="/home/jeremy/ClaudeHome/localfeed_codebrix_videoplayback_2026-08-29%3Bhttps://api.nuget.org/v3/index.json"
    dotnet build   -c Release
    dotnet test    -c Release

(The %3B is an escaped semicolon; MSBuild property values cannot carry a bare
one.)

MUST BE DONE BEFORE PUBLISHING THIS PACKAGE:

  1. CodeBrix.VideoPlayback.MitLicenseForever and
     CodeBrix.VideoPlayback.Skia.MitLicenseForever must be published FIRST, on
     one version, as one event.
  2. Change the Version above to the PUBLISHED version of
     CodeBrix.VideoPlayback.MitLicenseForever.
  3. Restore from nuget.org alone, with no folder feed and with --force, so the
     locally packed copy in the global package cache cannot satisfy the
     reference and hide a mistake:

         dotnet restore --force
         dotnet build -c Release
         dotnet test  -c Release

  4. Delete the local folder feed, or move it aside, and repeat step 3 to prove
     it. A restore that still succeeds after the feed is gone is the only
     evidence that the pin is real.
  5. Only then pack and publish.

Publishing against the local version would ship a package nobody can install.


PURPOSE AND SCOPE
================================================================================
One project, one package: AV1 decoding for CodeBrix.VideoPlayback, through a
binding over dav1d, with self-built native libraries for seven platforms.

    src/CodeBrix.VideoPlayback.Dav1d/   the binding, packable
    tests/CodeBrix.VideoPlayback.Dav1d.Tests/   the suite
    dav1d-native-tools/                 everything needed to BUILD the natives
    tests/assets/                       end-to-end playback files

The package's only dependency is CodeBrix.VideoPlayback.MitLicenseForever, which
brings CodeBrix.Audio.MitLicenseForever with it. Nothing else - no SkiaSharp, no
Opus, no platform packages.


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

The seven runtimes/<rid>/native/ folders are packed into the NuGet package AND
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

Three test classes touch process-wide state - the decoder registry - and are in
the "Process-wide registries" xUnit collection so they never run beside each
other.


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
              linux-x64,linux-arm64,linux-riscv64}/native/
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

The blocks are managed byte arrays allocated on the pinned object heap, so their
addresses never move and no long-lived pinning handle fragments the ordinary
heap. The free callback may run on any thread; the pool is thread-safe and, like
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


WHAT REMAINS TO BE VERIFIED, AND WHERE
================================================================================
Everything below has been verified ON linux-x64 ONLY, because that is the only
platform this repository has been built and run on so far:

    the conformance hashes, the zero-copy path, the release threads, the
    back-pressure loop, the probe, the 10-bit path, the frame-size guard, the
    library resolver, the API version guard, and whole-file playback.

Per §6.5 of the programme plan, each remaining device must run the SAME suite -
it is the per-RID verification, not a smoke test - and record the result:

    linux-arm64     a Pi-class board. NEON, DotProd and i8mm assembly paths.
    linux-riscv64   the RISC-V board. RVV assembly, detected at run time from
                    AT_HWCAP; qemu-user first, then real hardware.
    osx-arm64       Apple Silicon. Also the only place to check that
                    install_name_tool -id @rpath/libdav1d.dylib did its job.
    osx-x64         the Intel slice, on the same Mac.
    win-x64         a Windows x64 box. Also the place to check that
                    -Db_vscrt=static_from_buildtype really removed the need for a
                    VC redistributable.
    win-arm64       a Windows ARM64 box.

Two things to watch for specifically on the platforms not yet run:

  * THE errno TABLE. EAGAIN is 35 on macOS, not 11. Dav1dNativeLayoutTests checks
    the value for the platform it runs on, so the two macOS slices are what prove
    that branch of the table.
  * THE STRUCTURE OFFSETS. They are the same on every platform this package ships
    for, because all seven use a 64-bit model in which int and enum are four bytes
    and a pointer is eight, and no declaration here contains a C long. The layout
    tests are cheap and run everywhere; they are the proof rather than the
    assumption.
================================================================================
