"""Audit original unit appearance independently from inherited combat profiles."""
import argparse
from collections import Counter
import hashlib
import json
from catalog_files import read_catalog
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

from ew4_formats import BattleFile

ROOT = Path(__file__).resolve().parents[1]
CLIENT = ROOT / "rewrite/data/client"
SOURCE = ROOT / "apk_work/original/assets"
REPORT = ROOT / "rewrite/reports/unit-visuals-20261021"


def slug(name):
    return name.lower().replace(" ", "_")


def legacy_config(motions, kinds):
    result = dict(schema=1, factions={}, profiles={}, variants={}, defaults={}, definitions={})
    for key in motions:
        for kind in sorted(kinds, key=len, reverse=True):
            if key == kind:
                for grade in range(3):
                    result["defaults"].setdefault(f"{slug(kind)}.{grade}", key)
                break
            if not key.startswith(kind + " "):
                continue
            tail = key[len(kind) + 1:].split()
            if not tail[-1].isdigit():
                continue
            grade = int(tail[-1]) - 1
            slot = f"{slug(kind)}.{grade}"
            if len(tail) == 1:
                result["defaults"][slot] = key
            elif len(tail) == 2:
                result["variants"].setdefault(tail[0], {})[slot] = key
            break
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--write-config", action="store_true")
    args = parser.parse_args()
    REPORT.mkdir(parents=True, exist_ok=True)
    manifest = json.loads((CLIENT / "manifest.json").read_text(encoding="utf-8"))
    effects = json.loads((CLIENT / "effects.json").read_text(encoding="utf-8"))
    catalog = read_catalog(ROOT / "rewrite/data/legacy/catalog.json")
    source_motions = {e.attrib["name"]: dict(e.attrib, motions=[m.attrib for m in e])
                      for e in ET.parse(SOURCE / "def_motion.xml").getroot()}
    armies = ET.parse(SOURCE / "def_army.xml").getroot()
    kinds = list(dict.fromkeys(a.attrib["name"] for c in armies for a in c))
    assert effects["motions"] == source_motions, "Runtime motion bank differs from source"
    config = legacy_config(source_motions, kinds)
    if args.write_config:
        config_path = CLIENT / "unit-visuals.json"
        config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        index_path = ROOT / "rewrite/data/runtime-content.json"
        index = json.loads(index_path.read_text(encoding="utf-8"))
        entries = {entry["path"]: entry for entry in index["files"]}
        raw = config_path.read_bytes()
        entries["client/unit-visuals.json"] = dict(path="client/unit-visuals.json", bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
        index["files"] = list(entries.values())
        index_path.write_text(json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    variants = set(catalog["variants"]) | set(config["variants"])
    cases, affected, missing = [], Counter(), []
    for variant in sorted(variants):
        for slot, profile_id in catalog["variants"].get(variant, catalog["variants"]["others"]).items():
            profile = manifest["profiles"][profile_id]
            kind, grade = profile["kind"], profile["grade"]
            expected = next((k for k in (f"{kind} {variant} {grade + 1}", f"{kind} {grade + 1}", kind) if k in source_motions), None)
            if expected is None:
                missing.append(dict(variant=variant, slot=slot))
                continue
            old = effects["bindings"][profile_id]
            if old != expected:
                affected[variant] += 1
            cases.append(dict(variant=variant, slot=slot, profile=profile_id, expected=expected, previous=old))
    libraries = {}
    for library in sorted({m["res"] for m in source_motions.values()}):
        runtime = json.loads((CLIENT / "animations" / (library + ".json")).read_text(encoding="utf-8"))
        imported = json.loads((ROOT / "rewrite/data/legacy/animations" / (library + ".json")).read_text(encoding="utf-8"))
        assert runtime["items"] == imported["items"] and runtime["elements"] == imported["elements"], library
        for texture in runtime["atlas"]["textures"]:
            name = texture["name"]
            assert (CLIENT / "images" / name).read_bytes() == (SOURCE / name).read_bytes(), name
        libraries[library] = {item["name"] for item in runtime["items"]}
    for name, motion in source_motions.items():
        for action in motion["motions"]:
            if action["name"] not in libraries[motion["res"]]:
                missing.append(dict(motion=name, action=action))
    scenarios, live_affected = [], Counter()
    maps = {int(m.attrib["id"]): struct.unpack_from("<i", (ROOT / "rewrite/data/original" / m.attrib["file"]).read_bytes(), 8)[0]
            for m in ET.parse(ROOT / "rewrite/data/original/def_map.xml").getroot()}
    lookup = {(c["variant"], c["profile"]): c for c in cases}
    for path in sorted((ROOT / "rewrite/data/original").glob("*.btl")):
        battle = BattleFile.from_bytes(path.read_bytes())
        factions = []
        for offset in range(0, len(battle.sections["legions"]), 180):
            raw = battle.sections["legions"][offset:offset + 180]
            text = lambda p: raw[p:p + 16].split(b"\0", 1)[0].decode("ascii")
            factions.append(dict(country=text(24), variant=text(40)))
        for offset in range(0, len(battle.sections["armies"]), 32):
            raw = battle.sections["armies"][offset:offset + 32]
            cell = struct.unpack_from("<h", raw)[0]
            # BTL ownership is local to the scenario rectangle; army cells are map-absolute.
            map_width = maps[battle.header[1]]
            x, y = cell % map_width - battle.header[2], cell // map_width - battle.header[3]
            if not (0 <= x < battle.header[4] and 0 <= y < battle.header[5]):
                continue
            owner = battle.sections["ownership"][y * battle.header[4] + x]
            if owner >= len(factions):
                continue
            faction = factions[owner]
            unit, grade = struct.unpack_from("<2b", raw, 2)
            flat = unit * 3 + grade
            if flat < 0 or flat // 3 >= len(kinds):
                continue
            slot = f"{slug(kinds[flat // 3])}.{flat % 3}"
            variant = faction["variant"]
            profile = catalog["variants"].get(variant, catalog["variants"]["others"]).get(slot)
            case = lookup.get((variant, profile))
            if case and case["expected"] != case["previous"]:
                live_affected[variant] += 1
                scenarios.append(dict(scenario=path.stem, cell=cell, country=faction["country"], **case))
    report = dict(profiles=len(manifest["profiles"]), motionDefinitions=len(source_motions), libraries=len(libraries),
                  cases=len(cases), affectedBindings=dict(affected), affectedInitialUnits=dict(live_affected),
                  missingReferences=missing, errors=len(missing),
                  sourceMotionSha256=hashlib.sha256((SOURCE / "def_motion.xml").read_bytes()).hexdigest())
    (REPORT / "source-audit.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (REPORT / "cases.json").write_text(json.dumps(dict(cases=cases, initialUnits=scenarios), ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))
    assert not missing, "Missing original motion references"


if __name__ == "__main__":
    main()
