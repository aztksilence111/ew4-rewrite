"""Read/write the authoring sections through the same legacy in-memory shape as C#."""
import copy
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import hashlib

PARTS = ["units.json", "unit-variants.json", "commanders.json", "skills.json", "items.json",
         "recruitment.json", "buildings.json", "installations.json", "terrain-rules.json",
         "conquest-list.json", "combat-rules.json", "unit-progression.json", "commander-progression.json", "economy-rules.json"]
XML_PARTS = [("items.json", "items", "def_item.xml", "items", "item"),
             ("recruitment.json", "cards", "def_card.xml", "cards", "card"),
             ("buildings.json", "buildings", "def_construction.xml", "constructions", "construction"),
             ("installations.json", "installations", "def_installation.xml", "installations", "installation"),
             ("terrain-rules.json", "terrain", "def_terraintype.xml", "terraintypes", "terraintype"),
             ("conquest-list.json", "entries", "def_conquestlist.xml", "battlelist", "battle")]
PROGRESSION = ["commander_rank_xp", "commander_nobility_xp", "commander_rank_health_bonus", "commander_nobility_healing"]
WEAPONS = ["gun", "guns", "mgun", "cannon", "rocket", "cold"]
CATEGORIES = ["infantry", "cavalry", "artillery", "naval", "fort"]


def tree(element):
    return dict(tag=element.tag, attributes=dict(element.attrib), text=(element.text or "").strip(),
                children=[tree(child) for child in element])


def record_tree(tag, record):
    return dict(tag=tag, attributes={k: str(v) for k, v in record.items() if k not in ("tag", "children")}, text="",
                children=[record_tree(c["tag"], c) for c in record.get("children", [])])


def resolve_combat(unit, tables):
    if "attack_bands" not in unit:
        unit["attack_bands"] = [dict(strict_minimum_percent=value, rolls=i + 2)
                                for i, value in enumerate(tables["attack_count_health_thresholds"][:int(unit["grade"]) + 4])]
    if "damage_multipliers" not in unit:
        start = WEAPONS.index(unit["weapon"]) * 5
        unit["damage_multipliers"] = dict(zip(CATEGORIES, tables["weapon_category_multipliers"][start:start + 5]))


def read_rules(path):
    path = Path(path)
    data = json.loads(path.read_text(encoding="utf-8"))
    if data["schema_version"] == 1:
        return data
    if data["schema_version"] != 2:
        raise ValueError("Unsupported rule table index")
    rules = dict(schema_version=1, source_sha256=data["source_sha256"])
    for name in data["files"]:
        rules.update(json.loads((path.parent / name).read_text(encoding="utf-8"))["rules"])
    return rules


def read_catalog(path):
    path = Path(path)
    catalog = json.loads(path.read_text(encoding="utf-8"))
    if catalog["schema_version"] == 2:
        return catalog
    if catalog["schema_version"] != 3 or catalog["files"] != PARTS:
        raise ValueError("Unsupported catalog index")
    parts = {name: json.loads((path.parent / name).read_text(encoding="utf-8")) for name in PARTS}
    if any(part["schema"] != 1 for part in parts.values()):
        raise ValueError("Unsupported catalog section")
    catalog.pop("files")
    catalog.update(schema_version=2, profiles=parts["units.json"]["units"], variants=parts["unit-variants.json"]["variants"],
                   skills=parts["skills.json"]["skills"], commanders=[])
    trees = {}
    for filename, key, source, root, tag in XML_PARTS:
        trees[source] = dict(tag=root, attributes={}, text="", children=[record_tree(tag, r) for r in parts[filename][key]])
    for commander in parts["commanders.json"]["commanders"]:
        attrs = {k: str(v) for k, v in commander.items() if k not in ("id", "legacy_id", "skills", "skill_slots")}
        attrs["id"] = str(commander["legacy_id"])
        for i in range(4):
            skill = commander["skills"][i] if i < len(commander["skills"]) else ""
            attrs[f"skill{i + 1}"] = skill[10:] if skill.startswith("ew4.skill.") else "-1"
        entry = dict(id=commander["id"], legacy_attributes=attrs, skills=commander["skills"])
        if "skill_slots" in commander:
            entry["skill_slots"] = commander["skill_slots"]
        catalog["commanders"].append(entry)
    trees["def_commander.xml"] = dict(tag="commanders", attributes={}, text="", children=[
        dict(tag="commander", attributes=c["legacy_attributes"], text="", children=[]) for c in catalog["commanders"]])
    trees["def_army.xml"] = tree(ET.parse(path.parent.parent / "original/def_army.xml").getroot())
    tables = parts["combat-rules.json"]["tables"]
    progression = read_rules(path.parent / "runtime_rule_tables.json")
    tables.update({k: progression[k] for k in PROGRESSION})
    catalog.update(source_trees=trees, native_tables=tables)
    for unit in catalog["profiles"]:
        resolve_combat(unit, tables)
    return catalog


