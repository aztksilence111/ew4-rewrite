"""Package the verified signed APK and optional standalone Web publish."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--build", default="build/release-0.2.4-20261020")
parser.add_argument("--version", default="0.2.4")
parser.add_argument("--date", default="20261020")
parser.add_argument("--notes", default="rewrite/RELEASE_0.2.4_20261020.md")
parser.add_argument("--rule-api", type=int, choices=(1, 2, 3), default=1)
parser.add_argument("--android-only", action="store_true")
args = parser.parse_args()
build = ROOT / args.build
assets = build / "assets"
assets.mkdir(exist_ok=True)
head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
assert not subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=no"], cwd=ROOT).strip(), "Commit tracked source before packaging"
web = build / "web"
if not args.android_only:
    assert (web / "Ew4.Web.exe").is_file() and (web / "MoonSharp.Interpreter.dll").is_file()
    assert all((web / name).is_file() for name in ("Start-Web.cmd", "Open-Web-When-Ready.cmd")), "Publish the Web launchers before packaging"
    assert not (web / "saves").exists(), "Do not package user profiles"
report = json.loads((build / "android-verification.json").read_text(encoding="utf-8"))
source_apk = ROOT / report["apk"]
assert hashlib.sha256(source_apk.read_bytes()).hexdigest() == report["sha256"]
maps = [node.attrib["file"].removesuffix(".bin") for node in ET.parse(ROOT / "rewrite/data/original/def_map.xml").getroot() if "file" in node.attrib]
info = dict(version=args.version, source=head, ruleApi=args.rule_api, builtinMaps=maps,
            formalScenarios=95, androidDeviceExecution=False, apkCertificate="Android development certificate")
if args.android_only:
    info["platforms"] = ["android-arm64"]
apk = assets / f"ew4-rewrite-{args.version}-{args.date}-arm64.apk"
shutil.copyfile(source_apk, apk)
packages = [apk]
if not args.android_only:
    (web / "release-info.json").write_text(json.dumps(info, indent=2) + "\n", encoding="utf-8", newline="\n")
    shutil.copyfile(ROOT / args.notes, web / "RELEASE_NOTES.md")
    archive = assets / f"ew4-rewrite-{args.version}-{args.date}-web-win-x64.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for file in sorted(web.rglob("*")):
            if file.is_file():
                output.write(file, file.relative_to(web).as_posix())
    with zipfile.ZipFile(archive) as output:
        assert output.testzip() is None
        assert json.loads(output.read("release-info.json"))["source"] == head
    packages.append(archive)
files = [dict(name=p.name, bytes=p.stat().st_size, sha256=hashlib.sha256(p.read_bytes()).hexdigest())
         for p in packages]
(assets / "SHA256SUMS.txt").write_text("".join(f"{p['sha256']}  {p['name']}\n" for p in files), encoding="ascii", newline="\n")
(assets / "release-manifest.json").write_text(json.dumps(dict(info, files=files), indent=2) + "\n", encoding="utf-8", newline="\n")
print(json.dumps(dict(source=head, files=files)))
