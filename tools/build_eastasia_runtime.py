"""Build an additive rewrite map from the approved East Asia city plan."""

from __future__ import annotations

import argparse
from collections import Counter, deque
import hashlib
import json
import math
from pathlib import Path
import struct
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path.home() / ".codex/tools/ew4-inspect-deps"))
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from shapely import transform as transform_geometry
from shapely.geometry import LineString, Point, box, shape
from shapely.ops import nearest_points, unary_union

import build_eastasia as geo
import eastasia_relief
import eastasia_settlements
import eastasia_crossings
import eastasia_additions
import eastasia_cartography
import eastasia_water
import eastasia_hydrology
from ew4_formats import TerrainMap

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "rewrite/data"
OUT = ROOT / "rewrite/content/eastasia"
REPORT = ROOT / "maps/eastasia_rewrite"
GRID_W, GRID_H = 158, 136
SCALE = 2
IMAGE_LEFT, IMAGE_TOP = -32, -54
IMAGE_W, IMAGE_H = 10144, 7362
SOURCE_CITIES = ROOT / "maps/city_plan/conquest_v2/projected_cities.json"
TERRAIN_ADDITIONS = REPORT / "terrain_additions.geojson"
RIVER_ADDITIONS = {"ganges", "yamuna", "godavari", "mahanadi", "narmada", "tapti",
                   "ob", "irtysh", "yenisey", "lena", "tobol", "xiang", "luan", "red", "ping", "selenga", "nen", "fuchun"}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_xml(name, root):
    path = OUT / "original" / name
    ET.indent(root, space="  ")
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)


def center(col, row):
    return col * 64 + (row % 2) * 32, row * 54 - 18


def world_to_source(x, y):
    return x / SCALE, y / SCALE


def source_to_image(x, y):
    return x * SCALE - IMAGE_LEFT, y * SCALE - IMAGE_TOP


def neighbor(col, row, direction):
    return [(col+1,row),(col+row%2,row+1),(col+row%2-1,row+1),
            (col-1,row),(col+row%2-1,row-1),(col+row%2,row-1)][direction]


def to_source_geometry(geometry):
    return transform_geometry(geometry, lambda x,y,z=None: (
        (np.asarray(x)-geo.PROJ.xmin)*geo.WIDTH/(geo.PROJ.xmax-geo.PROJ.xmin),
        (geo.PROJ.ymax-np.asarray(y))*geo.HEIGHT/(geo.PROJ.ymax-geo.PROJ.ymin)), interleaved=False)


def geography():
    metres = geo.METRES_PER_PIXEL
    extent = box(geo.PROJ.xmin-150*metres, geo.PROJ.ymin-200*metres,
                 geo.PROJ.xmax+150*metres, geo.PROJ.ymax+100*metres)
    land = unary_union([g for g,_ in geo.read_shapes("land", extent)])
    lakes = [g for g,a in geo.read_shapes("lakes", extent)
             if g.area > 25_000_000 and a.get("featurecla") != "Reservoir"]
    for feature in json.loads((REPORT/'lake_additions.geojson').read_text(encoding='utf-8'))['features']:
        lakes.append(transform_geometry(shape(feature['geometry']), geo.PROJ.xy, interleaved=False))
    land = to_source_geometry(land.difference(unary_union(lakes)))
    rivers = []
    for geometry, attrs in geo.read_shapes("rivers", extent):
        names = " ".join(str(attrs.get(k, "") or "") for k in ("name","name_en","name_alt","name_zh")).lower()
        exact = str(attrs.get("name_en") or attrs.get("name") or "").lower()
        if exact in geo.EXACT_RIVERS | RIVER_ADDITIONS or any(word in names for word in geo.RIVER_WORDS):
            rivers.append((to_source_geometry(geometry), attrs))
    for feature in json.loads((REPORT/'river_additions.geojson').read_text(encoding='utf-8'))['features']:
        geometry=transform_geometry(shape(feature['geometry']),geo.PROJ.xy,interleaved=False)
        rivers.append((to_source_geometry(geometry),feature['properties']))
    for index, (geometry, attrs) in enumerate(rivers):
        if attrs.get('continueRiver'):
            parts = [part for g, a in rivers if
                     str(a.get('name_en') or a.get('name') or '').lower() == attrs['continueRiver'].lower()
                     for part in geo.geometries(g, 'LineString')]
            endpoints = [Point(part.coords[i]) for part in parts for i in (0, -1)]
            start = Point(geometry.coords[0])
            joined = min(endpoints, key=start.distance)
            assert start.distance(joined)*SCALE < 120, attrs['name']+' continuation is too far from river'
            geometry = LineString([joined.coords[0]]+list(geometry.coords))
            rivers[index] = (geometry, attrs)
        if not attrs.get('joinRiver'):
            continue
        target = unary_union([g for g, a in rivers if a is not attrs and attrs['joinRiver'].lower() in
                              str(a.get('name_en') or a.get('name') or '').lower()])
        end = Point(geometry.coords[-1])
        joined = nearest_points(end, target)[1]
        assert end.distance(joined)*SCALE < 40, attrs['name']+' outlet is too far from target river'
        if end.distance(joined) > .0001:
            rivers[index] = (LineString(list(geometry.coords)+[joined.coords[0]]), attrs)
    regions = []
    features = json.loads((ROOT/"maps/terrain_regions.geojson").read_text(encoding="utf-8"))["features"]
    features += json.loads(TERRAIN_ADDITIONS.read_text(encoding="utf-8"))["features"]
    for feature in sorted(features, key=lambda f:f["properties"]["priority"], reverse=True):
        region = transform_geometry(shape(feature["geometry"]), geo.PROJ.xy, interleaved=False)
        if "buffer_km" in feature["properties"]:
            region = region.buffer(feature["properties"]["buffer_km"]*1000)
        regions.append((to_source_geometry(region), feature["properties"]["terrain"], feature['properties'].get('density')))
    return land, eastasia_hydrology.join_seams(sys.modules[__name__], rivers), regions


