"""Join small source seams and trace estuary channels already present in the coastline."""
import heapq
import math

import numpy as np
from PIL import Image, ImageFilter
from shapely.geometry import LineString, Point, box
from shapely.ops import nearest_points, unary_union
from shapely.strtree import STRtree


def name(attrs):
    return str(attrs.get('name_en') or attrs.get('name') or '').lower()


def join_seams(runtime, features):
    parts = [(p, attrs) for g, attrs in features for p in runtime.geo.geometries(g, 'LineString')]
    tree = STRtree([p for p, _ in parts])
    connections, seen = [], set()
    for index, (line, attrs) in enumerate(parts):
        for side in (0, -1):
            end = Point(line.coords[side])
            candidates = [int(j) for j in tree.query(end.buffer(6)) if j != index and name(parts[j][1]) == name(attrs)]
            if not candidates:
                continue
            target = min(candidates, key=lambda j: end.distance(parts[j][0]))
            gap = end.distance(parts[target][0])
            if gap <= .025 or gap > 6:
                continue
            joined = nearest_points(end, parts[target][0])[1]
            key = tuple(sorted((tuple(round(v, 4) for v in end.coords[0]), tuple(round(v, 4) for v in joined.coords[0]))))
            if key in seen:
                continue
            seen.add(key)
            connections.append((LineString([end.coords[0], joined.coords[0]]),
                                dict(name=attrs.get('name_en') or attrs.get('name'), scalerank=attrs.get('scalerank'),
                                     repair='source_seam', gapWorldPixels=round(gap*2, 3))))
    return features+connections


def water_path(mask, start, radius=260):
    x, y = [round(v) for v in start]
    left, top = max(0, x-radius), max(0, y-radius)
    right, bottom = min(mask.width, x+radius+1), min(mask.height, y+radius+1)
    if right-left < 40 or bottom-top < 40:
        return None
    land = mask.crop((left, top, right, bottom))
    water_image = Image.eval(land, lambda v: 255-v)
    water = np.asarray(water_image) >= 128
    navigable = np.asarray(water_image.filter(ImageFilter.MaxFilter(5))) >= 128
    # Open water has room for the full river width and a gradual loss of its banks.
    opened = np.asarray(water_image.filter(ImageFilter.MinFilter(29))) >= 128
    softness = np.asarray(water_image.filter(ImageFilter.GaussianBlur(4)), dtype=float)/255
    h, w = water.shape
    sx, sy = x-left, y-top
    nearest = [(math.hypot(dx, dy), sx+dx, sy+dy) for dy in range(-14, 15) for dx in range(-14, 15)
               if 0 <= sx+dx < w and 0 <= sy+dy < h and water[sy+dy, sx+dx]]
    if not nearest:
        return None
    distance, sx, sy = min(nearest)
    if distance > 14:
        return None
    cost = np.full((h, w), np.inf)
    cost[sy, sx] = 0
    previous, queue = {}, [(0, sx, sy)]
    destination = None
    while queue:
        value, px, py = heapq.heappop(queue)
        if value != cost[py, px]:
            continue
        if opened[py, px]:
            destination = (px, py)
            break
        for dx, dy in [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)]:
            nx, ny = px+dx, py+dy
            if not (0 <= nx < w and 0 <= ny < h and navigable[ny, nx]):
                continue
            step = math.hypot(dx, dy)*(1+4*(1-softness[ny, nx])+(0 if water[ny, nx] else 30))
            next_cost = value+step
            if next_cost < cost[ny, nx]:
                cost[ny, nx] = next_cost
                previous[(nx, ny)] = (px, py)
                heapq.heappush(queue, (next_cost, nx, ny))
    if destination is None:
        return None
    points = [destination]
    while points[-1] != (sx, sy):
        points.append(previous[points[-1]])
    points.reverse()
    points = [tuple(start)]+[(px+left, py+top) for px, py in points]
    path = LineString(points).simplify(.6)
    return path if path.length > .1 else None


def estuaries(runtime, lines, widths, names, mask):
    proj, metres = runtime.geo.PROJ, runtime.geo.METRES_PER_PIXEL
    extent = box(proj.xmin-150*metres, proj.ymin-200*metres, proj.xmax+150*metres, proj.ymax+100*metres)
    coast_land = runtime.to_source_geometry(unary_union([g for g, _ in runtime.geo.read_shapes('land', extent)]))
    coast = coast_land.boundary
    records = []
    runtime.water_mouth_failures = []
    seen = set()
    for line, width, river_name in zip(lines, widths, names):
        for side in (0, -1):
            endpoint = Point(line.coords[side])
            source = Point((endpoint.x+runtime.IMAGE_LEFT)/2, (endpoint.y+runtime.IMAGE_TOP)/2)
            if source.distance(coast)*2 > 14 or not (30 < endpoint.x < mask.width-30 and 30 < endpoint.y < mask.height-30):
                continue
            key = (round(endpoint.x), round(endpoint.y))
            if key in seen:
                continue
            seen.add(key)
            path = water_path(mask, endpoint.coords[0])
            if path is None:
                path = water_path(mask, endpoint.coords[0], radius=520)
            if path is None:
                x, y = [round(v) for v in endpoint.coords[0]]
                if mask.crop((x-14, y-14, x+15, y+15)).getextrema()[1] != 0:
                    runtime.water_mouth_failures.append(dict(name=river_name, endpoint=list(endpoint.coords[0])))
                continue
            back = line.interpolate(min(14, line.length) if side == 0 else max(0, line.length-14))
            path = LineString([back.coords[0]]+list(path.coords))
            records.append(dict(path=path, name=river_name, width=width, endpoint=list(endpoint.coords[0]),
                                length=round(path.length, 2)))
    return records