def scalar(value):
    return int(value) if re.fullmatch(r"-?\d+", value) else value


def tree_record(node, include_tag=False):
    result = {k: scalar(v) for k, v in node["attributes"].items()}
    if include_tag:
        result["tag"] = node["tag"]
    if node["children"]:
        result["children"] = [tree_record(c, True) for c in node["children"]]
    return result


def write_catalog(path, catalog, runtime_rules=None, metagame_rules=None):
    """Regenerate all sections when importing original resources; never update hashes alone."""
    path = Path(path)
    runtime_rules = runtime_rules or read_rules(path.parent / "runtime_rule_tables.json")
    metagame_rules = metagame_rules or read_rules(path.parent / "metagame_rule_tables.json")
    tables = copy.deepcopy(catalog["native_tables"])
    for key in PROGRESSION:
        tables.pop(key)
    units = copy.deepcopy(catalog["profiles"])
    weapons = {}
    for country in catalog["source_trees"]["def_army.xml"]["children"]:
        for unit in country["children"]:
            a = unit["attributes"]
            slug = re.sub("[^a-z0-9]+", "_", a["name"].lower()).strip("_")
            weapons[f"ew4.unit.{country['attributes']['name']}.{slug}.{a['grade']}"] = a["weapon"]
    for unit in units:
        if unit["id"] not in weapons:
            continue
        unit.setdefault("weapon", weapons[unit["id"]])
        generated = {k: v for k, v in unit.items() if k != "damage_multipliers"}
        if str(unit["grade"]) in ("0", "1", "2"):
            generated.pop("attack_bands", None)
        resolve_combat(generated, tables)
        for key in ("attack_bands", "damage_multipliers"):
            if unit.get(key) == generated[key]:
                unit.pop(key, None)
    commanders = []
    for c in catalog["commanders"]:
        row = dict(id=c["id"], legacy_id=int(c["legacy_attributes"]["id"]))
        row.update({k: scalar(v) for k, v in c["legacy_attributes"].items() if k not in ("id", "skill1", "skill2", "skill3", "skill4")})
        row["skills"] = c["skills"]
        if "skill_slots" in c:
            row["skill_slots"] = c["skill_slots"]
        commanders.append(row)
    parts = {"units.json": dict(units=units), "unit-variants.json": dict(variants=catalog["variants"]),
             "commanders.json": dict(commanders=commanders), "skills.json": dict(skills=catalog["skills"]),
             "combat-rules.json": dict(tables=tables),
             "unit-progression.json": dict(rules={k: v for k, v in runtime_rules.items() if k not in PROGRESSION + ["schema_version", "source_sha256"]}),
             "commander-progression.json": dict(rules={k: runtime_rules[k] for k in PROGRESSION}),
             "economy-rules.json": dict(rules={k: v for k, v in metagame_rules.items() if k not in ("schema_version", "source_sha256")})}
    for filename, key, source, _, _ in XML_PARTS:
        parts[filename] = {key: [tree_record(c) for c in catalog["source_trees"][source]["children"]]}
    path.parent.mkdir(parents=True, exist_ok=True)
    for name, value in parts.items():
        (path.parent / name).write_text(json.dumps(dict(schema=1, **value), ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    index = {k: catalog[k] for k in ("schema_version", "origin", "source_sha256")}
    index.update(schema_version=3, files=PARTS)
    path.write_text(json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    for name, data, names in [("runtime_rule_tables.json", runtime_rules, ["unit-progression.json", "commander-progression.json"]),
                               ("metagame_rule_tables.json", metagame_rules, ["economy-rules.json"])]:
        (path.parent / name).write_text(json.dumps(dict(schema_version=2, source_sha256=data["source_sha256"], files=names), indent=2) + "\n", encoding="utf-8", newline="\n")


def refresh_runtime_index(data):
    data = Path(data)
    path = data / "runtime-content.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    records = {entry["path"]: entry for entry in manifest["files"] if not entry["path"].startswith("legacy/")}
    for file in sorted((data / "legacy").glob("*.json")):
        raw = file.read_bytes()
        name = file.relative_to(data).as_posix()
        records[name] = dict(path=name, bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
    manifest["description"] = "Self-contained runtime data with independently editable rule sections."
    ordered = [entry["path"] for entry in manifest["files"] if entry["path"] in records]
    ordered.extend(sorted(set(records) - set(ordered)))
    manifest["files"] = [records[key] for key in ordered]
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return dict(records=len(records), legacy_files=sum(k.startswith("legacy/") for k in records), errors=0)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh-index", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(refresh_runtime_index(args.refresh_index)))
