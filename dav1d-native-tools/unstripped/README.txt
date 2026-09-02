================================================================================
dav1d-native-tools/unstripped - the durable home for pre-strip binaries
================================================================================

UNLIKE output/, THIS FOLDER IS COMMITTED. It exists so the unstripped mate of
every binary shipped in the package survives the machine it was built on
(../output/ is git-ignored and disposable). These files are needed for crash
triage: a stripped release binary in a crash dump can only be symbolised from
its unstripped twin. Nothing here is shipped, and nothing here is an input to
any build or pack step.

One folder per runtime identifier, mirroring runtimes/<rid>/native/ in the
package tree:

  <rid>/libdav1d.so       the pre-strip ELF (Linux)
  <rid>/libdav1d.dylib    the pre-strip Mach-O (macOS) - see PENDING below
  <rid>/libdav1d.dylib.dSYM/
                          the macOS debug-symbol bundle - see PENDING below

Verify any file two ways:

  1. sha256 matches the "SHA256 unstripped" line of that RID's entry in
     ../BUILD-PROVENANCE.txt (and SHA256SUMS beside this file).
  2. Linux: the GNU build-id equals the shipped binary's -
        readelf -n <here>/libdav1d.so
        readelf -n ../../src/CodeBrix.VideoPlayback.Dav1d/runtimes/<rid>/native/libdav1d.so
     macOS: the LC_UUID equals the shipped dylib's (dwarfdump --uuid).

Build-ids of the stored Linux binaries (each verified equal to its shipped
twin's on 2026-09-01):

  linux-x64      e4c21b208368cd36807c52b8cd9282d506ccc5e1
  linux-arm64    6de1abac61d97a721e7079bf633427a893319f3c
  linux-riscv64  b572c681063f2af65f677118f9d86c189d97afbb

PENDING - the macOS slices
--------------------------------------------------------------------------------
osx-arm64 and osx-x64 were built on the Mac; their unstripped dylibs and .dSYM
bundles live in that machine's output/ tree and must be copied here from there.
IMPORTANT for osx-x64: the build is not byte-reproducible (two legitimate
LC_UUID variants - see its BUILD-PROVENANCE entry). The copy stored here must
be the SAME build that was adopted into runtimes/osx-x64/native/, proven by
matching LC_UUIDs - a fresh rebuild's unstripped output may not match the
shipped binary and would be useless for triage.

WINDOWS - nothing to store, by design
--------------------------------------------------------------------------------
The win-x64 and win-arm64 release builds emitted no debug information at all
("Debug symbols: none" in both BUILD-PROVENANCE entries; --buildtype=release
asks MSVC/clang-cl for none, and PE symbols live in a separate .pdb rather
than in a strippable section). The shipped DLLs are the only build products.
If Windows symbols are ever wanted, the windows/ build scripts must be changed
to request debug info first; there is no existing artifact to adopt.

THE RULE
--------------------------------------------------------------------------------
Whenever a newly built binary is adopted into runtimes/<rid>/native/, its
unstripped mate from the same build lands here in the same commit, and
SHA256SUMS is extended. A binary here that no longer matches the shipped one's
build-id/LC_UUID is stale and must be replaced, never kept alongside.
================================================================================
