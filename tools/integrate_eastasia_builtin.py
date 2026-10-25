"""Promote the reviewed East Asia map into the shared built-in runtime catalog."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import xml.etree.ElementTree as ET
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "rewrite/data"
SOURCE = ROOT / "rewrite/content/eastasia"


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path, value, compact=False):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=None if compact else 2,
                               separators=(",", ":") if compact else None) + "\n", encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cities-only', action='store_true', help='Merge appended cities while retaining current map art and lettering')
    args = parser.parse_args()
    original = DATA / "original"
    protected = {name: hashlib.sha256((original / name).read_bytes()).hexdigest()
                 for name in ("europe.bin", "america.bin", "def_battlelist.xml")}
    maps = ET.parse(original / "def_map.xml")
    source_map = next(e for e in ET.parse(SOURCE / "original/def_map.xml").getroot() if e.get("name") == "eastasia")
    existing = [e for e in maps.getroot() if e.get("id") == "3" or e.get("name") == "eastasia"]
    for e in existing:
        assert e.get("id") == "3" and e.get("name") == "eastasia", "Map identity collision"
        maps.getroot().remove(e)
    maps.getroot().append(source_map)
    ET.indent(maps, space="  ")
    maps.write(original / "def_map.xml", encoding="utf-8", xml_declaration=True)
    cities = ET.parse(original / "def_areaname.xml")
    by_id = {e.get("id"): e for e in cities.getroot()}
    placements = read_json(ROOT / "maps/eastasia_rewrite/cities.json")
    for city in placements:
        key = str(city["city_id"])
        if key in by_id:
            assert by_id[key].get("name") == city["name"], "City ID collision"
        else:
            ET.SubElement(cities.getroot(), "areaname", id=key, name=city["name"])
    ET.indent(cities, space="  ")
    cities.write(original / "def_areaname.xml", encoding="utf-8", xml_declaration=True)
    for name in (("eastasia.bin",) if args.cities_only else ("eastasia.bin", "maptextpos_eastasia.xml")):
        shutil.copyfile(SOURCE / "original" / name, original / name)
    shutil.copyfile(SOURCE / "eastasia.map.json", original / "eastasia.map.json")
    manifest_path = DATA / "client/manifest.json"
    manifest, incoming = read_json(manifest_path), read_json(SOURCE / "client/manifest.json")
    if args.cities_only:
        current_map, updated_map = manifest['maps']['eastasia'], incoming['maps']['eastasia']
        assert all(updated_map['cityNames'].get(k) == v for k, v in current_map['cityNames'].items()), 'Existing city label changed'
        added = {int(k) for k in updated_map['cityNames'] if k not in current_map['cityNames']}
        current_map['cityNames'].update(updated_map['cityNames'])
        current_map['overlays'] = [o for o in current_map['overlays'] if o['cell'] not in added]
        textpos = ET.Element('Elements')
        for label in current_map['labels']:
            ET.SubElement(textpos, 'Element', **{key:str(label[key]) for key in
                ('index', 'libName', 'colorAlphaPercent', 'tx', 'ty', 'left', 'top', 'width', 'height')})
        ET.indent(textpos, space='  ')
        ET.ElementTree(textpos).write(original/'maptextpos_eastasia.xml', encoding='utf-8', xml_declaration=True)
    else:
        manifest["maps"]["eastasia"] = incoming["maps"]["eastasia"]
    for name in (() if args.cities_only else ("tiles.png", "tiles_hd.png")):
        atlas = Path(name).stem
        geography_sprites = {k: v for k, v in manifest["atlases"][atlas].items() if k.startswith("impassable_")}
        if geography_sprites:
            with Image.open(SOURCE / "client/images" / name) as a, Image.open(DATA / "client/images" / name) as b:
                assert a.convert("RGBA").tobytes() == b.crop((0, 0, a.width, a.height)).convert("RGBA").tobytes(), "Base atlas pixels changed; rebuild geography assets"
            assert all(manifest["atlases"][atlas][k] == v for k, v in incoming["atlases"][atlas].items()), "Base sprite coordinates changed"
            continue
        with Image.open(SOURCE / "client/images" / name) as a, Image.open(DATA / "client/images" / name) as b:
            assert a.width >= b.width and a.height >= b.height, "Shared atlas shrank"
            assert a.crop((0, 0, b.width, b.height)).convert("RGBA").tobytes() == b.convert("RGBA").tobytes(), "Shared atlas pixels changed"
        atlas = Path(name).stem
        for key, value in manifest["atlases"][atlas].items():
            assert incoming["atlases"][atlas][key] == value, "Shared sprite coordinates changed"
        manifest["atlases"][atlas] = incoming["atlases"][atlas]
        shutil.copyfile(SOURCE / "client/images" / name, DATA / "client/images" / name)
    if not args.cities_only:
        library_path = DATA / "client/animations/maptext.json"
        library, extended = read_json(library_path), read_json(SOURCE / "client/animations/maptext.json")
        for key in ("atlas", "fps", "pixelRatio"):
            assert library[key] == extended[key], "Original map-text assets changed"
        for i, item in enumerate(library["items"]):
            if not item["name"].startswith("eastasia_"):
                assert item == extended["items"][i], "Original map-text entry changed"
        for i, element in enumerate(library["elements"]):
            assert element == extended["elements"][i], "Original map-text transform changed"
        write_json(library_path, extended, compact=True)
        shutil.copytree(SOURCE / "client/maps/eastasia", DATA / "client/maps/eastasia", dirs_exist_ok=True)
    else:
        write_json(DATA/'client/maps/eastasia/cities.json', placements)
    write_json(manifest_path, manifest, compact=True)
    assert struct.unpack_from("<4s3i", (original / "eastasia.bin").read_bytes()) == (b"YSAE", 1, 158, 136)
    assert all(hashlib.sha256((original / name).read_bytes()).hexdigest() == digest for name, digest in protected.items())
    if (DATA / "rewrite/geography.json").exists():
        import build_impassable_geography
        build_impassable_geography.geography()
    inputs = [p for part in ("original", "legacy", "client", "rewrite") for p in sorted((DATA / part).rglob("*"))
              if p.is_file() and p.suffix != ".md"
              and (part != "legacy" or p.parent == DATA / "legacy")]
    index = [dict(path=p.relative_to(DATA).as_posix(), bytes=p.stat().st_size,
                  sha256=hashlib.sha256(p.read_bytes()).hexdigest())
             for p in inputs]
    write_json(DATA / "runtime-content.json", dict(schema=1, description="Shared built-in runtime data, including the reviewed East Asia map.", files=index))
    report = dict(maps=[e.get("name") for e in maps.getroot()], city_count=len(placements),
                  labels=len(manifest["maps"]["eastasia"]["labels"]), tiles=len(manifest["maps"]["eastasia"]["tiles"]),
                  protected=protected, runtime_files=len(index), errors=0, formal_scenarios_added=0)
    write_json(ROOT / "maps/eastasia_rewrite/builtin-integration.json", report)
    print(json.dumps(report))


if __name__ == "__main__":
    main()