def terrain_at(col, row, regions):
    point = Point(*world_to_source(*center(col, row)))
    for region, kind, configured_density in regions:
        if not region.covers(point):
            continue
        if kind == 0:
            break
        seed = ((col*73856093)^(row*19349663))&0xffffffff
        density = configured_density if configured_density is not None else {3:68,4:60,5:55,6:72,7:80,8:48,9:90}[kind]
        if seed%100 < density:
            return kind, (col*13+row*17)%(9 if kind == 7 else 6)
        break
    return 0, 255


def make_terrain(land, regions):
    data = np.zeros((GRID_H,GRID_W,12),dtype=np.uint8)
    data[:,:,1] = 255
    data[:,:,4] = 11
    data[:,:,5] = 255
    fractions = np.zeros((GRID_H,GRID_W),dtype=np.float32)
    centres = {}
    # Geometry intersection measures the complete hex, including boundary cells.
    from shapely.geometry import Polygon
    for row in range(GRID_H):
        for col in range(GRID_W):
            x,y = center(col,row)
            sx,sy = world_to_source(x,y)
            centres[col,row] = (sx,sy)
            polygon = Polygon([(sx+dx/SCALE,sy+dy/SCALE) for dx,dy in ((0,-36),(32,-18),(32,18),(0,36),(-32,18),(-32,-18))])
            fractions[row,col] = polygon.intersection(land).area/polygon.area
            if fractions[row,col] <= .5:
                data[row,col,0] = 1
                data[row,col,1] = (col*13+row*7)%7
                continue
            data[row,col,:2] = terrain_at(col, row, regions)
    return data,fractions,centres


