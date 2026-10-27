"""Render reviewable settlement plans; never write game content or player data."""

from __future__ import annotations

from collections import Counter
import hashlib
import io
import json
import math
from pathlib import Path
from statistics import median
import sys
from zipfile import ZipFile

sys.path.insert(0, str(Path.home() / ".codex/tools/ew4-inspect-deps"))
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import shapefile
from shapely import transform
from shapely.geometry import Point, box, shape
from shapely.ops import nearest_points, unary_union

from build_city_plan import Occupancy, overlaps

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "maps/settlement_plan_20261026"
CACHE = ROOT / "maps/source_cache"
KINDS = {"city": ("城市", "#293940", "circle"),
         "industry": ("工厂", "#b34b32", "square"),
         "stable": ("马厩", "#32764f", "diamond"),
         "port": ("港口", "#176bb2", "triangle")}
SEA, LAND, INK = "#dcebf1", "#f5f5ee", "#25363b"
FONT = "C:/Windows/Fonts/msyh.ttc"
BOLD = "C:/Windows/Fonts/msyhbd.ttc"
RIVERS = {"japan": ("ishikari", "shinano", "tone", "kitakami", "kiso", "yodo", "chikugo"),
          "hre": ("rhine", "rhin", "rhone", "rhône", "danube", "donau", "elbe", "oder",
                  "wisla", "vistula", "weser", "meuse", "maas", "scheldt", "seine", "po", "arno")}


def font(size, bold=False):
    return ImageFont.truetype(BOLD if bold else FONT, size)


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


class View:
    def __init__(self, key):
        self.key = key
        self.width = 5000
        if key == "japan":
            reference = ROOT / "maps/reference_images_20261025"
            self.composition = json.loads((reference / "japan_to_okinawa_15_left50/composition.json").read_text(encoding="utf-8"))
            with Image.open(reference / "J1_original.jpg") as image:
                self.cx, self.cy = image.width / 2, image.height / 2
            self.bounds = self.composition["rotated_bounds"]
            self.height = round(self.width * 1052 / 1366)
            self.geo_clip = box(120, 24, 153, 50)
        else:
            self.composition = json.loads((OUT / "hre_crop.json").read_text(encoding="utf-8"))
            self.bounds = self.composition["bounds"]
            left, bottom, right, top = self.bounds
            self.height = round(self.width * (top-bottom) / (right-left))
            self.geo_clip = box(-8, 39, 32, 63)
            p1, p2, self.lon0, p0 = np.radians([44, 56, 12, 50])
            self.n = math.log(math.cos(p1) / math.cos(p2)) / math.log(math.tan(math.pi / 4 + p2 / 2) / math.tan(math.pi / 4 + p1 / 2))
            self.f = math.cos(p1) * math.tan(math.pi / 4 + p1 / 2) ** self.n / self.n
            self.rho0 = 6371008.8 * self.f / math.tan(math.pi / 4 + p0 / 2) ** self.n

    def xy(self, lon, lat):
        lon, lat = np.asarray(lon), np.asarray(lat)
        if self.key == "japan":
            c, s = math.cos(math.pi / 12), math.sin(math.pi / 12)
            u, v = 403 + (lon - 130) * 50.1 - self.cx, 1054 - (lat - 30) * 60 - self.cy
            x, y = c * u - s * v, s * u + c * v
            left, top, right, bottom = self.bounds
            return (x - left) * self.width / (right - left), (y - top) * self.height / (bottom - top)
        rho = 6371008.8 * self.f / np.tan(math.pi / 4 + np.radians(lat) / 2) ** self.n
        theta = self.n * (np.radians(lon) - self.lon0)
        x, y = rho * np.sin(theta), self.rho0 - rho * np.cos(theta)
        left, bottom, right, top = self.bounds
        return (x - left) * self.width / (right - left), (top - y) * self.height / (top - bottom)

    def project(self, geom):
        return transform(geom, self.xy, interleaved=False)


def parts(geom, kind):
    if geom.is_empty:
        return
    if geom.geom_type == kind:
        yield geom
    elif hasattr(geom, "geoms"):
        for sub in geom.geoms:
            yield from parts(sub, kind)


