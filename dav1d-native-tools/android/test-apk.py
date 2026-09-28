#!/usr/bin/env python3
"""Validate NuGet native packaging, then install/run the managed tests on one explicit device."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shlex
import struct
import subprocess
import time
import zipfile

from verify import TOOLS, sha256

APP = "com.codebrix.dav1d.tests"


def test_apk(apk, adb, serial, result_path, codec_only=False, audio_only=False):
    prefix = [adb, "-s", serial]

    def shell(*args, check=True):
        return subprocess.run([*prefix, "shell", shlex.join(str(a) for a in args)],
                              text=True, capture_output=True, check=check)

    abi = shell("getprop", "ro.product.cpu.abi").stdout.strip()
    if abi not in ("arm64-v8a", "x86_64"):
        raise SystemExit(f"Unsupported ABI: {abi}")
    api = int(shell("getprop", "ro.build.version.sdk").stdout.strip())
    if api < 33:
        raise SystemExit(f"Requires Android 13/API 33 or newer, got {api}")
    native_hashes = {}
    # The test app deliberately packages both ABIs, even when testing one device.
    with zipfile.ZipFile(apk) as archive, apk.open("rb") as raw:
        names = {name for name in archive.namelist() if name.endswith("/libdav1d.so")}
        expected = {f"lib/{name}/libdav1d.so" for name in ("arm64-v8a", "x86_64")}
        if names != expected:
            raise RuntimeError(f"Unexpected dav1d APK entries: {names}")
        for android_abi, rid in [("arm64-v8a", "android-arm64"), ("x86_64", "android-x64")]:
            name = f"lib/{android_abi}/libdav1d.so"
            checksum = hashlib.sha256(archive.read(name)).hexdigest()
            shipped = TOOLS.parent / "src/CodeBrix.VideoPlayback.Dav1d/runtimes" / rid / "native/libdav1d.so"
            if checksum != sha256(shipped):
                raise RuntimeError(f"APK does not contain the shipped {rid} library")
            info = archive.getinfo(name)
            # Uncompressed libraries may load directly from the APK; their ZIP offset matters too.
            if info.compress_type == zipfile.ZIP_STORED:
                raw.seek(info.header_offset + 26)
                name_size, extra_size = struct.unpack("<HH", raw.read(4))
                offset = info.header_offset + 30 + name_size + extra_size
                if offset % 16384 != 0:
                    raise RuntimeError(f"{name} is not 16 KB ZIP aligned")
            native_hashes[rid] = checksum
    subprocess.run([*prefix, "install", "--no-incremental", "-r", str(apk)], check=True)
    shell("am", "force-stop", APP)
    shell("run-as", APP, "rm", "-f", "files/results.txt", "files/progress.txt")
    shell("am", "start", "-W", "-n", APP + "/" + APP + ".MainActivity",
          "--ez", "audio", str(not codec_only).lower(), "--ez", "audioOnly", str(audio_only).lower())
    deadline = time.monotonic() + 180
    while time.monotonic() < deadline:
        result = shell("run-as", APP, "cat", "files/results.txt", check=False).stdout
        crashed = shell("pidof", APP, check=False).returncode != 0
        if crashed and not result.startswith(("PASS\n", "FAIL\n")):
            result = "FAIL\nApp process exited before completing.\n" + shell(
                "run-as", APP, "cat", "files/progress.txt", check=False).stdout
        if result.startswith(("PASS\n", "FAIL\n")):
            report = {"tested_utc": datetime.now(timezone.utc).isoformat(), "serial": serial,
                      "abi": abi, "api": api, "model": shell("getprop", "ro.product.model").stdout.strip(),
                      "page_size": int(shell("getconf", "PAGE_SIZE").stdout.strip()),
                      "apk_sha256": sha256(apk), "native_sha256": native_hashes,
                      "mode": "audio-only" if audio_only else "codec-only" if codec_only else "full",
                      "result": result}
            result_path.parent.mkdir(parents=True, exist_ok=True)
            result_path.write_text(json.dumps(report, indent=2) + "\n")
            print(result, flush=True)
            if result.startswith("FAIL"):
                raise SystemExit(1)
            return
        time.sleep(1)
    raise SystemExit("No final app result after 180 seconds. Inspect adb logcat for this app's crash or timeout.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--adb", default="adb")
    parser.add_argument("--serial", required=True)
    parser.add_argument("--result", type=Path, required=True)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--codec-only", action="store_true", help="Skip audio integration; still test video-only sessions")
    mode.add_argument("--audio-only", action="store_true", help="Isolate audio output without loading dav1d")
    args = parser.parse_args()
    test_apk(args.apk, args.adb, args.serial, args.result, args.codec_only, args.audio_only)
