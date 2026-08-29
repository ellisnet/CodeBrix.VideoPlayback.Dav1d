================================================================================
EXTRAS-README: CodeBrix.VideoPlayback.Dav1d
Samples, tools and other content in this repository that is not part of a NuGet
package
================================================================================

dav1d-native-tools/ - everything needed to build the native dav1d libraries
==========================================================================
  Path:   dav1d-native-tools/README.txt (start there)

  WHAT IT IS
    The vendored dav1d source snapshot, the build scripts and container
    recipes for the seven runtime identifiers (Windows x64 + ARM64, Linux x64
    + ARM64 + RISC-V 64, macOS Intel + Apple Silicon), one README per
    platform listing the tools to install on that build machine, the
    conformance test vectors, and the provenance record of every shipped
    binary. Builds run from this folder only and never reach outside the
    repository; only the tools named in the platform READMEs are external.
    None of it is packed into the NuGet package - only the built libraries
    are, under runtimes/<rid>/native/.

  See dav1d-native-tools/README.txt and the platform READMEs beneath it for
  prerequisites and the exact commands.
================================================================================