def read_source(name, view):
    filename = {"land": "ne_10m_land.zip", "lakes": "ne_10m_lakes.zip",
                "rivers": "ne_10m_rivers_lake_centerlines.zip"}[name]
    with ZipFile(CACHE / filename) as archive:
        stem = next(p[:-4] for p in archive.namelist() if p.endswith(".shp"))
        reader = shapefile.Reader(**{ext: io.BytesIO(archive.read(stem + "." + ext)) for ext in ("shp", "shx", "dbf")}, encoding="utf-8", encodingErrors="replace")
        for record in reader.iterShapeRecords():
            if not box(*record.shape.bbox).intersects(view.geo_clip):
                continue
            geom = shape(record.shape.__geo_interface__)
            if not geom.is_valid:
                geom = geom.buffer(0)
            geom = geom.intersection(view.geo_clip)
            if not geom.is_empty:
                yield geom, record.record.as_dict()


def draw_polygon(image, geom, fill, outline=None):
    draw = ImageDraw.Draw(image)
    for polygon in parts(geom, "Polygon"):
        draw.polygon(list(polygon.exterior.coords), fill=fill, outline=outline, width=2)
        for ring in polygon.interiors:
            draw.polygon(list(ring.coords), fill=SEA)


def base_map(view):
    frame = box(0, 0, view.width, view.height)
    land_geo = unary_union([g for g, _ in read_source("land", view)])
    lakes_geo = unary_union([g for g, _ in read_source("lakes", view)])
    land = view.project(land_geo.difference(lakes_geo)).intersection(frame)
    sea = frame.difference(view.project(land_geo))
    water = frame.difference(land)
    image = Image.new("RGB", (view.width, view.height), SEA)
    draw_polygon(image, land, LAND, "#839e99")
    draw = ImageDraw.Draw(image)
    # Graticules provide geographic orientation without implying political divisions.
    for lon in range(-5 if view.key == "hre" else 120, 33 if view.key == "hre" else 154, 5):
        points = [tuple(map(float, view.xy(lon, lat))) for lat in np.linspace(24 if view.key == "japan" else 39, 63 if view.key == "hre" else 50, 180)]
        draw.line(points, fill="#c5d4d5", width=1)
    for lat in range(25 if view.key == "japan" else 40, 64 if view.key == "hre" else 51, 5):
        points = [tuple(map(float, view.xy(lon, lat))) for lon in np.linspace(120 if view.key == "japan" else -8, 153 if view.key == "japan" else 32, 180)]
        draw.line(points, fill="#c5d4d5", width=1)
    river_names = set()
    for geom, attrs in read_source("rivers", view):
        name = str(attrs.get("name") or "").lower()
        if not any(token == name or (len(token) > 3 and token in name) for token in RIVERS[view.key]):
            continue
        river_names.add(name)
        for line in parts(view.project(geom).intersection(frame), "LineString"):
            if len(line.coords) > 1:
                draw.line(list(line.coords), fill="#9bbac9", width=3)
    return image, land, sea, water, river_names


def symbol(draw, x, y, kind, radius=9):
    color = KINDS[kind][1]
    draw.ellipse((x-radius-3, y-radius-3, x+radius+3, y+radius+3), fill="white")
    if kind == "city":
        draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill=color)
    elif kind == "industry":
        draw.rectangle((x-radius, y-radius, x+radius, y+radius), fill=color)
    elif kind == "stable":
        draw.polygon([(x, y-radius-2), (x+radius+2, y), (x, y+radius+2), (x-radius-2, y)], fill=color)
    else:
        draw.polygon([(x, y-radius-3), (x+radius+2, y+radius), (x-radius-2, y+radius)], fill=color)