def place_cities(data, fractions, centres, land):
    cities = json.loads(SOURCE_CITIES.read_text(encoding="utf-8"))
    used, placements, adjustments = set(), [], []
    candidates = []
    for (col,row),(sx,sy) in centres.items():
        if data[row,col,0] != 1 and land.covers(Point(sx,sy)):
            candidates.append((col,row,sx,sy))
    # Reserve island and close coastal pairs before easy inland placements.
    def difficulty(city):
        nearby = sum((sx-city['x'])**2+(sy-city['y'])**2 < 45**2 for _,_,sx,sy in candidates)
        return nearby, city['tier'], city['id']
    for city in sorted(cities,key=difficulty):
        x,y = city['x'],city['y']
        possible = [p for p in candidates if p[:2] not in used and math.hypot(p[2]-x,p[3]-y) < 38]
        generalized = False
        if possible:
            col,row,sx,sy = min(possible,key=lambda p:(p[2]-x)**2+(p[3]-y)**2)
        else:
            # Small named islands cannot occupy a water-majority cell without explicit generalization.
            col,row = min((p for p in centres if p not in used),key=lambda p:(centres[p][0]-x)**2+(centres[p][1]-y)**2)
            sx,sy = centres[col,row]
            generalized = True
            adjustments.append(dict(name=city['name'],cell=row*GRID_W+col,source_center=[sx,sy],
                                    reason="Named coastal/island settlement retained at this grid resolution"))
        used.add((col,row))
        data[row,col,0:4] = [0,255,0,0]
        distance_km = math.hypot(sx-x,sy-y)*geo.METRES_PER_PIXEL/1000
        if distance_km > 55:
            raise ValueError(f"City displacement exceeds 55 km: {city['name']} ({distance_km:.2f})")
        placements.append({**city,"cell":row*GRID_W+col,"col":col,"row":row,
                           "world_pixel":list(center(col,row)),"displacement_km":round(distance_km,2),
                           "generalized_land":generalized,"city_id":10000+len(placements),
                           "building_level":2 if city['tier']==1 else 1 if city['tier']==2 else 0})
    return sorted(placements,key=lambda c:c['city_id']),adjustments


def add_rivers(data,centres,rivers):
    barrier = unary_union([g for g,_ in rivers])
    for row in range(GRID_H):
        for col in range(GRID_W):
            if data[row,col,0] == 1:
                continue
            for direction in range(3):
                nc,nr = neighbor(col,row,direction)
                if 0 <= nc < GRID_W and 0 <= nr < GRID_H and data[nr,nc,0] != 1:
                    if LineString([centres[col,row],centres[nc,nr]]).intersects(barrier):
                        data[row,col,8] |= 1<<direction
                        data[nr,nc,8] |= 1<<(direction+3)


def make_crossings(data,rivers=None):
    def project(lon,lat):
        x,y=geo.PROJ.xy(lon,lat)
        return ((x-geo.PROJ.xmin)*geo.WIDTH/(geo.PROJ.xmax-geo.PROJ.xmin)*SCALE,
                (geo.PROJ.ymax-y)*geo.HEIGHT/(geo.PROJ.ymax-geo.PROJ.ymin)*SCALE)
    if rivers is None:
        rivers=geography()[1]
    paths={}
    for name,word in (('Yangtze','yangtze'),('Yellow','yellow')):
        pieces=[g for g,a in rivers if word in ' '.join(str(a.get(k,'') or '') for k in ('name','name_en','name_alt')).lower()]
        if not pieces:
            raise ValueError('Missing main river geometry: '+name)
        paths[name]=unary_union(pieces)
    return eastasia_crossings.build(REPORT/'bridge_plan.json',data,project,paths)


def make_land_mask(land,placements):
    mask = Image.new("L",(IMAGE_W,IMAGE_H),0)
    draw = ImageDraw.Draw(mask)
    for polygon in geo.geometries(land,"Polygon"):
        draw.polygon([source_to_image(x,y) for x,y in polygon.exterior.coords],fill=255)
        for interior in polygon.interiors:
            draw.polygon([source_to_image(x,y) for x,y in interior.coords],fill=0)
    for city in placements:
        x,y = city['world_pixel']
        ix,iy = x-IMAGE_LEFT,y-IMAGE_TOP
        # Keep the city sprite's anchor on visible dry land after snapping to the grid.
        if mask.getpixel((int(ix),int(iy))) < 128 or city['generalized_land']:
            draw.ellipse((ix-17,iy-14,ix+17,iy+14),fill=255)
            city['visual_land_patch'] = True
        else:
            city['visual_land_patch'] = False
    return mask


def background(land,rivers,placements,bridges,mask):
    # The renderer puts terrain and scenario ownership BELOW these map tiles.
    sea = eastasia_water.background(sys.modules[__name__], land, rivers, placements, mask)
    atlas=Image.open(BASE/'client/images/buildings.png').convert('RGBA')
    sprites=json.loads((BASE/'client/manifest.json').read_text(encoding='utf-8'))['atlases']['buildings']
    for bridge in bridges:
        sprite=sprites[f"bridge_{bridge['style']}_{bridge['direction']+1}.png"]
        sx,sy,w,h=[round(sprite[k]) for k in ('x','y','w','h')]
        x,y=bridge['world_pixel']
        sea.alpha_composite(atlas.crop((sx,sy,sx+w,sy+h)),
                            (round(x-sprite['refx']-IMAGE_LEFT),round(y-sprite['refy']-IMAGE_TOP)))
    return sea,mask


