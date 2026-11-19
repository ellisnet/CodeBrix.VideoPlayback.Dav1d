================================================================================
dav1d-native-tools/patches - local changes to the vendored dav1d source
================================================================================

AS OF 2026-11-19 THIS FOLDER HOLDS ONE PATCH:

  001-msvc-1952-has-include-next.patch
      Windows only in effect. MSVC 19.52 (Visual Studio 2026 18.12) rejects
      include/compat/msvc/stdatomic.h's
          #if defined(__clang__) && __has_include_next(<stdatomic.h>)
      with C7780 + C1017; the patch nests the test, keeping upstream's branch
      choice. First applied to the win-x64 and win-arm64 builds of 2026-11-19.
      The file's own header says what it fixes, the upstream status and when
      it can be dropped.

Until then (2026-08-28 to 2026-11-18) the folder was empty and the vendored
snapshot in ../dav1d/ built as-is for every runtime identifier; it still does
for everything except MSVC 19.52 and later.

WHY THE FOLDER EXISTS ANYWAY
--------------------------------------------------------------------------------
The vendored source in ../dav1d/ is an unmodified upstream snapshot and must
stay that way - that is what makes it verifiable against upstream (see
../dav1d/UPSTREAM.txt). If a build ever does need a source change, it belongs
here as a patch file, never as an edit in ../dav1d/.

HOW A PATCH WOULD BE USED
--------------------------------------------------------------------------------
  1. Name it NNN-short-description.patch (e.g. 001-riscv-align-fix.patch),
     produced with `git diff` or `diff -u` against the vendored tree, with paths
     relative to ../dav1d/ (i.e. -p1 applies from inside dav1d/).

  2. Head the file with a comment block: what it fixes, which platforms it
     applies to, the upstream issue or merge-request link if there is one, and
     the date it can be dropped (normally: when the vendored snapshot is next
     bumped to a release that contains the fix).

  3. The build scripts copy ../dav1d/ into a scratch directory, apply every
     patch in this folder in filename order with `patch -p1`, and build there.
     The copy step already exists in linux/container-build.sh; the Windows and
     macOS scripts contain the same step. If a patch fails to apply the build
     stops - patches are never applied "best effort".

  4. Record it in ../BUILD-PROVENANCE.txt and in ../../THIRD-PARTY-NOTICES.txt
     (the "Modifications" line of the dav1d entry), because a patched binary is
     no longer plain upstream dav1d and the licence requires the change to be
     stated.
