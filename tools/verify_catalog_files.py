"""Check a split migration against its preserved catalog and the C# export."""
import argparse
import json
from pathlib import Path
from catalog_files import read_catalog, read_rules, write_catalog

parser = argparse.ArgumentParser()
parser.add_argument("original", type=Path)
parser.add_argument("compiled", type=Path)
parser.add_argument("--data", type=Path, default=Path(__file__).resolve().parents[1] / "rewrite/data")
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
before = json.loads(args.original.read_text(encoding="utf-8"))
python = read_catalog(args.data / "legacy/catalog.json")
compiled = json.loads(args.compiled.read_text(encoding="utf-8"))
assert python == compiled, "Python and C# section loaders disagree"
checks = 1
for old, new in zip(before["profiles"], compiled["profiles"], strict=True):
    assert old == {k: v for k, v in new.items() if k != "weapon"}, old["id"]
    checks += 1
for old, new in zip(before["commanders"], compiled["commanders"], strict=True):
    for c in (old, new):
        for i in range(1, 5):
            c["legacy_attributes"].pop(f"skill{i}", None)
    assert old == new, old["id"]
    checks += 1
for key in ("skills", "variants", "native_tables", "source_sha256"):
    assert before[key] == compiled[key], key
    checks += 1
for name in before["source_trees"]:
    if name == "def_commander.xml":
        continue
    assert before["source_trees"][name] == compiled["source_trees"][name], name
    checks += 1

roundtrip = args.output.parent / "python-roundtrip/legacy"
roundtrip.parent.mkdir(parents=True, exist_ok=True)
roundtrip.mkdir(exist_ok=True)
write_catalog(roundtrip / "catalog.json", python, read_rules(args.data / "legacy/runtime_rule_tables.json"),
              read_rules(args.data / "legacy/metagame_rule_tables.json"))
# Source-only army tree is resolved from the preserved original resources.
(roundtrip.parent / "original").mkdir(exist_ok=True)
(roundtrip.parent / "original/def_army.xml").write_bytes((args.data / "original/def_army.xml").read_bytes())
assert read_catalog(roundtrip / "catalog.json") == python, "Python importer roundtrip changed definitions"
checks += 1
report = dict(checks=checks, errors=0, units=len(python["profiles"]), commanders=len(python["commanders"]), skills=len(python["skills"]),
              scope="Original values retained; Python/C# loaders agree; Python writer roundtrip; XML skill slots normalized to ordered skill IDs")
args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8", newline="\n")
print(json.dumps(report))