def map_resources(image,data,placements):
    manifest = json.loads((BASE/'client/manifest.json').read_text(encoding='utf-8'))
    terrain_defs = list(ET.parse(BASE/'original/def_mapterrain.xml').getroot())
    overlays=[]
    for row in range(GRID_H):
        for col in range(GRID_W):
            cell=data[row,col]
            kind,index=int(cell[0]),int(cell[1])
            x,y=center(col,row)
            for layer, (kind,index,dx,dy) in enumerate((tuple(map(int,cell[:4])),tuple(map(int,cell[4:8])))):
                if kind in (0,11) or (layer==0 and kind==1) or index==255:continue
                entry=next(e for e in terrain_defs[kind] if int(e.get('idx'))==index)
                dx,dy=(v if v<128 else v-256 for v in (dx,dy))
                overlays.append(dict(cell=row*GRID_W+col,layer=layer,seaForeground=kind==1,
                                     image=entry.get('image'),x=x+dx,y=y+dy))
    eastasia_relief.apply(sys.modules[__name__],manifest,overlays,data)
    tiles=[]
    folder=OUT/'client/maps/eastasia'
    folder.mkdir(parents=True,exist_ok=True)
    for top in range(0,image.height,1024):
        for left in range(0,image.width,1024):
            # Overlap adjacent tiles to avoid fractional-zoom hairlines across open sea.
            tile=image.crop((left,top,min(left+1026,image.width),min(top+1026,image.height)))
            filename=f'{left}_{top}.png'
            tile.save(folder/filename,compress_level=6)
            tiles.append(dict(x=left+IMAGE_LEFT,y=top+IMAGE_TOP,width=tile.width,height=tile.height,file=f'maps/eastasia/{filename}'))
    labels=json.loads((REPORT/'geographic_labels.json').read_text(encoding='utf-8'))
    translations=json.loads((REPORT/'city_names.json').read_text(encoding='utf-8'))
    city_names={str(c['cell']):dict(name=c['name'],zh=c['name'],en=translations['byCityId'].get(str(c['city_id']),translations['names'][c['name']])) for c in placements}
    manifest['maps']['eastasia']=dict(width=image.width,height=image.height,pixelRatio=1,tiles=tiles,overlays=overlays,labels=labels,cityNames=city_names)
    manifest['strings']['text_conquest_age_7']='东亚地图检查'
    manifest['strategic']['conquest7']=dict(name='conquest 7',file='conquest7.btl',map='3',hide='0',centerx='284',centery='160',scale='1',age='',agex='0',agey='0')
    write_json(OUT/'client/manifest.json',manifest)
    return manifest,overlays


