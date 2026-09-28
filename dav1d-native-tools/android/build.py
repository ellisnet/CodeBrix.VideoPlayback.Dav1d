#!/usr/bin/env python3
"""Build both Android slices from vendored source; never installs or downloads tools."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import tempfile

from verify import ARCHITECTURES, PINS, TOOLS, output, sha256, verify


def run(*args, **kwargs):
    print("+", " ".join(str(arg) for arg in args), flush=True)
    subprocess.run([str(arg) for arg in args], check=True, **kwargs)


def build(arch, ndk, jobs):
    host = {"Linux": "linux-x86_64", "Darwin": "darwin-x86_64"}.get(platform.system())
    if host is None:
        raise SystemExit("Run on Linux or macOS (on Windows, use a Linux build machine).")
    toolchain = ndk / "toolchains/llvm/prebuilt" / host / "bin"
    revision = re.search(r"Pkg.Revision\s*=\s*(\S+)", (ndk / "source.properties").read_text())[1]
    if revision != PINS["ndk"]:
        raise SystemExit(f"NDK {PINS['ndk']} is pinned; found {revision}")
    meson = output("meson", "--version").strip()
    ninja = output("ninja", "--version").strip()
    assert meson == PINS["meson"], f"Expected Meson {PINS['meson']}, got {meson}"
    assert ninja == PINS["ninja"] or ninja.startswith(PINS["ninja"] + "."), ninja
    nasm = output("nasm", "-v").strip() if arch == "x64" else "not required"
    if arch == "x64":
        version = tuple(map(int, re.search(r"version ([\d.]+)", nasm)[1].split(".")))
        assert version >= tuple(map(int, PINS["nasm_minimum"].split("."))), nasm
    cpu = ARCHITECTURES[arch][0]
    compiler = toolchain / f"{cpu}-linux-android{PINS['api']}-clang"
    dest = TOOLS / "output" / ("android-" + arch)
    dest.mkdir(parents=True, exist_ok=True)
    # Clear a previous runtime result before building a different binary.
    (dest / "DEVICE-RESULT.json").unlink(missing_ok=True)
    with tempfile.TemporaryDirectory(prefix="android-build-", dir=TOOLS / "output") as scratch:
        scratch = Path(scratch)
        source, build_dir = scratch / "dav1d", scratch / "build"
        shutil.copytree(TOOLS / "dav1d", source)
        # Keep the vendored upstream snapshot pristine. Local patches, if any, apply to this copy.
        patches = sorted((TOOLS / "patches").glob("*.patch"))
        for patch in patches:
            run("patch", "-p1", "--forward", "--batch", "-i", patch, cwd=source)
        crossfile = scratch / "android.meson"
        # Fixed compilation paths keep debug information independent of the scratch directory.
        flags = [f"-ffile-prefix-map={source}=/dav1d", f"-ffile-prefix-map={build_dir}=/dav1d-build",
                 "-fdebug-compilation-dir=/dav1d-build"]
        crossfile.write_text(f"""[binaries]
c = '{compiler}'
ar = '{toolchain / 'llvm-ar'}'
strip = '{toolchain / 'llvm-strip'}'
[built-in options]
c_args = {flags!r}
c_link_args = ['-Wl,-z,max-page-size=16384', '-Wl,-z,common-page-size=16384', '-Wl,--build-id=sha1']
[properties]
needs_exe_wrapper = true
[host_machine]
system = 'android'
cpu_family = '{cpu}'
cpu = '{cpu}'
endian = 'little'
""")
        options = ["--buildtype=release", "-Ddebug=true", "-Ddefault_library=shared", "-Dbitdepths=8,16",
                   "-Denable_asm=true", "-Denable_tools=true", "-Denable_tests=false",
                   "-Denable_examples=false", "-Denable_docs=false", "-Dxxhash_muxer=disabled", "--wrap-mode=nodownload"]
        run("meson", "setup", build_dir, source, "--cross-file", crossfile, *options)
        run("ninja", "-C", build_dir, "-j", jobs)
        # Header checks prove the native ABI on the target, not the build host.
        run(compiler, "-std=c11", "-I" + str(source / "include"), "-I" + str(build_dir / "include"),
            "-c", TOOLS / "android/layout-check.c", "-o", scratch / "layout-check.o")
        library = build_dir / "src/libdav1d.so"
        (dest / "unstripped").mkdir(exist_ok=True)
        shutil.copy2(library, dest / "unstripped/libdav1d.so")
        shutil.copy2(library, dest / "libdav1d.so")
        run(toolchain / "llvm-strip", "--strip-unneeded", dest / "libdav1d.so")
        shutil.copy2(build_dir / "tools/dav1d", dest / "dav1d")
        run(toolchain / "llvm-strip", "--strip-unneeded", dest / "dav1d")
        run(compiler, "-O2", "-Wl,-z,max-page-size=16384", TOOLS / "smoke-test.c",
            "-ldl", "-o", dest / "smoke-test")
        shutil.copy2(source / "COPYING", dest / "LICENSE-Dav1d.txt")
        report = verify(dest / "libdav1d.so", arch, toolchain)
        unstripped = verify(dest / "unstripped/libdav1d.so", arch, toolchain)
        assert report["build_id"] == unstripped["build_id"]
        report.update({"built_utc": datetime.now(timezone.utc).isoformat(), "ndk": revision,
                       "clang": output(compiler, "--version").splitlines()[0],
                       "meson": meson, "ninja": ninja, "nasm": nasm,
                       "source_commit": PINS["source_commit"], "meson_options": options,
                       "patches": [p.name for p in patches], "native_layout": "passed",
                       "unstripped_sha256": sha256(dest / "unstripped/libdav1d.so"),
                       "runtime_tests": "UNRUN - run test-device.py on the target ABI"})
        (dest / "BUILD-INFO.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps(report, indent=2), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--arch", choices=["all", *ARCHITECTURES], default="all")
    parser.add_argument("--ndk", type=Path, default=os.environ.get("CODEBRIX_ANDROID_NDK"), required=not os.environ.get("CODEBRIX_ANDROID_NDK"))
    parser.add_argument("--jobs", type=int, default=4)
    args = parser.parse_args()
    if args.jobs < 1:
        parser.error("--jobs must be positive")
    for arch in ARCHITECTURES if args.arch == "all" else [args.arch]:
        build(arch, args.ndk.resolve(), args.jobs)
