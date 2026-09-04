================================================================================
EXTRAS-README: CodeBrix.VideoPlayback.Dav1d
Content in this repository that is not part of the NuGet package
================================================================================

Three folders hold things that never ship: the tooling that builds the native
libraries, the streams the binding is checked against, and the files the
end-to-end tests play.


dav1d-native-tools/
================================================================================
Everything needed to build the seven native dav1d libraries, and nothing outside
this repository. That is the rule the folder exists for: no clone, no download,
no fetch of source or test data during a build. The only things allowed to live
outside are the tools installed on the build machine - compilers, meson, ninja,
nasm, Xcode, Visual Studio, podman - and every one of them is named, with a
version floor and install instructions, in the platform README that needs it.

The purpose is that these libraries can be rebuilt years from now from this
repository alone, with nothing that may have disappeared in the meantime.

    dav1d/              the vendored dav1d source snapshot - 1.5.4 plus one
                        commit - with its COPYING and an UPSTREAM.txt naming the
                        commit, tag, date and URL. This is what gets compiled;
                        never a clone.
    linux/              the container route (digest-pinned images) and the bare
                        Debian-host route, for linux-x64, linux-arm64 and
                        linux-riscv64.
    macos/              build scripts and the x86_64 crossfile, for osx-arm64 and
                        osx-x64 from one Apple Silicon Mac.
    windows/            build scripts for win-x64 and win-arm64.
    patches/            local changes to the vendored source, if there ever are
                        any. There are none.
    test-vectors/       see below.
    unstripped/         COMMITTED, unlike output/. The pre-strip twin of every
                        native library the package ships, one folder per runtime
                        identifier, with SHA256SUMS and a README.txt carrying the
                        rule that keeps them in step with the shipped binaries.
                        They exist so a crash dump from a stripped release binary
                        can still be symbolised. Nothing here ships and nothing
                        here is an input to any build or pack step. The three
                        Linux twins are stored; the two macOS twins are still to
                        be copied from the Mac, and the Windows builds produce no
                        debug information to store.
    output/             gitignored, and disposable: one build's own working tree
                        - freshly built, stripped and staged libraries, its own
                        transient pre-strip copies, BUILD-INFO.txt and
                        SHA256SUMS. Delete the whole folder and the next build
                        recreates it. The pre-strip copies that are meant to LAST
                        are the committed ones in unstripped/ above.
    BUILD-PROVENANCE.txt
                        how each committed native was actually built, and by whom
                        and when.
    smoke-test.c        dlopen, dav1d_version, close. The first thing a fresh
                        build is asked to do.

No build in this repository compiles any of this. The libraries are built on the
machine that can build them and committed; a dotnet build never touches a
compiler.


dav1d-native-tools/test-vectors/
================================================================================
Six small AV1 bitstreams in IVF containers, plus EXPECTED.md5, the hashes their
decoded pictures must produce. They cover 8-bit and 10-bit, 4:2:0 and 4:4:4, two
different encoders, an odd frame size, and film grain both applied and not.

They are used TWICE, which is why they live here rather than under tests/:

  * every native build decodes them with the dav1d command-line tool it has just
    built, and fails if a hash does not match - which is what would catch a
    broken NEON or RVV path on hardware nobody has to hand;
  * the managed test suite decodes the same files through the binding and
    compares against the same file, so the two can never drift apart.

The test project links them in rather than keeping a second copy.

They are not third-party content: every stream was encoded here from an ffmpeg
lavfi synthetic generator by generate-test-vectors.sh in that folder. The
README.txt beside them explains that, and explains the film-grain flag on every
line of EXPECTED.md5 - which matters, because AV1 film grain is synthesised by
the decoder and one stream legitimately has two correct outputs.


tests/assets/
================================================================================
Three short WebM files - AV1 video with Opus audio, with Vorbis audio, and with
no audio - played whole through a VideoPlaybackSession by the end-to-end tests.
They answer a different question from the conformance streams: not "are the
pictures right" but "does a real container, demuxer, clock and presenter play a
real file once this decoder is registered".

    generate-assets.sh  the exact ffmpeg commands, as code. Run only to change
                        what the files contain; the files are committed, so the
                        test suite does not require ffmpeg.
    ASSETS.txt          what each file holds, why, and where they came from.
    *.webm              the three files.

Also not third-party content: synthetic picture and a sine tone, generated here.

One test in the suite plays the Vorbis file THROUGH THE SOUND DEVICE, and runs
only when CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 is set in the environment. Without
it the test skips and says so. A machine with no audio device - a container, a
headless build - must be able to run the whole suite green.
================================================================================