def make_preview(data,placements,image,facilities,bridges):
    maps=ET.parse(BASE/'original/def_map.xml').getroot()
    for entry in list(maps):
        if entry.get('name')=='eastasia':maps.remove(entry)
    ET.SubElement(maps,'map',id='3',name='eastasia',continent='eastasia',file='eastasia.bin',
                  w=str(IMAGE_W),h=str(IMAGE_H),textpos='maptextpos_eastasia.xml')
    write_xml('def_map.xml',maps)
    textpos=ET.Element('Elements')
    for label in json.loads((OUT/'client/manifest.json').read_text(encoding='utf-8'))['maps']['eastasia']['labels']:
        ET.SubElement(textpos,'Element',**{key:str(label[key]) for key in
                      ('index','libName','colorAlphaPercent','tx','ty','left','top','width','height')})
    write_xml('maptextpos_eastasia.xml',textpos)
    names=ET.parse(BASE/'original/def_areaname.xml').getroot()
    city_ids={str(city['city_id']) for city in placements}
    for entry in list(names):
        if entry.get('id') in city_ids:names.remove(entry)
    for city in placements:
        ET.SubElement(names,'areaname',id=str(city['city_id']),name=city['name'])
    write_xml('def_areaname.xml',names)
    btl=(BASE/'original/conquest1.btl').read_bytes()
    header=list(struct.unpack_from('<18i',btl))
    count=header[6]
    factions=[btl[72+i*180:72+(i+1)*180] for i in range(count)]
    def country(raw):return raw[24:40].split(b'\0')[0].decode()
    factions=[bytearray(next(f for f in factions if country(f)==name)) for name in ('fra','gbr')]
    for i,faction in enumerate(factions):
        struct.pack_into('<6i',faction,0,9999,9999,9999,1,i+1,0)
        struct.pack_into('<f',faction,56,1)
    owners=np.full((GRID_H,GRID_W),255,dtype=np.uint8)
    by_name={c['name']:c for c in placements}
    city_cells={c['cell'] for c in placements} | {f['cell'] for f in facilities}
    start_cells=[]
    # Unowned cities keep this inspection scene free of fixed territorial districts.
    for owner,name in enumerate(('北京','东京')):
        city=by_name[name]
        nc,nr=next((nc,nr) for nc,nr in (neighbor(city['col'],city['row'],d) for d in range(6))
                   if 0<=nc<GRID_W and 0<=nr<GRID_H and data[nr,nc,0]!=1 and nr*GRID_W+nc not in city_cells)
        owners[nr,nc]=owner
        start_cells.append(nr*GRID_W+nc)
    sites=[]
    for city in placements:
        site=bytearray(16)
        struct.pack_into('<hh',site,0,city['cell'],city['city_id'])
        site[4],site[5],site[8],site[9],site[11]=city.get('building_kind',0),city['building_level'],255,255,255
        site[7]=city['building_style']
        sites.append(site)
    for facility in facilities:
        site=bytearray(16)
        struct.pack_into('<hh',site,0,facility['cell'],-1)
        site[4],site[5],site[8],site[9],site[11]=facility['building_kind'],facility['level'],255,255,255
        site[7]=facility['building_style']
        sites.append(site)
    by_cell={struct.unpack_from('<h',site)[0]:site for site in sites}
    for bridge in bridges:
        for cell,direction in ((bridge['cell'],bridge['direction']),(bridge['other'],bridge['direction']+3)):
            if cell not in by_cell:
                site=bytearray(16)
                struct.pack_into('<hh',site,0,cell,-1)
                site[4],site[8],site[9],site[11]=255,255,255,255
                sites.append(site)
                by_cell[cell]=site
            by_cell[cell][11+bridge['style']] |= 1<<direction
    start_army=72+180*count+header[4]*header[5]+header[7]*16
    template=next(btl[start_army+i*32:start_army+(i+1)*32] for i in range(header[8]) if btl[start_army+i*32+2]==0)
    armies=[]
    for cell in start_cells:
        army=bytearray(template)
        struct.pack_into('<h',army,0,cell)
        struct.pack_into('<h',army,16,-1)
        army[18],army[19],army[23],army[26],army[27]=0,0,0,255,0
        armies.append(army)
    header[1:11]=[3,0,0,GRID_W,GRID_H,2,len(sites),len(armies),0,0]
    header[11:18]=[1,999,0,0,1,0,0]
    output=struct.pack('<18i',*header)+b''.join(factions)+owners.tobytes()+b''.join(sites)+b''.join(armies)
    (OUT/'original/conquest7.btl').write_bytes(output)
    battles=ET.parse(BASE/'original/def_battlelist.xml').getroot()
    ET.SubElement(battles,'battle',name='conquest 7',file='conquest7.btl',map='3',hide='0',centerx='284',centery='160',scale='1',age='',agex='0',agey='0',citynames='conquest7.names.xml')
    write_xml('def_battlelist.xml',battles)
    if not (OUT/'original/conquest7.names.xml').exists():
        write_xml('conquest7.names.xml',ET.Element('citynames'))
    text=(BASE/'original/stringtable_cn.ini').read_text(encoding='utf-8-sig')
    text+='\ntext_conquest_age_7=东亚地图检查\ndesc_conquest 7=东亚底图与城市落格检查，非历史剧本。\n'
    (OUT/'original/stringtable_cn.ini').write_text(text,encoding='utf-8')
    catalog=json.loads((BASE/'legacy/catalog.json').read_text(encoding='utf-8'))
    for name in catalog['source_sha256']:
        if (OUT/'original'/name).exists():catalog['source_sha256'][name]=sha((OUT/'original'/name).read_bytes())
    write_json(OUT/'legacy/catalog.json',catalog)
    pages=json.loads((ROOT/'rewrite/Ew4.Web/wwwroot/conquest-pages.json').read_text(encoding='utf-8'))
    thumb=image.copy();thumb.thumbnail((568,320))
    thumb.save(OUT/'client/maps/eastasia/selection.png')
    slot=dict(scenario='conquest7',art='maps/eastasia/selection.png',labels=['东亚','地图检查'])
    empty=next((p for p in pages['pages'] if any(s is None for s in p['slots'])),None)
    if empty is None:pages['pages'].append(dict(id='eastasia-preview',slots=[slot]+[None]*5))
    else:empty['slots'][empty['slots'].index(None)]=slot
    write_json(OUT/'frontend/conquest-pages.json',pages)
    city_names=json.loads((ROOT/'rewrite/Ew4.Web/wwwroot/city-names.json').read_text(encoding='utf-8'))
    for c in placements:city_names[c['name']]=c['name']
    write_json(OUT/'frontend/city-names.json',city_names)


