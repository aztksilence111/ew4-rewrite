"""Import the runtime data closure without changing original content bytes."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "rewrite/data"
SOURCE = ROOT / "apk_work/original/assets"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    catalog = json.loads((DATA / "legacy/catalog.json").read_text(encoding="utf-8"))
    code = (ROOT / "rewrite/Ew4.Client/ClientContent.cs").read_text(encoding="utf-8")
    names = set(catalog["source_sha256"]) | set(re.findall(r'Text\("([^"/]+)"\)', code))
    names.update(e.attrib["file"] for e in ET.parse(SOURCE / "def_map.xml").getroot())
    names.update(p.name for p in SOURCE.glob("*.btl"))
    imports = [(SOURCE / name, DATA / "original" / name) for name in sorted(names)]
    imports.extend((ROOT / "reverse/reports" / name, DATA / "legacy" / name)
                   for name in ("runtime_rule_tables.json", "metagame_rule_tables.json")
                   if not (DATA / "legacy" / name).exists() or json.loads((DATA / "legacy" / name).read_text())["schema_version"] == 1)
    # A previously edited destination must never be overwritten by a source refresh.
    for source, target in imports:
        if not source.is_file():
            raise FileNotFoundError(source)
        if target.exists() and digest(source) != digest(target):
            raise RuntimeError(f"Destination differs; preserved without overwrite: {target}")
    copied = 0
    for source, target in imports:
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists():
            shutil.copyfile(source, target)
            copied += 1
    files = [dict(path=p.relative_to(DATA).as_posix(), bytes=p.stat().st_size, sha256=digest(p))
             for p in sorted(set([target for _, target in imports] + list((DATA / "legacy").glob("*.json"))))]
    manifest = dict(schema=1, description="Self-contained rewrite runtime data; byte-identical imported source content.", files=files)
    (DATA / "runtime-content.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(files=len(files), copied=copied, bytes=sum(f["bytes"] for f in files), errors=0)))


if __name__ == "__main__":
    main()
