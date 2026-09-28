#!/usr/bin/env python3
"""Inspect Android ELF assets without loading them on the build host."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess

if not __debug__:
    raise SystemExit("Run the Android build/verification tools without Python -O; their gates must stay enabled.")

TOOLS = Path(__file__).resolve().parents[1]
PINS = json.loads((TOOLS / "android/pins.json").read_text())
ARCHITECTURES = {"arm64": ("aarch64", 183), "x64": ("x86_64", 62)}


def output(*args):
    return subprocess.check_output([str(arg) for arg in args], text=True)


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def verify(library, arch, toolchain):
    library = Path(library)
    data = library.read_bytes()
    assert data[:6] == b"\x7fELF\x02\x01", "Expected little-endian ELF64"
    assert struct.unpack_from("<H", data, 16)[0] == 3, "Expected a shared object"
    assert struct.unpack_from("<H", data, 18)[0] == ARCHITECTURES[arch][1], "Wrong architecture"
    phoff = struct.unpack_from("<Q", data, 32)[0]
    phsize, phnum = struct.unpack_from("<HH", data, 54)
    alignments = []
    android_api = None
    for index in range(phnum):
        kind, flags, offset, address, _, size, _, alignment = struct.unpack_from(
            "<IIQQQQQQ", data, phoff + index * phsize)
        if kind == 1:  # PT_LOAD
            assert alignment >= 16384 and alignment & (alignment - 1) == 0, "Not 16 KB aligned"
            assert offset % 16384 == address % 16384, "Invalid load alignment"
            assert flags & 3 != 3, "Writable executable segment"
            alignments.append(alignment)
        if kind == 4:  # PT_NOTE
            cursor = offset
            while cursor + 12 <= offset + size:
                namesz, descsz, note_type = struct.unpack_from("<III", data, cursor)
                cursor += 12
                name = data[cursor:cursor + namesz].rstrip(b"\0")
                cursor += (namesz + 3) & ~3
                if name == b"Android" and note_type == 1 and descsz >= 4:
                    android_api = struct.unpack_from("<I", data, cursor)[0]
                cursor += (descsz + 3) & ~3
    assert alignments, "No load segments"
    assert android_api == PINS["api"], f"Expected API 33, got {android_api}"
    readelf = toolchain / "llvm-readelf"
    dynamic = output(readelf, "--dynamic", library)
    dependencies = re.findall(r"\(NEEDED\).*?\[(.*?)\]", dynamic)
    assert dependencies and set(dependencies) <= {"libc.so", "libm.so", "libdl.so"}, dependencies
    assert "GLIBC_" not in output(readelf, "--version-info", library), "Desktop glibc dependency"
    assert not re.search(r"\((TEXTREL|RPATH|RUNPATH)\)", dynamic), "Text relocations or build-host paths"
    assert re.search(r"\(SONAME\).*?\[libdav1d\.so\]", dynamic), "Wrong Android SONAME"
    exports = set(re.findall(r"\b(dav1d_\w+)$", output(toolchain / "llvm-nm", "-D", "--defined-only", library), re.M))
    imports = (TOOLS.parent / "src/CodeBrix.VideoPlayback.Dav1d/Interop/Dav1dNative.cs").read_text()
    required = set(re.findall(r'EntryPoint = "(dav1d_\w+)"', imports))
    assert required and required <= exports, f"Missing exports: {required - exports}"
    notes = output(readelf, "--notes", library)
    build_id = re.search(r"Build ID: (\w+)", notes)
    assert build_id, "Missing build ID"
    return {"rid": "android-" + arch, "api": android_api,
            "load_alignments": alignments, "dependencies": dependencies,
            "required_exports": sorted(required), "build_id": build_id[1],
            "sha256": sha256(library), "bytes": len(data)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("library", type=Path)
    parser.add_argument("--arch", required=True, choices=ARCHITECTURES)
    parser.add_argument("--toolchain", type=Path, required=True, help="NDK prebuilt bin directory")
    args = parser.parse_args()
    print(json.dumps(verify(args.library, args.arch, args.toolchain), indent=2))
