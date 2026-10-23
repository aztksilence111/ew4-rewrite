"""Read-only inventory of original campaign bridges and frontend resources."""
import hashlib
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "rewrite/data"
OUT = ROOT / "build/campaign-bridges-20261024"


def neighbor(cell, direction, width, height):
    y, x = divmod(cell, width)
    dx, dy = ((1, 0), (y % 2, 1), (y % 2 - 1, 1), (-1, 0), (y % 2 - 1, -1), (y % 2, -1))[direction]
    x, y = x + dx, y + dy
    return y * width + x if 0 <= x < width and 0 <= y < height else -1


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    names = dict(line.split("=", 1) for line in (DATA / "original/stringtable_cn.ini").read_text(encoding="utf-8-sig").splitlines() if "=" in line)
    listed = {}
    for entry in ET.parse(DATA / "original/def_battlelist.xml").getroot():
        for field in ("file", "file2"):
            if field in entry.attrib:
                listed[entry.attrib[field]] = dict(name=names.get("name_" + entry.attrib["name"], entry.attrib["name"]), hidden=entry.attrib.get("hide") == "1", branch=field == "file2")
    maps = {}
    for entry in ET.parse(DATA / "original/def_map.xml").getroot():
        raw = (DATA / "original" / entry.attrib["file"]).read_bytes()
        maps[int(entry.attrib["id"])] = (struct.unpack_from("<2i", raw, 8), raw, entry.attrib["name"])
    manifest = json.loads((DATA / "client/manifest.json").read_text(encoding="utf-8"))
    cases = []
    for path in sorted((DATA / "original").glob("campaign*.btl")):
        raw = path.read_bytes()
        h = struct.unpack_from("<18i", raw)
        (width, height), terrain, map_name = maps[h[1]]
        offset = 72 + h[6] * 180 + h[4] * h[5]
        sites = {}
        for i in range(h[7]):
            site = raw[offset + i * 16:offset + (i + 1) * 16]
            sites[struct.unpack_from("<h", site)[0]] = site
        def active(cell):
            y, x = divmod(cell, width)
            return 0 <= cell < width * height and h[2] <= x < h[2] + h[4] and h[3] <= y < h[3] + h[5]
        bridges, edges, anomalies = [], set(), []
        for cell, site in sites.items():
            if not any(site[12:15]):
                continue
            directions = []
            for style, mask in enumerate(site[12:15]):
                for direction in range(6):
                    if not mask & (1 << direction):
                        continue
                    other = neighbor(cell, direction, width, height)
                    reciprocal = other in sites and bool(sites[other][12 + style] & (1 << ((direction + 3) % 6)))
                    river = bool(terrain[16 + cell * 12 + 8] & (1 << direction))
                    directions.append(dict(style=style + 1, direction=direction, other=other, reciprocal=reciprocal, river=river, active=active(cell)))
                    if active(cell):
                        edges.add((min(cell, other), max(cell, other)))
                    if not reciprocal or not river or not active(cell) or not active(other):
                        anomalies.append(dict(cell=cell, other=other, reciprocal=reciprocal, river=river, active=active(cell), otherActive=active(other)))
            bridges.append(dict(cell=cell, styles=list(site[12:15]), active=active(cell), directions=directions))
        original = ROOT / "apk_work/original/assets" / path.name
        cases.append(dict(id=path.stem, chapter=int(path.stem[8]), **listed.get(path.name, dict(name="未列入战役菜单", hidden=False, branch=False)),
            listed=path.name in listed, map=map_name, width=width, activeBridgeSites=sum(b["active"] for b in bridges),
            uniqueBridgeEdges=len(edges), bridgeSites=bridges, anomalies=anomalies,
            matchesOriginal=original.exists() and hashlib.sha256(original.read_bytes()).digest() == hashlib.sha256(raw).digest()))
    summary = dict(campaignFiles=len(cases), listedFiles=sum(c["listed"] for c in cases), affectedFiles=sum(c["uniqueBridgeEdges"] > 0 for c in cases),
        affectedListedFiles=sum(c["listed"] and c["uniqueBridgeEdges"] > 0 for c in cases),
        uniqueBridgeEdges=sum(c["uniqueBridgeEdges"] for c in cases), originalMismatches=sum(not c["matchesOriginal"] for c in cases),
        bridgeSprites={atlas:len([n for n in manifest["atlases"][atlas] if n.startswith("bridge_")]) for atlas in ("buildings", "buildings_hd")},
        chapters=[dict(chapter=i, files=sum(c["chapter"] == i for c in cases), affected=sum(c["chapter"] == i and c["uniqueBridgeEdges"] > 0 for c in cases),
            bridges=sum(c["uniqueBridgeEdges"] for c in cases if c["chapter"] == i)) for i in range(1,7)])
    (OUT / "inventory.json").write_text(json.dumps(dict(summary=summary, campaigns=cases), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary))
    first = next(c for c in cases if c["id"] == "campaign2_01")
    print(json.dumps({k:first[k] for k in ("id", "name", "activeBridgeSites", "uniqueBridgeEdges", "anomalies", "matchesOriginal")}))
    lines = ["# 全战役桥梁清单", "", "方向记录两端去重为一座；同一座桥在不同剧本中分别计数。", "",
             "| 剧本 | 名称 | 桥梁数 | 范围 |", "| --- | --- | ---: | --- |"]
    for case in cases:
        scope = "国家分支" if case["branch"] else "主入口（原隐藏）" if case["hidden"] else "主入口" if case["listed"] else "未列入菜单"
        lines.append(f"| {case['id']} | {case['name']} | {case['uniqueBridgeEdges']} | {scope} |")
    (OUT / "campaign-list.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
