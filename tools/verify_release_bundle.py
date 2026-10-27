"""Verify release APK assets, ELF alignment, signing continuity and Web ZIP bytes."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(__doc__)
parser.add_argument("build", type=Path)
parser.add_argument("apk", type=Path)
parser.add_argument("--sdk", type=Path, required=True)
parser.add_argument("--previous-signature", type=Path, required=True)
args = parser.parse_args()
build, apk = root / args.build, root / args.apk
tools = sorted((args.sdk / "build-tools").iterdir())[-1]
checks = 0


def check(condition, label):
    global checks
    assert condition, label
    checks += 1


signature = subprocess.check_output([str(tools / "apksigner.bat"), "verify", "--verbose", "--print-certs", str(apk)], text=True)
signer = next(line for line in signature.splitlines() if "certificate SHA-256 digest:" in line)
check(signer in (root / args.previous_signature).read_text(encoding="utf-8"), "Signing certificate changed")
subprocess.run([str(tools / "zipalign.exe"), "-c", "-P", "16", "4", str(apk)], check=True)
project = ET.parse(root / "rewrite/Ew4.Android/Ew4.Android.csproj").getroot()
badging = subprocess.check_output([str(tools / "aapt2.exe"), "dump", "badging", str(apk)], text=True, encoding="utf-8")
check("name='org.ew4.reconstruction'" in badging and f"versionCode='{project.findtext('.//ApplicationVersion')}'" in badging
      and f"versionName='{project.findtext('.//ApplicationDisplayVersion')}'" in badging, "Package identity/version")
native = 0
with zipfile.ZipFile(apk) as archive:
    for source in (root / "rewrite/Ew4.Web/wwwroot").rglob("*"):
        if source.is_file():
            name = "assets/www/" + source.relative_to(root / "rewrite/Ew4.Web/wwwroot").as_posix()
            check(archive.read(name) == source.read_bytes(), "APK frontend: " + name)
    for name in archive.namelist():
        if name.startswith("lib/") and name.endswith(".so"):
            check(name.startswith("lib/arm64-v8a/") and "/libaot-" not in name, "Unexpected ABI or AOT: " + name)
            data = archive.read(name)
            header = struct.unpack_from("<16sHHIQQQIHHHHHH", data)
            check(header[0][:6] == b"\x7fELF\x02\x01" and header[2] == 183, "Expected ARM64 ELF: " + name)
            segments = [struct.unpack_from("<IIQQQQQQ", data, header[5] + i * header[9]) for i in range(header[10])]
            check(all(segment[7] >= 16384 for segment in segments if segment[0] == 1), "ELF 16 KiB alignment: " + name)
            native += 1
zip_checks = 0
for package in (build / "assets").glob("*-web-win-x64.zip"):
    with zipfile.ZipFile(package) as archive:
        check(archive.testzip() is None, "Web ZIP CRC")
        expected = {file.relative_to(build / "web").as_posix(): file for file in (build / "web").rglob("*") if file.is_file()}
        check(set(archive.namelist()) == set(expected), "Web ZIP inventory")
        for name, source in expected.items():
            check(archive.read(name) == source.read_bytes(), "Web ZIP file: " + name)
            zip_checks += 1
report = dict(checks=checks, errors=0, nativeLibraries=native, zipFiles=zip_checks, signer=signer,
              apkSha256=hashlib.sha256(apk.read_bytes()).hexdigest(), androidDeviceExecution=False)
(build / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
(build / "signature.txt").write_text(signature, encoding="utf-8")
print(json.dumps(report))
