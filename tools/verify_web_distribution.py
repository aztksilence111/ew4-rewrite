"""Verify the standalone distribution against current runtime and frontend bytes."""

import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("directory")
args = parser.parse_args()
package = ROOT / args.directory
checks = 0
for source_root, destination in ((ROOT / "rewrite/Ew4.Web/wwwroot", package / "wwwroot"),
                                 (ROOT / "rewrite/data/client", package / "data/client"),
                                 (ROOT / "rewrite/data/original", package / "data/original"),
                                 (ROOT / "rewrite/data/rewrite", package / "data/rewrite")):
    for source in source_root.rglob("*"):
        if not source.is_file() or source.name == "README.md":
            continue
        target = destination / source.relative_to(source_root)
        assert target.read_bytes() == source.read_bytes(), str(target) + " differs from source"
        checks += 1
for source in (ROOT / "rewrite/data/legacy").glob("*.json"):
    assert (package / "data/legacy" / source.name).read_bytes() == source.read_bytes()
    checks += 1
for entry in json.loads((ROOT / "rewrite/data/runtime-content.json").read_text())["files"]:
    file = package / "data" / entry["path"]
    assert file.stat().st_size == entry["bytes"] and hashlib.sha256(file.read_bytes()).hexdigest() == entry["sha256"]
    checks += 1
for name in ("Start-Web.cmd", "Open-Web-When-Ready.cmd"):
    assert (package / name).read_bytes() == (ROOT / "rewrite/Ew4.Web/launcher" / name).read_bytes()
    checks += 1
assert not (package / "saves").exists(), "Never distribute player saves"
assert not (package / "data/client/fonts/LXGWWenKai-Regular.ttf").exists(), "Unused font must not be duplicated"
assert all((package / name).is_file() for name in ("Ew4.Web.exe", "MoonSharp.Interpreter.dll", "README.md", "THIRD_PARTY_NOTICES.md"))
report = dict(checks=checks, errors=0, directory=str(package.relative_to(ROOT)), playerSaves=False,
              scope="Exact frontend/runtime/font bytes, runtime index hashes, launchers and standalone dependencies")
(package.parent / "web-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))
