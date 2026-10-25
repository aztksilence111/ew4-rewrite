"""Generate an explicit catalog for the new runtime from preserved original data.

Legacy IDs and three-grade interpretation are confined to this import boundary.
All XML attributes/children are preserved in source trees for unported semantics.
"""

import configparser
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from catalog_files import write_catalog

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "apk_work/original/assets"
XML_NAMES = ("def_army.xml", "def_commander.xml", "def_item.xml", "def_card.xml",
             "def_construction.xml", "def_installation.xml", "def_terraintype.xml", "def_conquestlist.xml")
CATEGORIES = ("infantry", "cavalry", "artillery", "naval", "fort")
WEAPONS = ("gun", "guns", "mgun", "cannon", "rocket", "cold")


def slug(name):
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def tree(element):
    return {"tag": element.tag, "attributes": dict(element.attrib),
            "text": (element.text or "").strip(), "children": [tree(c) for c in element]}


def build():
    roots = {name: ET.parse(ASSETS / name).getroot() for name in XML_NAMES}
    strings = configparser.ConfigParser(interpolation=None, strict=False, delimiters=("=",), allow_no_value=True)
    strings.optionxform = str
    strings.read_string("[strings]\n" + (ASSETS / "stringtable_cn.ini").read_text(encoding="utf-8-sig"))
    labels = strings["strings"]
    tables = json.loads((ROOT / "reverse/native/rules_inventory.json").read_text(encoding="utf-8"))["tables"]
    profiles, variants = [], {}
    for country in roots["def_army.xml"]:
        variant = country.attrib["name"]
        if variant in variants:
            raise ValueError("Duplicate country variant " + variant)
        variants[variant] = {"ref": country.get("ref"), "profiles": {}}
        for army in country.findall("army"):
            a = army.attrib
            grade, name = int(a["grade"]), slug(a["name"])
            if grade not in range(3):
                raise ValueError("Unsupported legacy grade")
            profile_id = f"ew4.unit.{variant}.{name}.{grade}"
            key = f"{name}.{grade}"
            if key in variants[variant]["profiles"]:
                raise ValueError("Duplicate unit profile " + profile_id)
            variants[variant]["profiles"][key] = profile_id
            traits = {"Rocket": ["no_retaliation", "line_splash", "ai.rocket"],
                      "Light Artillery": ["ai.direct_artillery"], "Heavy Artillery": ["ai.direct_artillery"],
                      "Siege Artillery": ["siege_gun", "ai.direct_artillery"], "Coastal Fort": ["coastal_gun"],
                      "Machine Gun": ["ai.counter_mounted"], "Armored Car": ["ai.counter_mounted"]}.get(a["name"], [])
            weapon = WEAPONS.index(a["weapon"])
            profiles.append({"id": profile_id, "category": "naval" if a["type"] == "warship" else a["type"], "grade": str(grade),
                             "max_health": int(a["strength"]), "movement": int(a["movement"]),
                             "minimum_attack": int(a["minatk"]), "maximum_attack": int(a["maxatk"]),
                             "minimum_range": int(a["minatkrange"]), "maximum_range": int(a["maxatkrange"]),
                             "food_consumption": int(a["consumption"]), "traits": traits,
                             "kill_experience": grade + 1,
                             "ignition_numerator": 1 if weapon == 3 else 5 if weapon == 4 else 0,
                             "ignition_denominator": 2 if weapon == 4 else 1,
                             "attack_bands": [{"strict_minimum_percent": t, "rolls": i + 2}
                                              for i, t in enumerate(tables["attack_count_health_thresholds"][:grade + 4])],
                             "damage_multipliers": dict(zip(CATEGORIES, tables["weapon_category_multipliers"][weapon * 5:weapon * 5 + 5]))})
    resolved = {}

    def resolve(variant, visiting):
        if variant in resolved:
            return resolved[variant]
        if variant in visiting or variant not in variants:
            raise ValueError("Invalid country variant reference " + variant)
        entry = variants[variant]
        base = resolve(entry["ref"], visiting | {variant}) if entry["ref"] else {}
        resolved[variant] = dict(base, **entry["profiles"])
        return resolved[variant]

    for variant in variants:
        resolve(variant, set())
    skills = [{"id": f"ew4.skill.{i}", "legacy_id": i, "names": {"zh_CN": labels[f"name_skill_{i + 1:02}"]},
               "legacy_description": labels.get(f"desc_skill_{i + 1:02}", "")} for i in range(40)]
    commanders = []
    for general in roots["def_commander.xml"]:
        attrs = general.attrib
        commanders.append({"id": "ew4.commander." + str(int(attrs["id"])), "legacy_attributes": dict(attrs),
                           "skills": [f"ew4.skill.{int(attrs[k])}" for k in ("skill1", "skill2", "skill3", "skill4") if int(attrs.get(k, -1)) >= 0]})
    sources = {name: hashlib.sha256((ASSETS / name).read_bytes()).hexdigest() for name in (*XML_NAMES, "stringtable_cn.ini")}
    return {"schema_version": 2, "origin": "Preserved original APK; no Republican China content or balance changes.",
            "source_sha256": sources, "profiles": profiles, "variants": resolved, "skills": skills, "commanders": commanders,
            "source_trees": {name: tree(root) for name, root in roots.items()}, "native_tables": tables}


def main():
    catalog = build()
    ids = [p["id"] for p in catalog["profiles"]]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate profile IDs")
    output = ROOT / "rewrite/data/legacy/catalog.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    write_catalog(output, catalog)
    print(json.dumps({"profiles": len(ids), "variants": len(catalog["variants"]), "skills": len(catalog["skills"]),
                      "commanders": len(catalog["commanders"]), "errors": 0}))


if __name__ == "__main__":
    main()
