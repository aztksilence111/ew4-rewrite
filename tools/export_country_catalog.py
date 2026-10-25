"""Create the global country registry from the preserved scenario identities."""
import hashlib
import json
from catalog_files import read_catalog
from pathlib import Path
import xml.etree.ElementTree as ET

from ew4_formats import BattleFile

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "rewrite/data"


def main():
    variants = {}
    for path in sorted((DATA / "original").glob("*.btl")):
        raw = BattleFile.from_bytes(path.read_bytes()).sections["legions"]
        for offset in range(0, len(raw), 180):
            text = lambda p: raw[offset + p:offset + p + 16].split(b"\0", 1)[0].decode("utf-8")
            country, variant = text(24), text(40)
            if country in variants and variants[country] != variant:
                raise ValueError("Conflicting original country variant: " + country)
            variants[country] = variant
    for country in ET.parse(DATA / "original/def_army.xml").getroot():
        if country.attrib["name"] != "others":
            variants.setdefault(country.attrib["name"], country.attrib["name"])
    strings = {}
    for line in (DATA / "original/stringtable_cn.ini").read_text(encoding="utf-8-sig").splitlines():
        if "=" in line and not line.lstrip().startswith(";"):
            key, value = line.split("=", 1)
            strings[key.strip()] = value.strip()
    runtime = read_catalog(DATA / "legacy/catalog.json")
    countries = {country: dict(name=strings.get(country, strings.get("name_" + country, country)), flag=variant,
                              unitVariant=variant if variant in runtime["variants"] else "others", visualVariant=variant)
                 for country, variant in sorted(variants.items())}
    path = DATA / "client/countries.json"
    sources = {name: hashlib.sha256((DATA / "original" / name).read_bytes()).hexdigest() for name in ("def_army.xml", "stringtable_cn.ini")}
    path.write_text(json.dumps(dict(schema=1, countries=countries, aliases={}, sourceSha256=sources), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    index_path = DATA / "runtime-content.json"
    index = json.loads(index_path.read_text(encoding="utf-8"))
    entries = {entry["path"]: entry for entry in index["files"]}
    raw = path.read_bytes()
    entries["client/countries.json"] = dict(path="client/countries.json", bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
    index["files"] = list(entries.values())
    index_path.write_text(json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(dict(countries=len(countries), errors=0)))


if __name__ == "__main__":
    main()
