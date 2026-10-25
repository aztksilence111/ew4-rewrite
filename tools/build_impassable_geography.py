"""Build reusable original-derived geography sprites and the East Asia barrier configuration."""

from __future__ import annotations

import hashlib
import json
import math
from collections import deque
from pathlib import Path
import sys

sys.path.insert(0, str(Path.home() / ".codex/tools/ew4-inspect-deps"))
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from shapely.geometry import LineString, Point

import build_eastasia_runtime as runtime

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "rewrite/data"
REPORT = ROOT / "maps/eastasia_rewrite/impassable"


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def write(path, value, compact=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=None if compact else 2) + "\n", encoding="utf-8")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def crop(atlas, entry):
    return atlas.crop(tuple(round(entry[k]) for k in ("x", "y")) +
                      (round(entry["x"] + entry["w"]), round(entry["y"] + entry["h"])))


def snow(source):
    piece = source.resize((round(source.width * .95), round(source.height * 1.24)), Image.Resampling.LANCZOS)
    a = np.array(piece).astype(float)
    y, x = np.indices(a.shape[:2])
    limit = piece.height * (.39 + .055 * np.sin(x / piece.width * 24) + .025 * np.sin(x / piece.width * 53))
    weight = np.clip((limit - y) / (piece.height * .09), 0, 1) * .82
    brightness = a[:, :, :3].mean(axis=2)
    for channel, tint in enumerate((238, 244, 247)):
        target = tint * (.68 + .32 * brightness / 255)
        a[:, :, channel] = a[:, :, channel] * (1 - weight) + target * weight
    return Image.fromarray(np.uint8(np.clip(a, 0, 255))), .95, 1.24