def checks(data,placements,fractions,mask):
    errors=[]
    expected=len(json.loads(SOURCE_CITIES.read_text(encoding='utf-8')))+len(json.loads((REPORT/'map_additions.json').read_text(encoding='utf-8'))['cities'])
    if len(placements)!=expected or len({c['cell'] for c in placements})!=expected:errors.append('City count or unique cell assignment')
    river_edges=0
    for row in range(GRID_H):
        for col in range(GRID_W):
            for direction in range(6):
                if int(data[row,col,8])&(1<<direction):
                    river_edges+=1
                    nc,nr=neighbor(col,row,direction)
                    if not(0<=nc<GRID_W and 0<=nr<GRID_H and int(data[nr,nc,8])&(1<<((direction+3)%6))):errors.append('River reciprocity')
    for c in placements:
        x,y=c['world_pixel']
        if data[c['row'],c['col'],0]==1 or mask.getpixel((x-IMAGE_LEFT,y-IMAGE_TOP))<128:errors.append('City anchor in water: '+c['name'])
    binary=(OUT/'original/eastasia.bin').read_bytes()
    decoded=TerrainMap.from_bytes(binary)
    if decoded.to_bytes()!=binary:errors.append('Binary round trip')
    report=dict(format='YSAE/1; 12-byte cells',width=GRID_W,height=GRID_H,cells=GRID_W*GRID_H,bytes=len(binary),
                city_count=len(placements),max_city_displacement_km=max(c['displacement_km'] for c in placements),
                generalized_cities=[c['name'] for c in placements if c['generalized_land']],
                visual_city_patches=[c['name'] for c in placements if c['visual_land_patch']],
                terrain_counts=dict(Counter(int(k) for k in data[:,:,0].flat)),directed_river_edges=river_edges,
                terrain_additions_sha256=sha(TERRAIN_ADDITIONS.read_bytes()),
                source_city_sha256=sha(SOURCE_CITIES.read_bytes()),runtime_tested=False,errors=errors)
    write_json(REPORT/'generation.json',report)
    if errors:raise ValueError(errors)
    return report


