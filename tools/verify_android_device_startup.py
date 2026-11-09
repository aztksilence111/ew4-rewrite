"""Verify installed EW4 cold launches on a connected emulator without clearing app data."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(__doc__)
parser.add_argument("--adb", required=True)
parser.add_argument("--serial", required=True)
parser.add_argument("--package", default="org.ew4.reconstruction")
parser.add_argument("--version", required=True)
parser.add_argument("--abi", choices=("arm64-v8a", "x86_64"), required=True)
parser.add_argument("--apk", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--attempts", type=int, default=2)
args = parser.parse_args()
assert args.serial.startswith(("127.0.0.1:", "emulator-")), "Use an emulator for this launch test"
args.output.mkdir(parents=True, exist_ok=True)

def adb(*command, timeout=30):
    return subprocess.check_output([args.adb, "-s", args.serial, *command], timeout=timeout)

def text(*command, timeout=30):
    return adb(*command, timeout=timeout).decode("utf-8", errors="replace")

package = text("shell", "dumpsys", "package", args.package)
assert "versionName=" + args.version in package, "Installed version differs"
assert "primaryCpuAbi=" + args.abi in package, "Android selected a translated ABI"
component = text("shell", "cmd", "package", "resolve-activity", "--brief", args.package).strip().splitlines()[-1]
assert component.startswith(args.package + "/"), "Unexpected launch activity"
report = dict(package=args.package, version=args.version, abi=args.abi, serial=args.serial,
              apkSha256=hashlib.sha256(args.apk.read_bytes()).hexdigest(), launches=[], errors=0,
              scope="Installed APK emulator execution; cold processes with retained app data, not an ARM64 phone test")
try:
    for attempt in range(args.attempts):
        adb("shell", "am", "force-stop", args.package)
        start = time.monotonic()
        adb("shell", "am", "start", "-W", "-n", component)
        pid = text("shell", "pidof", args.package).strip()
        assert pid.isdigit(), "App process did not start"
        # An interrupted launch displays a native recovery button. Resume it once.
        adb("shell", "uiautomator", "dump", "/sdcard/ew4-startup-review.xml", timeout=20)
        xml = adb("shell", "cat", "/sdcard/ew4-startup-review.xml")
        for node in ET.fromstring(xml).iter("node"):
            if node.get("package") == args.package and node.get("text") in ("继续启动", "重试"):
                x1, y1, x2, y2 = map(int, re.findall(r"\d+", node.get("bounds")))
                adb("shell", "input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2))
                break
        while time.monotonic() - start < 90:
            log = text("logcat", "-d", "--pid=" + pid, "-t", "2500")
            if "Startup: 启动完成" in log:
                break
            assert "FATAL EXCEPTION" not in log and "Startup: 游戏页面启动失败" not in log, "App startup failed"
            time.sleep(1)
        else:
            raise AssertionError("Installed app did not reach its menu within 90 seconds")
        (args.output / f"launch-{attempt + 1}.log").write_text(log, encoding="utf-8")
        (args.output / f"menu-{attempt + 1}.png").write_bytes(adb("exec-out", "screencap", "-p"))
        report["launches"].append(dict(attempt=attempt + 1, seconds=round(time.monotonic() - start, 2), pid=pid, ready=True))
finally:
    if len(report["launches"]) != args.attempts:
        report["errors"] = 1
    (args.output / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))
