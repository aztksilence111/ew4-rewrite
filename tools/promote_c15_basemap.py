"""Merge reviewed C15 geography into the neutral built-in East Asia map."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "rewrite/data"


def read(path):
    return json.loads(path.read_bytes())


def digest(data):
    return hashlib.sha256(data).hexdigest()


def write(path, value, compact=False):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=None if compact else 2,
        separators=(",", ":") if compact else None) + "\n", encoding="utf-8",
        newline="\r\n" if path.name == "geography.json" else "\n")


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    protected = {p: digest(p.read_bytes()) for p in (DATA / "original").glob("*.btl")}
    for name in ("original/def_battlelist.xml", "original/stringtable_cn.ini", "legacy/catalog.json",
                 "client/countries.json", "rewrite/extension.json"):
        p = DATA / name
        protected[p] = digest(p.read_bytes())
    terrain = (DATA / "original/eastasia.bin").read_bytes()
    document = read(DATA / "original/eastasia.map.json")
    visual = read(DATA / "client/manifest.json")
    original_visual = copy.deepcopy(visual)
    cities = read(DATA / "client/maps/eastasia/cities.json")
    names = ET.parse(DATA / "original/def_areaname.xml")
    geography = read(DATA / "rewrite/geography.json")
    with zipfile.ZipFile(args.source) as source:
        incoming = source.read("original/eastasia.bin")
        incoming_map = json.loads(source.read("client/manifest.json"))["maps"]["eastasia"]
        incoming_names = ET.fromstring(source.read("original/def_areaname.xml"))
    assert len(incoming) == len(terrain) and incoming[:16] == terrain[:16]
    changed_cells = [i for i in range(len(document["cells"]))
        if terrain[16 + i * 12:28 + i * 12] != incoming[16 + i * 12:28 + i * 12]]
    for cell in changed_cells:
        offset = 16 + cell * 12
        assert terrain[offset] == 1 and incoming[offset:offset + 2] == b"\0\0"
        assert terrain[offset + 2:offset + 12] == incoming[offset + 2:offset + 12]
        site = document["cells"][cell]
        assert site["buildingKind"] is None and site["owner"] is None
        site["terrain"].update(sea=False, terrainCost=3)
        site.update(fireTerrain="open", defenseTerrain="land")
    map_visual = visual["maps"]["eastasia"]
    labels = map_visual["cityNames"]
    # Keep scenario ownership and JURI entirely outside the base map.
    added = [(14084, 30001, 85.324, 27.717), (14726, 30002, 89.64, 27.47),
             (14890, 30003, 91.86, 27.59), (8855, 30004, 74.65, 36.32),
             (4938, 30005, 88.13, 47.85)]
    for cell, city_id, lon, lat in added:
        label = incoming_map["cityNames"][str(cell)]
        name = next(n.get("name") for n in incoming_names if n.get("id") == str(city_id))
        assert name == label["zh"]
        site = document["cells"][cell]
        assert site["owner"] is None and site["terrain"]["sea"] is False
        assert site["buildingKind"] is None or site["name"] == name
        site.update(name=name, buildingKind="city", buildingLevel=1)
        labels[str(cell)] = label
        if not any(c["city_id"] == city_id for c in cities):
            cities.append(dict(name=name, lon=lon, lat=lat, tier=1, city_id=city_id,
                building_level=1, building_style=2, cell=cell, col=cell % document["width"],
                row=cell // document["width"], world_pixel=[cell % document["width"] * 64
                + cell // document["width"] % 2 * 32, cell // document["width"] * 54 - 18]))
        existing = next((n for n in names.getroot() if n.get("id") == str(city_id)), None)
        if existing is None:
            ET.SubElement(names.getroot(), "areaname", id=str(city_id), name=name)
        else:
            assert existing.get("name") == name
    moved = next(c for c in cities if c["city_id"] == 10250)
    old_cell, new_cell = 5087, 5088
    if moved["cell"] == old_cell:
        origin, target = document["cells"][old_cell], document["cells"][new_cell]
        assert target["buildingKind"] is None and origin["name"] == incoming_map["cityNames"][str(new_cell)]["zh"]
        for key in ("name", "buildingKind", "buildingLevel"):
            target[key] = origin[key]
        origin.update(name=None, buildingKind=None, buildingLevel=0)
        moved.update(cell=new_cell, col=new_cell % document["width"], row=new_cell // document["width"],
                     world_pixel=[2048, 1710])
        moved.pop("displacement_km", None)
        labels.pop(str(old_cell))
    assert moved["cell"] == new_cell
    labels[str(new_cell)] = incoming_map["cityNames"][str(new_cell)]
    expected_visual = copy.deepcopy(original_visual)
    expected_visual["maps"]["eastasia"]["cityNames"] = labels
    assert visual == expected_visual
    assert not any(c["owner"] is not None or c["objective"] for c in document["cells"])
    assert not any(k in map_visual for k in ("territoryDisplay", "territoryEdgeExtension"))
    next(g for g in geography["maps"] if g["map"] == "eastasia")["baseTerrainSha256"] = digest(incoming)
    (DATA / "original/eastasia.bin").write_bytes(incoming)
    write(DATA / "original/eastasia.map.json", document, compact=True)
    write(DATA / "client/manifest.json", visual, compact=True)
    write(DATA / "client/maps/eastasia/cities.json", cities)
    ET.indent(names, space="  ")
    names.write(DATA / "original/def_areaname.xml", encoding="utf-8", xml_declaration=True)
    write(DATA / "rewrite/geography.json", geography)
    changed = {"original/eastasia.bin", "original/eastasia.map.json", "client/manifest.json",
               "client/maps/eastasia/cities.json", "original/def_areaname.xml", "rewrite/geography.json"}
    index = read(DATA / "runtime-content.json")
    for entry in index["files"]:
        if entry["path"] in changed:
            content = (DATA / entry["path"]).read_bytes()
            entry.update(bytes=len(content), sha256=digest(content))
    write(DATA / "runtime-content.json", index)
    assert all(digest(p.read_bytes()) == value for p, value in protected.items())
    report = dict(source=str(args.source), sourceSha256=digest(args.source.read_bytes()),
        islandCells=changed_cells, addedCityCells=[c[0] for c in added], movedCity=[old_cell, new_cell],
        protectedFiles=len(protected), scenariosChanged=0, factionDataChanged=0, jurisdictionDataImported=False,
        cities=len(cities), errors=0)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    write(args.report, report)
    print(json.dumps(report))


if __name__ == "__main__":
    main()