def add_saved_cities():
    binary = OUT/'original/eastasia.bin'
    before = binary.read_bytes()
    data = np.frombuffer(before[16:], dtype=np.uint8).reshape((GRID_H, GRID_W, 12)).copy()
    placements = json.loads((REPORT/'cities.json').read_text(encoding='utf-8'))
    old_ids = {c['city_id'] for c in placements}
    old_cities = json.loads(json.dumps(placements))
    facilities = json.loads((REPORT/'facilities.json').read_text(encoding='utf-8'))
    bridges = json.loads((REPORT/'bridges.json').read_text(encoding='utf-8'))
    placements = eastasia_additions.apply(sys.modules[__name__], data, placements, geography()[0])
    added = [c for c in placements if c['city_id'] not in old_ids]
    if not added:
        print(json.dumps(dict(addedCities=0, errors=0)))
        return
    occupied = {f['cell'] for f in facilities} | {b[k] for b in bridges for k in ('cell', 'other')}
    if any(c['cell'] in occupied for c in added):
        raise ValueError('Additional city intersects an existing facility or bridge')
    plan = json.loads((REPORT/'settlement_plan.json').read_text(encoding='utf-8'))
    translations = json.loads((REPORT/'city_names.json').read_text(encoding='utf-8'))
    manifest_path = OUT/'client/manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    template_path = OUT/'eastasia.map.json'
    template = json.loads(template_path.read_text(encoding='utf-8'))
    added_cells = {c['cell'] for c in added}
    for city in added:
        city.update(building_level=plan['cityLevelByTier'][str(city['tier'])], building_style=2,
                    visual_land_patch=False)
        c = template['cells'][city['cell']]
        c.update(name=city['name'], buildingKind='city', buildingLevel=city['building_level'], defenseTerrain='land')
        c['terrain'].update(sea=False, terrainCost=3)
        manifest['maps']['eastasia']['cityNames'][str(city['cell'])] = dict(
            name=city['name'], zh=city['name'], en=translations['names'][city['name']])
    assert placements[:len(old_cities)] == old_cities, 'Existing city records changed'
    changed = np.flatnonzero(np.any(data.reshape((-1, 12)) != np.frombuffer(before[16:], dtype=np.uint8).reshape((-1, 12)), axis=1))
    assert set(map(int, changed)) <= added_cells, 'Unrelated terrain changed'
    manifest['maps']['eastasia']['overlays'] = [o for o in manifest['maps']['eastasia']['overlays'] if o['cell'] not in added_cells]
    write_json(manifest_path, manifest)
    write_json(template_path, template)
    binary.write_bytes(before[:16]+data.tobytes())
    for folder in (REPORT, OUT/'client/maps/eastasia'):
        write_json(folder/'cities.json', placements)
    make_preview(data, placements, Image.open(OUT/'client/maps/eastasia/selection.png').copy(), facilities, bridges)
    settlements = json.loads((REPORT/'settlements.json').read_text(encoding='utf-8'))
    settlements.update(cities=len(placements), city_levels=dict(sorted(Counter(c['building_level'] for c in placements).items())))
    write_json(REPORT/'settlements.json', settlements)
    checks(data, placements, np.load(REPORT/'land_fraction.npy'), Image.open(REPORT/'land_mask.png'))
    report = dict(addedCities=added, existingCitiesUnchanged=True, facilitiesUnchanged=len(facilities),
                  bridgesUnchanged=len(bridges), changedTerrainCells=list(map(int, changed)), errors=0)
    write_json(REPORT/'city-additions-verification.json', report)
    print(json.dumps(dict(addedCities=len(added), cities=len(placements), errors=0)))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--render-only',action='store_true',help='Reuse existing terrain and city placements; rebuild display resources only')
    parser.add_argument('--settlements-only',action='store_true',help='Reuse existing geography and art; update initial city levels and economic sites')
    parser.add_argument('--additions-only',action='store_true',help='Append requested cities, preserving existing art, city records and facilities')
    args=parser.parse_args()
    for folder in (OUT/'original',OUT/'client',OUT/'legacy',OUT/'frontend',REPORT):folder.mkdir(parents=True,exist_ok=True)
    if args.additions_only:
        add_saved_cities()
        return
    if args.settlements_only:
        data=np.frombuffer((OUT/'original/eastasia.bin').read_bytes()[16:],dtype=np.uint8).reshape((GRID_H,GRID_W,12)).copy()
        placements=json.loads((REPORT/'cities.json').read_text(encoding='utf-8'))
        required={c['name'] for c in json.loads((REPORT/'map_additions.json').read_text(encoding='utf-8'))['cities']}
        if not required.issubset({c['name'] for c in placements}):
            raise ValueError('City additions require --render-only before --settlements-only')
        facilities,settlements=eastasia_settlements.build(REPORT/'settlement_plan.json',placements,data,Image.open(REPORT/'land_mask.png'))
        bridges,bridge_report=make_crossings(data)
        make_preview(data,placements,Image.open(OUT/'client/maps/eastasia/selection.png').copy(),facilities,bridges)
        for folder in (REPORT,OUT/'client/maps/eastasia'):
            write_json(folder/'cities.json',placements)
            write_json(folder/'facilities.json',facilities)
        write_json(REPORT/'settlements.json',settlements)
        write_json(REPORT/'bridges.json',bridges)
        write_json(REPORT/'bridge-verification.json',bridge_report)
        print(json.dumps(settlements,ensure_ascii=False),flush=True)
        return
    print('Loading cached geographic sources',flush=True)
    land,rivers,regions=geography()
    if args.render_only:
        data=np.frombuffer((OUT/'original/eastasia.bin').read_bytes()[16:],dtype=np.uint8).reshape((GRID_H,GRID_W,12)).copy()
        placements=json.loads((REPORT/'cities.json').read_text(encoding='utf-8'))
        adjustments=json.loads((REPORT/'geographic_adjustments.json').read_text(encoding='utf-8'))
        fractions=np.load(REPORT/'land_fraction.npy')
        placements=eastasia_additions.apply(sys.modules[__name__],data,placements,land)
        centres={(col,row):world_to_source(*center(col,row)) for row in range(GRID_H) for col in range(GRID_W)}
        data[:,:,8]=0
        add_rivers(data,centres,rivers)
        (OUT/'original/eastasia.bin').write_bytes(struct.pack('<4s3i',b'YSAE',1,GRID_W,GRID_H)+data.tobytes())
    else:
        print('Measuring 21488 hexagons and terrain',flush=True)
        data,fractions,centres=make_terrain(land,regions)
        placements,adjustments=place_cities(data,fractions,centres,land)
        placements=eastasia_additions.apply(sys.modules[__name__],data,placements,land)
        add_rivers(data,centres,rivers)
        write_json(REPORT/'cities.json',placements)
        binary=struct.pack('<4s3i',b'YSAE',1,GRID_W,GRID_H)+data.tobytes()
        (OUT/'original/eastasia.bin').write_bytes(binary)
    mask=make_land_mask(land,placements)
    facilities,settlements=eastasia_settlements.build(REPORT/'settlement_plan.json',placements,data,mask)
    eastasia_relief.landmarks(sys.modules[__name__],data,placements,facilities,mask)
    bridges,bridge_report=make_crossings(data,rivers)
    labels=eastasia_cartography.labels(sys.modules[__name__],mask,placements,facilities)
    eastasia_cartography.waves(sys.modules[__name__],data,mask,placements,facilities,labels)
    (OUT/'original/eastasia.bin').write_bytes(struct.pack('<4s3i',b'YSAE',1,GRID_W,GRID_H)+data.tobytes())
    write_json(REPORT/'bridges.json',bridges)
    write_json(REPORT/'bridge-verification.json',bridge_report)
    print('Rendering coast, river, bridges and city anchors',flush=True)
    image,mask=background(land,rivers,placements,bridges,mask)
    manifest,overlays=map_resources(image,data,placements)
    write_json(REPORT/'cities.json',placements)
    write_json(OUT/'client/maps/eastasia/cities.json',placements)
    write_json(REPORT/'geographic_adjustments.json',adjustments)
    np.save(REPORT/'land_fraction.npy',fractions)
    mask.save(REPORT/'land_mask.png')
    preview=geo.land_texture((IMAGE_W,IMAGE_H)).convert('RGBA')
    preview.alpha_composite(image)
    # Actual terrain sprites, not the old generator's displaced diagnostic picture.
    atlas=Image.open(OUT/'client/images/tiles.png').convert('RGBA')
    sheet=manifest['atlases']['tiles']
    for overlay in overlays:
        sprite=sheet[overlay['image']]
        sx,sy,w,h=[round(sprite[k]) for k in ('x','y','w','h')]
        preview.alpha_composite(atlas.crop((sx,sy,sx+w,sy+h)),(round(overlay['x']-sprite['refx']-IMAGE_LEFT),round(overlay['y']-sprite['refy']-IMAGE_TOP)))
    for folder in (REPORT,OUT/'client/maps/eastasia'):
        write_json(folder/'cities.json',placements)
        write_json(folder/'facilities.json',facilities)
    write_json(REPORT/'settlements.json',settlements)
    make_preview(data,placements,preview,facilities,bridges)
    preview=preview.convert('RGB');preview.thumbnail((3000,2200))
    preview.save(REPORT/'eastasia_game_base_small.jpg',quality=83,optimize=True,progressive=True)
    preview.thumbnail((1600,1200))
    preview.save(REPORT/'eastasia_terrain_overview.jpg',quality=78,optimize=True,progressive=True)
    report=checks(data,placements,fractions,mask)
    print(json.dumps({k:report[k] for k in ('cells','bytes','city_count','max_city_displacement_km','generalized_cities','directed_river_edges','errors')},ensure_ascii=False),flush=True)


if __name__=='__main__':main()