def make_records(key, view, land, sea, water):
    source = json.loads((OUT / f"{key}.json").read_text(encoding="utf-8"))
    records, warnings, cropped = [], [], []
    for group, rows in source["groups"].items():
        for name, lon, lat, kind in rows:
            x, y = map(float, view.xy(lon, lat))
            if not 0 < x < view.width or not 0 < y < view.height:
                if key == "japan":
                    raise ValueError(f"Out of frame: {key}/{name}")
                cropped.append(dict(name=name,group=group,lon=lon,lat=lat,kind=kind,reason="外围裁切；原始候选保留"))
                continue
            original = Point(x, y)
            # The historical Zuiderzee is a current lake; flag it instead of asserting modern sea access.
            historic_water = key == "hre" and "须德海" in name
            target = (water if historic_water else sea) if kind == "port" else land
            display = original
            if not target.covers(original):
                edge = nearest_points(original, target)[1]
                # Prefer a point just inside the intended surface, not exactly on a shoreline.
                options = [Point(edge.x + dx, edge.y + dy) for dx, dy in ((0,0),(2,0),(-2,0),(0,2),(0,-2),(3,3),(-3,-3))]
                display = next((p for p in options[1:] if target.contains(p)), edge)
            distance_pixels = display.distance(original)
            step_km = (view.bounds[2]-view.bounds[0])/1000/view.width if key == "hre" else 111.2 / (60 * view.width / 1366)
            displacement = round(distance_pixels * step_km, 2)
            if displacement > 22:
                raise ValueError(f"Surface displacement exceeds review threshold: {name}/{displacement} km")
            stable_id = key + "-" + hashlib.sha256(f"{group}:{name}:{lon}:{lat}".encode()).hexdigest()[:12]
            notes = []
            if kind == "industry":
                notes.append("矿冶、锻造或作坊功能候选；并非已核定年代的工厂名录")
            if kind == "stable":
                notes.append("平原、盆地或驮运补给候选；不宣称史实马场")
            if historic_water:
                notes.append("须德海历史海湾，现代围垦和堤坝须修正；本版不验收最终海航连通")
                warnings.append(name + "：须德海历史岸线待重建")
            if key == "japan" and group == "北海道与北方四岛":
                notes.append("北方地名仅作跨时期定位；早期剧本须复核聚落及开发年代")
            if group == "朝鲜半岛":
                notes.append("朝鲜半岛低密度城市骨架；历史名称、城市等级及政治归属依剧本确定")
            if group == "俄远东沿海":
                notes.append("海参崴，又名符拉迪沃斯托克；1860年以后城市开发，早期剧本须复核启用时期")
            if key == "japan" and name in ("别子", "波佐见", "夕张", "幌内", "釜石", "上国", "相川"):
                notes.append("设施启用时期待复核；战国剧本不得直接套用近代工业")
            if kind == "port":
                notes.append("三角符号位于候选水面，港级、岸向和最终格心尚未确定")
            records.append(dict(id=stable_id, name=name, lon=lon, lat=lat, kind=kind, group=group,
                                x=round(display.x, 3), y=round(display.y, 3),
                                source_x=round(x, 3), source_y=round(y, 3),
                                coast_adjustment_approx_km=displacement,
                                surface="historical_bay_candidate" if historic_water else "sea" if kind == "port" else "land",
                                notes=notes))
    blocked = [(r["x"], r["y"]) for r in records if r["kind"] != "port"]
    for record in (r for r in records if r["kind"] == "port"):
        target = water if record["surface"] == "historical_bay_candidate" else sea
        initial = Point(record["x"], record["y"])
        original = Point(record["source_x"], record["source_y"])
        candidates = [initial]
        for radius in (8, 16, 24, 32, 40, 48, 64, 80):
            candidates.extend(Point(initial.x + radius*math.cos(a), initial.y + radius*math.sin(a)) for a in np.linspace(0,2*math.pi,33)[:-1])
        found = next((p for p in candidates if target.contains(p) and p.distance(original)*step_km <= 22
                      and all(math.hypot(p.x-x,p.y-y) >= 24 for x,y in blocked)), None)
        if found is None:
            raise ValueError("No separate water-side port candidate: " + record["name"])
        record["x"], record["y"] = round(found.x,3), round(found.y,3)
        record["coast_adjustment_approx_km"] = round(found.distance(original)*step_km,2)
        blocked.append((found.x,found.y))
    if len({r["id"] for r in records}) != len(records) or len({r["name"] for r in records}) != len(records):
        raise ValueError("Duplicate IDs or names")
    return source, records, warnings, cropped