def ocean(ratio):
    pattern = Image.open(DATA / "client/images/map_pt@2x.png").convert("RGBA")
    piece = Image.new("RGBA", (64 * ratio, 72 * ratio))
    p = np.array(pattern)
    pixels = np.zeros((72 * ratio, 64 * ratio, 4), dtype=np.uint8)
    for y in range(pixels.shape[0]):
        for x in range(pixels.shape[1]):
            v = p[(y * 2 // ratio) % p.shape[0], (x * 2 // ratio) % p.shape[1], :3].mean() / 255
            pixels[y, x] = (round(35 + v * 12), round(67 + v * 15), round(88 + v * 18), 255)
    piece = Image.fromarray(pixels)
    mask = Image.new("L", piece.size)
    ImageDraw.Draw(mask).polygon([(32*ratio,0),(64*ratio,18*ratio),(64*ratio,54*ratio),
                                 (32*ratio,72*ratio),(0,54*ratio),(0,18*ratio)], fill=255)
    piece.putalpha(mask)
    return piece


def assets():
    path = DATA / "client/manifest.json"
    manifest = read(path)
    protected = {}
    for name, ratio in (("tiles", 1), ("tiles_hd", 2)):
        atlas_path = DATA / f"client/images/{name}.png"
        source = Image.open(atlas_path).convert("RGBA")
        entries = manifest["atlases"][name]
        protected[name] = {k: digest(crop(source, v).tobytes()) for k, v in entries.items()
                           if not k.startswith("impassable_")}
        pieces = []
        for i in range(1, 7):
            old = entries[f"mountain_{i}.png"]
            piece, sx, sy = snow(crop(source, old))
            pieces.append((f"impassable_mountain_{i}.png", piece, old["refx"]*sx, old["refy"]*sy))
        pieces.append(("impassable_sea.png", ocean(ratio), 32*ratio, 36*ratio))
        if all(key in entries for key, *_ in pieces):
            expanded = source.copy()
            for key, piece, _, _ in pieces:
                assert (entries[key]["w"], entries[key]["h"]) == piece.size
                expanded.paste(piece, (round(entries[key]["x"]), round(entries[key]["y"])))
        else:
            x, y, row_height = 2, source.height + 2, 0
            positioned = []
            for key, piece, refx, refy in pieces:
                if x + piece.width + 2 > source.width:
                    y += row_height + 2
                    x, row_height = 2, 0
                entries[key] = dict(x=x, y=y, w=piece.width, h=piece.height, refx=refx, refy=refy)
                positioned.append((piece, (x, y)))
                x += piece.width + 2
                row_height = max(row_height, piece.height)
            expanded = Image.new("RGBA", (source.width, y + row_height + 2))
            expanded.paste(source, (0, 0))
            for piece, position in positioned:
                expanded.paste(piece, position)
        for key, expected in protected[name].items():
            assert digest(crop(expanded, entries[key]).tobytes()) == expected, key
        expanded.save(atlas_path, compress_level=6)
    write(path, manifest, compact=True)
    preview = Image.new("RGB", (1200, 480), "#898d83")
    draw = ImageDraw.Draw(preview)
    font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 24)
    a = Image.open(DATA / "client/images/tiles_hd.png").convert("RGBA")
    for i in range(6):
        for row, prefix in ((0, "mountain"), (1, "impassable_mountain")):
            p = crop(a, manifest["atlases"]["tiles_hd"][f"{prefix}_{i+1}.png"])
            p.thumbnail((170, 150))
            preview.paste(p, (i*200+(200-p.width)//2, row*180+45), p)
    draw.text((15, 8), "原版山地", font=font, fill="white")
    draw.text((15, 188), "禁行雪山", font=font, fill="white")
    draw.rectangle((20, 390, 575, 465), fill=runtime.geo.SEA)
    draw.rectangle((625, 390, 1180, 465), fill=(44, 78, 102))
    draw.text((35, 410), "普通海洋", font=font, fill="white")
    draw.text((640, 410), "禁航海洋", font=font, fill="white")
    preview.save(REPORT / "terrain-samples.jpg", quality=94)
    return manifest, protected


def world(coordinates):
    return [runtime.eastasia_cartography.world(runtime, *p) for p in coordinates]


def neighbors(cell):
    col, row = cell % runtime.GRID_W, cell // runtime.GRID_W
    return [y*runtime.GRID_W+x for x, y in (runtime.neighbor(col, row, d) for d in range(6))
            if 0 <= x < runtime.GRID_W and 0 <= y < runtime.GRID_H]


def shortest(start, end, allowed):
    pending, parent = deque([start]), {start: None}
    while pending:
        cell = pending.popleft()
        if cell == end:
            route = []
            while cell is not None:
                route.append(cell)
                cell = parent[cell]
            return route[::-1]
        for nxt in neighbors(cell):
            if nxt in allowed and nxt not in parent:
                parent[nxt] = cell
                pending.append(nxt)
    raise ValueError(f"No route between {start} and {end}")


def geography():
    plan = read(ROOT / "maps/eastasia_rewrite/impassable_plan.json")
    binary = (DATA / "original/eastasia.bin").read_bytes()
    raw = np.frombuffer(binary[16:], dtype=np.uint8).reshape((-1, 12))
    centers = [runtime.center(i % runtime.GRID_W, i // runtime.GRID_W) for i in range(len(raw))]
    line = LineString(world(plan["ridge"]))
    # Convert physical distances using the map's existing projection, not a new pixel scale.
    pixels_per_km = runtime.SCALE * 1000 / runtime.geo.METRES_PER_PIXEL
    radius = plan["crestHalfWidthKm"] * pixels_per_km
    land = {i for i in range(len(raw)) if raw[i, 0] != 1}
    blocked = {i for i in land if line.distance(Point(centers[i])) <= radius}
    occupied = {s["cell"] for name in ("cities.json", "facilities.json", "bridges.json")
                for s in read(ROOT / "maps/eastasia_rewrite" / name)}
    conflicts = sorted(blocked & occupied)
    if conflicts:
        raise ValueError(f"Crest intersects existing site cells: {conflicts}")
    passages = []
    for item in plan["passes"]:
        points = world(item["route"])
        route = []
        anchors = [min(land, key=lambda i: math.dist(centers[i], p)) for p in points]
        near_route = {i for i in land if LineString(points).distance(Point(centers[i])) < radius * 1.5}
        for start, end in zip(anchors, anchors[1:]):
            route.extend(shortest(start, end, near_route)[:-1])
        route.append(anchors[-1])
        removed = sorted(blocked.intersection(route))
        if not removed:
            raise ValueError("Pass does not intersect crest: " + item["id"])
        blocked.difference_update(route)
        location = world([item["location"]])[0]
        mapped = min(route, key=lambda i: math.dist(centers[i], location))
        passages.append(dict(item, cells=list(dict.fromkeys(route)), openedBarrierCells=removed,
                             mappedPassCell=mapped, mappedPassOffsetKm=round(math.dist(centers[mapped], location) / pixels_per_km, 2)))
    for passage in passages:
        corridor = LineString(world(passage["route"]))
        allowed = {i for i in land - blocked if corridor.distance(Point(centers[i])) < radius * 1.5}
        start, end = passage["cells"][0], passage["cells"][-1]
        shortest(start, end, allowed)
        try:
            shortest(start, end, allowed - set(passage["openedBarrierCells"]))
        except ValueError:
            passage["localClosureVerified"] = True
        else:
            raise ValueError("Local route bypasses closed pass: " + passage["id"])
    pass_cells = {i for p in passages for i in p["openedBarrierCells"]}
    config = dict(schema=1, maps=[dict(map="eastasia", width=runtime.GRID_W, height=runtime.GRID_H,
        baseTerrainSha256=digest(binary), terrainOverrides=[dict(column=i % runtime.GRID_W,
        row=i // runtime.GRID_W, type="impassable_mountain") for i in sorted(blocked)] + [
        dict(column=i % runtime.GRID_W, row=i // runtime.GRID_W, type="mountain_pass") for i in sorted(pass_cells)])])
    write(DATA / "rewrite/geography.json", config)
    template_path = DATA / "original/eastasia.map.json"
    template = read(template_path)
    template["version"] = 2
    for i, cell in enumerate(template["cells"]):
        cell["terrain"].pop("barrier", None)
        cell["terrain"].pop("minimumEntryCost", None)
        if i in blocked:
            cell["terrain"]["barrier"] = "Mountain"
            cell["defenseTerrain"] = "mountain"
            cell["fireTerrain"] = "open"
        elif i in pass_cells:
            cell["terrain"].update(terrainCost=6, minimumEntryCost=6)
            cell["defenseTerrain"] = "mountain"
            cell["fireTerrain"] = "open"
    write(template_path, template, compact=True)
    extension_path = DATA / "rewrite/extension.json"
    extension = read(extension_path) if extension_path.exists() else dict(schema=1, apiVersion=3)
    extension.update(apiVersion=3, geography="rewrite/geography.json")
    write(extension_path, extension)
    report = dict(schema=1, map="eastasia", baseTerrainSha256=digest(binary), blockedCells=sorted(blocked),
                  passes=passages, passMountainCells=sorted(pass_cells), passMinimumEntryCost=6,
                  existingSitesUnchanged=len(occupied), sourcePlan="../impassable_plan.json", errors=0)
    write(REPORT / "himalaya-cells.json", report)
    review = Image.new("RGB", (1500, 560), "#eeeeeb")
    draw = ImageDraw.Draw(review)
    all_ids = blocked | {i for p in passages for i in p["cells"]}
    x0 = min(centers[i][0] for i in all_ids)-70
    y0 = min(centers[i][1] for i in all_ids)-80
    scale = min(1430 / (max(centers[i][0] for i in all_ids)-x0+70), 430 / (max(centers[i][1] for i in all_ids)-y0+70))
    for i in land:
        x, y = centers[i]
        if x < x0 or y < y0 or (x-x0)*scale > 1480 or (y-y0)*scale > 470:
            continue
        fill = "#8b999c" if i in blocked else "#cccebe" if raw[i,0] == 9 else "#e0e1d5"
        polygon = [(30+(x+dx-x0)*scale, 50+(y+dy-y0)*scale) for dx,dy in ((0,-36),(32,-18),(32,18),(0,36),(-32,18),(-32,-18))]
        draw.polygon(polygon, fill=fill, outline="#aeb4ac")
    font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 20)
    draw.text((18, 10), "当前东亚：喜马拉雅禁行带（深灰）与历史山口概化通道（绿色）", font=font, fill="#253a3c")
    for j, passage in enumerate(passages):
        pts = [(30+(centers[i][0]-x0)*scale, 50+(centers[i][1]-y0)*scale) for i in passage["cells"]]
        draw.line(pts, fill="#34795a", width=5)
        x,y = pts[len(pts)//2]
        draw.text((x+8,y+8), str(j+1), font=font, fill="#123c2a", stroke_width=2, stroke_fill="white")
        draw.text((18+j*495, 505), f"{j+1}. {passage['name']}", font=font, fill="#234936")
    review.save(REPORT / "himalaya-grid.jpg", quality=94)
    return report


def main():
    REPORT.mkdir(parents=True, exist_ok=True)
    manifest, protected = assets()
    report = geography()
    paths = sorted(p for part in ("original", "legacy", "client", "rewrite") for p in (DATA / part).rglob("*")
                   if p.is_file() and p.suffix != ".md")
    index = read(DATA / "runtime-content.json")
    index["files"] = [dict(path=p.relative_to(DATA).as_posix(), bytes=p.stat().st_size, sha256=digest(p.read_bytes())) for p in paths]
    write(DATA / "runtime-content.json", index)
    write(REPORT / "build-verification.json", dict(protectedAtlasSprites={k:len(v) for k,v in protected.items()},
        originalAtlasSpritesUnchanged=True, newSpritesPerAtlas=7, blockedCells=len(report["blockedCells"]),
        passCorridors=len(report["passes"]), runtimeFiles=len(paths), errors=0))
    print(json.dumps(dict(blockedCells=len(report["blockedCells"]), passes=len(report["passes"]), errors=0)))


if __name__ == "__main__":
    main()
