#!/usr/bin/env python3
"""Run the native load/context checks and all conformance hashes on one explicit adb device."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import shlex
import subprocess
import uuid

from verify import TOOLS, sha256


def test_device(adb, serial):
    prefix = [adb, "-s", serial]

    def shell(*args):
        return subprocess.check_output([*prefix, "shell", shlex.join(str(a) for a in args)], text=True).strip()

    abi = shell("getprop", "ro.product.cpu.abi")
    arch = {"arm64-v8a": "arm64", "x86_64": "x64"}.get(abi)
    if arch is None:
        raise SystemExit(f"Unsupported device ABI: {abi}")
    api = int(shell("getprop", "ro.build.version.sdk"))
    if api < 33:
        raise SystemExit(f"Requires Android 13/API 33 or newer, got {api}")
    dest = TOOLS / "output" / ("android-" + arch)
    build_info = json.loads((dest / "BUILD-INFO.json").read_text())
    library_hash = sha256(dest / "libdav1d.so")
    if build_info["sha256"] != library_hash:
        raise SystemExit("The library no longer matches BUILD-INFO.json; rebuild it.")
    remote = "/data/local/tmp/codebrix-dav1d-" + uuid.uuid4().hex
    report = {"tested_utc": datetime.now(timezone.utc).isoformat(), "serial": serial,
              "model": shell("getprop", "ro.product.model"), "abi": abi, "api": api,
              "page_size": int(shell("getconf", "PAGE_SIZE")), "sha256": library_hash,
              "conformance": []}
    shell("mkdir", remote)
    try:
        for name in ["libdav1d.so", "dav1d", "smoke-test"]:
            subprocess.run([*prefix, "push", str(dest / name), remote + "/" + name], check=True)
        subprocess.run([*prefix, "push", str(TOOLS / "test-vectors"), remote + "/test-vectors"], check=True)
        shell("chmod", "755", remote + "/dav1d", remote + "/smoke-test")
        assert shell("sha256sum", remote + "/libdav1d.so").split()[0] == library_hash
        smoke = shell(remote + "/smoke-test", remote + "/libdav1d.so")
        print(smoke, flush=True)
        report["smoke_test"] = smoke
        for line in (TOOLS / "test-vectors/EXPECTED.md5").read_text().splitlines():
            if not line.strip() or line.startswith("#"):
                continue
            file_name, flags, expected = line.split("|")
            actual = shell("env", "LD_LIBRARY_PATH=" + remote, remote + "/dav1d",
                           "-i", remote + "/test-vectors/" + file_name, "--muxer", "md5", "-o", "-",
                           *shlex.split(flags))
            if actual.strip() != expected:
                raise RuntimeError(f"{file_name} {flags}: expected {expected}, got {actual}")
            report["conformance"].append({"file": file_name, "flags": flags, "md5": actual})
            print(f"PASS {file_name} {flags}: {actual}", flush=True)
        report["result"] = "passed"
        (dest / "DEVICE-RESULT.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps(report, indent=2))
    finally:
        # Only this run's uniquely named directory, never shared device storage.
        shell("rm", "-rf", remote)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", default="adb")
    parser.add_argument("--serial", required=True, help="Explicit authorized test device; never picks a default")
    args = parser.parse_args()
    test_device(args.adb, args.serial)