def annotate(base, records, extent, width, title, subtitle, path):
    l, t, r, b = extent
    scale = width / (r-l)
    height = round((b-t)*scale)
    header, footer = 165, 100
    image = Image.new("RGB", (width, height+header+footer), "white")
    image.paste(base.crop(tuple(round(v) for v in extent)).resize((width, height), Image.Resampling.LANCZOS), (0, header))
    draw = ImageDraw.Draw(image)
    draw.text((38, 19), title, font=font(47, True), fill=INK)
    draw.text((40, 83), subtitle, font=font(24), fill=INK)
    for index, (kind, (label, color, _)) in enumerate(KINDS.items()):
        x = 52 + 235*index
        symbol(draw, x, 137, kind, 9)
        draw.text((x+21, 119), label, font=font(27, True), fill=color)
    selected = [p for p in records if l <= p["x"] <= r and t <= p["y"] <= b]
    positions = {p["id"]: ((p["x"]-l)*scale, (p["y"]-t)*scale+header) for p in selected}
    occupancy = Occupancy()
    for p in selected:
        x, y = positions[p["id"]]
        occupancy.add((x-12, y-12, x+12, y+12), p["id"])
    placements, missing = [], []
    for p in sorted(selected, key=lambda p: (p["kind"] != "city", p["y"], p["x"])):
        x, y = positions[p["id"]]
        ft = font(27 if p["kind"] == "city" else 24, p["kind"] == "city")
        bbox = draw.textbbox((0,0), p["name"], font=ft)
        tw, th = bbox[2]-bbox[0], bbox[3]-bbox[1]
        found = None
        for distance in (16, 30, 48, 70, 96, 126, 162, 206, 258, 320, 395, 480, 580):
            for angle in (0, 180, -45, 45, -135, 135, -90, 90):
                c, s = math.cos(math.radians(angle)), math.sin(math.radians(angle))
                px, py = x+distance*c, y+distance*s
                tx, ty = px if c > .5 else px-tw if c < -.5 else px-tw/2, py-th/2
                rect = (tx-5,ty-5,tx+tw+5,ty+th+5)
                if rect[0] < 10 or rect[2] > width-10 or rect[1] < header+12 or rect[3] > header+height-12:
                    continue
                if occupancy.clear(rect,p["id"]):
                    found = (rect,tx,ty)
                    break
            if found:
                break
        if found is None:
            missing.append(p["name"])
            continue
        rect,tx,ty = found
        occupancy.add(rect,p["id"])
        placements.append((p,rect,tx,ty,ft,bbox))
    for p,rect,tx,ty,ft,bbox in placements:
        x,y = positions[p["id"]]
        nx,ny = min(max(x,rect[0]+5),rect[2]-5),min(max(y,rect[1]+5),rect[3]-5)
        if math.hypot(x-nx,y-ny) > 20:
            draw.line((x,y,nx,ny),fill="#94a0a2",width=1)
    for p in selected:
        symbol(draw,*positions[p["id"]],p["kind"],radius=6 if p["kind"]=="city" else 7)
    for p,rect,tx,ty,ft,bbox in placements:
        draw.text((tx-bbox[0],ty-bbox[1]),p["name"],font=ft,fill=KINDS[p["kind"]][1],stroke_width=3,stroke_fill="white")
    angle=math.pi/12 if path.name.startswith("japan") else 0
    bx,by=width-90,header-20
    tip=(bx+math.sin(angle)*55,by-math.cos(angle)*55)
    draw.line((bx,by,*tip),fill=INK,width=3)
    draw.ellipse((tip[0]-4,tip[1]-4,tip[0]+4,tip[1]+4),fill=INK)
    draw.text((tip[0]-10,tip[1]-40),"N",font=font(26,True),fill=INK)
    count = Counter(p["kind"] for p in selected)
    text = "  /  ".join(KINDS[k][0]+" "+str(count[k]) for k in KINDS)
    draw.text((40,header+height+14),text+"  |  无固定归属，等级未定",font=font(25),fill=INK)
    draw.text((40,header+height+54),"候选位置示意，未吸附游戏格心；工厂含矿冶作坊，马厩为补给选址。",font=font(23),fill="#59676c")
    image.save(path,quality=92,optimize=True,subsampling=0)
    collisions=sum(overlaps(a[1],b[1]) for i,a in enumerate(placements) for b in placements[i+1:])
    return dict(file=path.name, points=len(selected), labels=len(placements), missing_labels=missing,
                label_overlaps=collisions, width=image.width, height=image.height,
                max_label_leader_pixels=round(max((math.hypot(positions[p["id"]][0]-tx,positions[p["id"]][1]-ty) for p,_,tx,ty,_,_ in placements),default=0)))


def detail_extent(view, corners):
    coords=[view.xy(lon,lat) for lon in np.linspace(corners[0],corners[2],15) for lat in np.linspace(corners[1],corners[3],15)]
    return (max(0,min(float(x) for x,y in coords)-35),max(0,min(float(y) for x,y in coords)-35),
            min(view.width,max(float(x) for x,y in coords)+35),min(view.height,max(float(y) for x,y in coords)+35))


def median_city_spacing(records):
    def distance(a,b):
        la,lb=math.radians(a["lat"]),math.radians(b["lat"])
        dlat=lb-la
        dlon=math.radians(b["lon"]-a["lon"])
        h=math.sin(dlat/2)**2+math.cos(la)*math.cos(lb)*math.sin(dlon/2)**2
        return 12742*math.asin(min(1,math.sqrt(h)))
    return round(median(min(distance(a,b) for b in records if a["id"]!=b["id"]) for a in records),1)


def main():
    summary={"date":"2026-10-26","stage":"settlement placement proposal, no runtime changes","maps":{}}
    all_data={}
    for key in ("japan","hre"):
        view=View(key)
        base,land,sea,water,river_names=base_map(view)
        source,records,warnings,cropped=make_records(key,view,land,sea,water)
        base.save(OUT/f"{key}_base.jpg",quality=92,optimize=True)
        counts=dict(Counter(r["kind"] for r in records))
        images=[annotate(base,records,(0,0,view.width,view.height),5000,source["title"]+" · 第一版",
                        "15°收左界构图；列岛151城、朝鲜半岛23城，俄远东补海参崴" if key=="japan" else "外围收紧版；丹麦—低地—瑞士—北意大利保留，不表示帝国边界",
                        OUT/f"{key}_overview.jpg")]
        details = [("central","中部—畿内建筑详图",(134.2,33.7,140.2,37.4)),
                   ("north","北海道与北方四岛建筑详图",(139.6,41.3,148.7,45.8)),
                   ("south","九州—四国—琉球建筑详图",(127.0,25.9,135.1,34.5)),
                   ("korea","朝鲜半岛城市分布 · 低密度版",(124.0,33.1,130.0,42.8))] if key=="japan" else [
                   ("northwest","低地—莱茵—北德建筑详图",(2.0,49.5,10.8,54.5)),
                   ("alps","瑞士—阿尔卑斯—北意大利建筑详图",(5.5,43.0,14.1,48.5)),
                   ("east","波希米亚—奥地利—东部周边建筑详图",(12.0,45.5,23.3,53.0))]
        for suffix,title,corners in details:
            detail_records=[p for p in records if p["group"]=="朝鲜半岛"] if suffix=="korea" else records
            images.append(annotate(base,detail_records,detail_extent(view,corners),3400,title,
                                  "仅23处主要城市；不配置半岛工厂、马厩或港口" if suffix=="korea" else "颜色与符号沿用总图；引线连接实际候选位置",OUT/f"{key}_{suffix}.jpg"))
        with Image.open(OUT/f"{key}_overview.jpg") as image:
            image.thumbnail((2200,2400),Image.Resampling.LANCZOS)
            image.save(OUT/f"{key}_preview.jpg",quality=87,optimize=True)
        projected=dict(title=source["title"],width=view.width,height=view.height,counts=counts,records=records,
                       groups=list(source["groups"]),images=images)
        write_json(OUT/f"{key}_points.json",projected)
        all_data[key]=projected
        surface_guards={"land":land.buffer(.01),"sea":sea.buffer(.01),"historical_bay_candidate":water.buffer(.01)}
        surface_mismatches=sum(not surface_guards[p["surface"]].covers(Point(p["x"],p["y"])) for p in records)
        symbol_overlaps=sum(math.hypot(a["x"]-b["x"],a["y"]-b["y"]) < (6 if a["kind"]=="city" else 7)+(6 if b["kind"]=="city" else 7)
                            for i,a in enumerate(records) for b in records[i+1:])
        if surface_mismatches or symbol_overlaps:
            raise ValueError(f"Surface/marker review required: {key}/{surface_mismatches}/{symbol_overlaps}")
        summary["maps"][key]=dict(total=len(records),counts=counts,images=images,
                                  out_of_frame=0,duplicate_names=0,duplicate_ids=0,
                                  surface_mismatches=surface_mismatches,symbol_overlaps=symbol_overlaps,
                                  coast_adjustments=sum(r["coast_adjustment_approx_km"]>0 for r in records),
                                  maximum_coast_adjustment_approx_km=max(r["coast_adjustment_approx_km"] for r in records),
                                  warnings=warnings,rivers=sorted(river_names),
                                  projection="15 degree clockwise reference-registered equirectangular; provisional" if key=="japan" else "spherical Lambert conformal conic; lon0=12, lat0=50, parallels=44/56",
                                  bounds=view.bounds,game_hex_snapping_verified=False,sea_navigation_verified=False)
        if key=="hre":
            old=view.composition["previous_bounds"]
            new=view.bounds
            summary["maps"][key]["crop"]=dict(previous_bounds=old,current_bounds=new,
                frame_area_reduction_percent=round((1-(new[2]-new[0])*(new[3]-new[1])/((old[2]-old[0])*(old[3]-old[1])))*100,2),
                outside_after_crop=cropped)
        else:
            korea=[p for p in records if p["group"]=="朝鲜半岛"]
            japan_cities=[p for p in records if p["kind"]=="city" and p["group"] not in ("朝鲜半岛","俄远东沿海")]
            korea_spacing=median_city_spacing(korea)
            japan_spacing=median_city_spacing(japan_cities)
            if len(korea)!=23 or any(p["kind"]!="city" for p in korea) or korea_spacing<=japan_spacing:
                raise ValueError("Korean cities must remain a sparse city-only skeleton")
            summary["maps"][key]["city_spacing"]=dict(japan_cities=len(japan_cities),korea_cities=len(korea),
                japan_median_nearest_city_km=japan_spacing,korea_median_nearest_city_km=korea_spacing)
    sources=[]
    for filename in ("ne_10m_land.zip","ne_10m_lakes.zip","ne_10m_rivers_lake_centerlines.zip"):
        sources.append(dict(file="../source_cache/"+filename,sha256=hashlib.sha256((CACHE/filename).read_bytes()).hexdigest(),
                            url="https://naturalearth.s3.amazonaws.com/10m_physical/"+filename,license="Natural Earth public domain"))
    summary["sources"]=sources
    summary["limits"]=["人工地理初选，不是设施史考证；地名与设施启用年份须依剧本复核", "海岸基于现代Natural Earth，须德海与河口有历史修正待办", "未制作政治边界、地形阻行、禁航海区、最终六角格或性能验收"]
    write_json(OUT/"verification.json",summary)
    # Inline data keeps the viewer usable directly from file:// without a local server.
    data_script="window.PLAN_DATA="+json.dumps(all_data,ensure_ascii=False).replace("<","\\u003c")+";\n"
    (OUT/"viewer-data.js").write_text(data_script,encoding="utf-8")
    print(json.dumps({k:dict(total=v["total"],counts=v["counts"],missing_labels=sum(len(i["missing_labels"]) for i in v["images"]),overlaps=sum(i["label_overlaps"] for i in v["images"]),coast_adjustments=v["coast_adjustments"],max_km=v["maximum_coast_adjustment_approx_km"]) for k,v in summary["maps"].items()},ensure_ascii=False))
    if any(i["missing_labels"] or i["label_overlaps"] for v in summary["maps"].values() for i in v["images"]):
        raise SystemExit("Label layout needs review")


if __name__=="__main__":
    main()
