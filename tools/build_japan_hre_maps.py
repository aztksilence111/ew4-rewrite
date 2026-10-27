"""Build neutral, spacious hex-map source assets without changing installed maps."""

from __future__ import annotations

import argparse
import base64
from collections import Counter, deque
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path.home() / '.codex/tools/ew4-inspect-deps'))
import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter
from shapely import contains_xy, intersection, polygons, prepare, transform
from shapely.geometry import LineString, Point, Polygon, box
from shapely.ops import nearest_points, unary_union
from shapely.strtree import STRtree

import build_japan_hre_plan as plan
import dual_map_relief
import dual_map_cartography
import dual_map_hydrography
import japan_sengoku
from dual_map_city_levels import city_level
from eastasia_settlements import hex_distance, neighbors, ocean_cells
from ew4_formats import TerrainMap

ROOT = Path(__file__).resolve().parents[1]
CONFIG_PATH = ROOT / 'maps/dual_map_config.json'
BASE = ROOT / 'rewrite/data'
ART = json.loads((BASE / 'client/manifest.json').read_text(encoding='utf-8'))
TERRAIN_DEFS = None


def write_json(path, value, compact=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=None if compact else 2,
                               separators=(',', ':') if compact else None) + '\n', encoding='utf-8')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def geodesic_distance(lon,lat,other_lon,other_lat):
    a,b,c,d=map(np.radians,(lon,lat,other_lon,other_lat))
    h=np.sin((d-b)/2)**2+np.cos(b)*np.cos(d)*np.sin((c-a)/2)**2
    return 6371.0088*2*np.arcsin(np.sqrt(np.clip(h,0,1)))


def required_spacing(a,b,policy):
    if 'port' in (a,b):return policy['portMinimumHexDistance']
    if a==b=='city':return policy['cityMinimumHexDistance']
    return policy['minimumHexDistance']


def site_spacing(a,b,policy):
    if 'port' in (a['kind'],b['kind']):
        return required_spacing(a['kind'],b['kind'],policy)
    return max(required_spacing(a['kind'],b['kind'],policy),a.get('minimumSpacing',0),b.get('minimumSpacing',0))


def source_sites(key, config):
    rows = json.loads((plan.OUT/(key+'_points.json')).read_text(encoding='utf-8'))['records']
    excluded = set(config.get('excludedSiteNames', []))
    assert excluded.issubset({r['name'] for r in rows}), 'Unknown excluded site'
    override_path = ROOT/'maps'/key/'sengoku_plan.json'
    revision = json.loads(override_path.read_text(encoding='utf-8')) if override_path.exists() else {}
    overrides = revision.get('siteOverrides', {})
    result = [dict(r, **overrides.get(r['id'], {})) for r in rows if r['name'] not in excluded]
    result.extend(revision.get('additionalSites', []))
    if key == 'japan' and excluded:
        for record in result:
            if record['group'] == '北海道与北方四岛':
                record['group'] = '北海道'
    if key == 'japan' and override_path.exists():
        for record in result:
            record.update(japan_sengoku.locate(record['lon'], record['lat']))
    return result


class MapView:
    def __init__(self, key, config):
        self.key, self.config = key, config
        self.reference = plan.View(key)
        self.width = math.ceil(config['approximateFrameWidthKm'] / config['gridStepKm']) + 1
        self.world_width = (self.width - .5) * 64
        self.world_height = self.world_width * self.reference.height / self.reference.width
        self.height = math.ceil(self.world_height / 54) + 1
        self.world_height = (self.height - 1) * 54
        self.scale_x = self.world_width / self.reference.width
        self.scale_y = self.world_height / self.reference.height
        self.km_per_pixel = config['approximateFrameWidthKm'] / self.world_width
        self.origin_x = self.origin_y = 0
        if window := config.get('gridWindow'):
            col, row, width, height = window
            assert row % 2 == 0 and col >= 0 and row >= 0
            assert col+width <= self.width and row+height <= self.height
            self.origin_x, self.origin_y = col*64, row*54
            self.width, self.height = width, height
            self.world_width, self.world_height = (width-.5)*64, (height-1)*54
        self.clip = box(-32, -54, self.world_width + 32, self.world_height + 54)
        rows, cols = np.indices((self.height, self.width))
        self.x = (cols * 64 + (rows % 2) * 32).ravel().astype(float)
        self.y = (rows * 54 - 18).ravel().astype(float)
        self.points = np.column_stack((self.x, self.y))

    def xy(self, lon, lat):
        x, y = self.reference.xy(lon, lat)
        return x * self.scale_x-self.origin_x, y * self.scale_y-self.origin_y

    def project(self, geometry):
        return transform(geometry, self.xy, interleaved=False)

    def lonlat(self,x,y):
        reference=self.reference
        x,y=(np.asarray(x)+self.origin_x)/self.scale_x,(np.asarray(y)+self.origin_y)/self.scale_y
        if self.key=='japan':
            left,top,right,bottom=reference.bounds
            a=x/reference.width*(right-left)+left
            b=y/reference.height*(bottom-top)+top
            c,s=math.cos(math.pi/12),math.sin(math.pi/12)
            u,v=c*a+s*b,-s*a+c*b
            return (u+reference.cx-403)/50.1+130,30+(1054-v-reference.cy)/60
        left,bottom,right,top=reference.bounds
        a=left+x/reference.width*(right-left)
        b=top-y/reference.height*(top-bottom)
        rho=np.hypot(a,reference.rho0-b)
        theta=np.arctan2(a,reference.rho0-b)
        return np.degrees(reference.lon0+theta/reference.n),np.degrees(2*np.arctan((6371008.8*reference.f/rho)**(1/reference.n))-np.pi/2)

    def nearest(self, lon, lat, mask=None):
        x, y = self.xy(lon, lat)
        indices = np.arange(len(self.x)) if mask is None else np.flatnonzero(mask)
        return int(indices[np.argmin((self.x[indices]-x)**2+(self.y[indices]-y)**2)])


def geography(view):
    land_geo = unary_union([g for g, _ in plan.read_source('land', view.reference)])
    if regions := view.config.get('excludedIslandRegions'):
        pieces = list(plan.parts(land_geo, 'Polygon'))
        protected = {min(range(len(pieces)), key=lambda i:pieces[i].distance(Point(*p)))
                     for p in view.config['protectedMainlandAnchors']}
        excluded = unary_union([box(*r) for r in regions])
        land_geo = unary_union([g for i,g in enumerate(pieces) if i in protected or not g.intersects(excluded)])
    lakes = unary_union([g for g, a in plan.read_source('lakes', view.reference)
                         if a.get('featurecla') != 'Reservoir' and g.area > .0003])
    land = view.project(land_geo.difference(lakes)).intersection(view.clip).simplify(.25, preserve_topology=True)
    components = list(plan.parts(land, 'Polygon'))
    tree = STRtree(components)
    prepare(land)
    rivers=[]
    for geometry,name in dual_map_hydrography.load(view.key):
        g=view.project(geometry).intersection(view.clip)
        if not g.is_empty:rivers.append((g,name))
    return land, components, tree, rivers


def make_grid(view, land, components, tree):
    count = view.width * view.height
    offsets = np.array([[0,-36],[32,-18],[32,18],[0,36],[-32,18],[-32,-18]])
    hexes = polygons(view.points[:, None, :] + offsets[None, :, :])
    fractions = np.zeros(count)
    # Full polygons are measured on both sides of the frame, including boundary hexes.
    inside = contains_xy(land, view.x, view.y)
    boundary = land.boundary.buffer(42)
    coastal = contains_xy(boundary, view.x, view.y)
    fractions[inside & ~coastal] = 1
    indices = np.flatnonzero(coastal)
    for chunk in np.array_split(indices, max(1, math.ceil(len(indices)/500))):
        if len(chunk):
            fractions[chunk] = np.array([g.area for g in intersection(hexes[chunk], land)]) / 3456
    dry = inside & (fractions > .5)
    component = np.full(count, -1, dtype=int)
    for index, geometry in enumerate(components):
        minx, miny, maxx, maxy = geometry.bounds
        selected = np.flatnonzero(dry & (view.x>=minx)&(view.x<=maxx)&(view.y>=miny)&(view.y<=maxy))
        if len(selected):
            component[selected[contains_xy(geometry, view.x[selected], view.y[selected])]] = index
    changes = []
    forced = set()
    sources=source_sites(view.key,view.config)
    anchors=view.config['islands']+[[s['name'],s['lon'],s['lat']] for s in sources if s['kind']!='port']
    for name, lon, lat in anchors:
        x, y = view.xy(lon, lat)
        island = int(tree.nearest(Point(x, y)))
        options = np.flatnonzero((component == island) & dry)
        if len(options):
            continue
        distance = (view.x-x)**2+(view.y-y)**2
        for cell in np.argsort(distance)[:50]:
            if hexes[cell].intersects(components[island]):
                dry[cell], component[cell] = True, island
                forced.add(int(cell))
                changes.append(dict(name=name, cell=int(cell), kind='retain-small-island', land_fraction=round(float(fractions[cell]),4)))
                break
        else:
            raise ValueError('Cannot retain island: ' + name)
    # Separate genuinely distinct land masses before buildings reserve coast cells.
    for _ in range(3):
        conflicts = []
        for cell in np.flatnonzero(dry):
            for other in neighbors(int(cell), view.width, view.height):
                if other <= cell or not dry[other] or component[other] == component[cell]:
                    continue
                candidates = [c for c in (int(cell), other) if c not in forced]
                if not candidates:
                    raise ValueError('Two protected islands share a land edge')
                remove = min(candidates, key=lambda c: fractions[c])
                conflicts.append(remove)
        if not conflicts:
            break
        for cell in sorted(set(conflicts)):
            dry[cell] = False
            changes.append(dict(cell=cell, kind='preserve-real-water-between-land-masses'))
    for name, lon, lat in view.config['waterControls']:
        cell = view.nearest(lon, lat)
        if dry[cell]:
            water = np.flatnonzero(~inside)
            cell = int(water[np.argmin((view.x[water]-view.xy(lon,lat)[0])**2+(view.y[water]-view.xy(lon,lat)[1])**2)])
        assert not dry[cell], name
    data = np.zeros((count,12), dtype=np.uint8)
    data[:,1] = 255
    data[:,4:6] = [11,255]
    data[~dry,0] = 1
    data[~dry,1] = np.flatnonzero(~dry) % 7
    return data, dry, component, fractions, changes


def terrain(view, data, dry,land,rivers=()):
    cells = np.arange(len(dry))
    seed = ((cells % view.width) * 73856093) ^ ((cells // view.width) * 19349663)
    relief,evidence=dual_map_relief.classify(view,data,dry,land,rivers)
    passes = []
    for name, lon, lat in view.config['passes']:
        cell = view.nearest(lon,lat,dry)
        # A pass lowers one traversal cell, not a seven-cell circular plain.
        data[cell,0:2],relief[cell]=[0,255],''
        passes.append(dict(name=name,cell=cell,world_pixel=[view.x[cell],view.y[cell]]))
    forest = dry & (data[:,0] == 0) & (seed % 100 < (24 if view.key == 'japan' else 14))
    if view.config.get('forestMode') == 'none-until-landcover-reference':
        forest[:] = False
    data[forest,0] = 6
    selected = dry & (data[:,0] != 0)
    data[selected,1] = seed[selected] % 6
    if view.config.get('terrainAppearance') in ('native-height-hierarchy','native-range-bands'):
        for sample in evidence['cells']:
            cell = sample['cell']
            if data[cell,0] == 8 and view.config['terrainAppearance']=='native-height-hierarchy':
                data[cell,1] = 0 if sample['river_valley'] or sample['near_relief_m'] < 280 else 1
            elif data[cell,0] == 9 and view.config['terrainAppearance']=='native-height-hierarchy':
                data[cell,1] = 1 if relief[cell] == 'low' else 0
        if view.config['terrainAppearance']=='native-range-bands':
            dual_map_cartography.materials(data,evidence,view.width,view.height)
    return relief, evidence, passes


def allocate(view, data, dry, component, components, tree, fractions, policy):
    source = source_sites(view.key,view.config)
    requests, adjustments = [], []
    ocean = ocean_cells(data.reshape(view.height,view.width,12))
    natural_land=unary_union(components)
    prepare(natural_land)
    # Keep the berth anchor clear of the shoreline after raster downsampling.
    water_centers=~contains_xy(natural_land.buffer(8),view.x,view.y)
    near_land = np.zeros(len(dry),dtype=bool)
    land_neighbors = np.zeros(len(dry),dtype=np.int16)
    for cell in np.flatnonzero(dry):
        for other in neighbors(int(cell),view.width,view.height):
            near_land[other] = True
            land_neighbors[other] += 1
    near_land_two=near_land.copy()
    for cell in np.flatnonzero(near_land):
        for other in neighbors(int(cell),view.width,view.height):near_land_two[other]=True
    indices = np.arange(len(dry))
    cell_lon,cell_lat=view.lonlat(view.x,view.y)
    inner = (view.x>=0)&(view.x<view.world_width)&(view.y>=0)&(view.y<view.world_height)
    for record in source:
        record=dict(record)
        x, y = map(float,view.xy(record['lon'],record['lat']))
        island = int(tree.nearest(Point(x,y)))
        allowed = ((~dry & near_land & water_centers & np.isin(indices,list(ocean))) if record['kind']=='port' else dry & (component==island)) & inner
        distance = geodesic_distance(record['lon'],record['lat'],cell_lon,cell_lat)
        maximum_shift = policy['portMaximumShiftKm'] if record['kind']=='port' else policy['cityMaximumShiftKm'] if record['kind']=='city' else policy['maximumShiftKm']
        candidates = np.flatnonzero(allowed & (distance<=maximum_shift))
        if view.key == 'japan' and record.get('province') and record['kind'] == 'city':
            features, geometries, _ = japan_sengoku.layers()['kuni_polygons']
            province = next(g for f,g in zip(features,geometries) if f['properties']['province']==record['province'])
            same_province = contains_xy(province, cell_lon[candidates], cell_lat[candidates])
            if np.any(same_province):
                candidates = candidates[same_province]
        if view.key=='hre' and record['name']=='安特卫普河口泊地':
            # The Scheldt is narrower than one hex; preserve the real berth location.
            record['river_berth']=True
            candidates=np.flatnonzero(inner & (distance<=policy['cityMaximumShiftKm']))
        if record['kind'] not in ('port','city') and np.any(land_neighbors[candidates]>=3):
            candidates=candidates[land_neighbors[candidates]>=3]
        if not len(candidates):
            if record['kind']=='port':
                candidates=np.flatnonzero(~dry & near_land_two & water_centers & np.isin(indices,list(ocean)) & inner & (distance<=maximum_shift))
            if record['kind']=='port' and record['surface']=='historical_bay_candidate':
                # Do not invent a navigable medieval shoreline from the modern lake.
                candidates = np.flatnonzero((~dry & near_land & water_centers & inner)&(distance<=maximum_shift))
            if not len(candidates):
                raise ValueError('No geographically valid candidate: '+record['name'].encode('unicode_escape').decode()+f' ({record["kind"]}, component {island}, cells {int(np.count_nonzero(component==island))})')
        requests.append((record,x,y,candidates,distance))
    width = view.width
    q = indices % width - (indices//width-(indices//width)%2)//2
    r = indices // width
    fixed=[];used=set()
    cities=sorted((s for s in requests if s[0]['kind']=='city'),key=lambda s:(bool(s[0].get('minimumSpacing')),len(s[3]),float(np.min(s[4][s[3]])),s[0]['id']))
    for request in cities:
        record,_,_,candidates,distance=request
        available=[int(c) for c in candidates if int(c) not in used and all(hex_distance(int(c),old,view.width)>=site_spacing(record,previous,policy) for previous,old in fixed)]
        if not available:raise ValueError('City anchors collide at this grid resolution: '+record['name'])
        cell=min(available,key=lambda c:distance[c])
        fixed.append((record,cell));used.add(cell)
    requests=[s for s in requests if s[0]['kind']!='city']
    placements = None
    for attempt in range(30):
        pending,result=list(requests),list(fixed)
        clearances = {request[0]['id']: np.full(len(request[3]),99) for request in requests}
        legalities = {request[0]['id']: np.ones(len(request[3]),dtype=bool) for request in requests}
        for request in requests:
            record,_,_,candidates,_=request
            for previous,cell in fixed:
                dq,dr=q[candidates]-q[cell],r[candidates]-r[cell]
                steps=np.maximum(np.maximum(abs(dq),abs(dr)),abs(dq+dr))
                legalities[record['id']] &= steps>=site_spacing(record,previous,policy)
                np.minimum(clearances[record['id']],steps,out=clearances[record['id']])
        rng = np.random.default_rng(20261026+attempt)
        while pending:
            choices = []
            for request in pending:
                record,x,y,candidates,distance = request
                clearance = clearances[record['id']]
                legal = legalities[record['id']]
                valid = candidates[legal]
                if not len(valid):
                    break
                score = distance[valid]+np.maximum(0,policy['preferredHexDistance']-clearance[legal])*view.config['gridStepKm']*.75
                if attempt:
                    score = score+rng.random(len(valid))*view.config['gridStepKm']*.65
                best = int(valid[np.argmin(score)])
                choices.append((len(valid),record['kind']=='port',float(np.min(distance[valid])),request,best))
            else:
                choice = min(choices,key=lambda c:c[:3])
                request,cell = choice[3],choice[4]
                pending.remove(request)
                result.append((request[0],cell))
                for other in pending:
                    record,_,_,candidates,_ = other
                    dq,dr=q[candidates]-q[cell],r[candidates]-r[cell]
                    steps=np.maximum(np.maximum(abs(dq),abs(dr)),abs(dq+dr))
                    required=site_spacing(record,request[0],policy)
                    legalities[record['id']] &= steps>=required
                    np.minimum(clearances[record['id']],steps,out=clearances[record['id']])
                continue
            break
        if not pending:
            placements = result
            break
    if placements is None:
        blocked=[request[0]['name'] for request in pending if not np.any(legalities[request[0]['id']])]
        raise ValueError('Cannot meet building spacing: '+', '.join(blocked))
    result = []
    for record,cell in placements:
        row,col = divmod(cell,width)
        level = city_level(view.key, record)
        data[cell,0:2] = [1,int(data[cell,1])%7] if record['kind']=='port' else [0,255]
        x,y = map(float,view.xy(record['lon'],record['lat']))
        shift = float(geodesic_distance(record['lon'],record['lat'],cell_lon[cell],cell_lat[cell]))
        result.append(dict(**record,cell=cell,col=col,row=row,world_pixel=[float(view.x[cell]),float(view.y[cell])],
                           source_world_pixel=[x,y],displacement_km=round(shift,3),level=level,
                           ocean_connected=cell in ocean if record['kind']=='port' else None,
                           empty_land_neighbors=int(land_neighbors[cell]) if record['kind']!='port' else None,
                           generalized_land=record['kind']!='port' and float(fractions[cell])<=.5))
    result.sort(key=lambda c:c['id'])
    for site in result:
        if site['kind']!='port':
            site['empty_land_neighbors']=int(sum(bool(dry[c]) and c not in {s['cell'] for s in result} for c in neighbors(site['cell'],view.width,view.height)))
        others=[(hex_distance(site['cell'],s['cell'],width),s['id']) for s in result if s['id']!=site['id']]
        site['nearest_building_hexes'],site['nearest_building_id']=min(others)
    pairs=[]
    for i,site in enumerate(result):
        for other in result[i+1:]:
            d=hex_distance(site['cell'],other['cell'],width)
            if d<policy['minimumHexDistance']:
                assert d>=site_spacing(site,other,policy)
                pairs.append(dict(first=site['name'],second=other['name'],first_id=site['id'],second_id=other['id'],
                    first_cell=site['cell'],second_cell=other['cell'],distance=d,
                    reason='Keep nearby cities at their real geographic anchors' if site['kind']==other['kind']=='city' else 'Geographic coastal berth'))
    return result, pairs, adjustments


def river_edges(view,data,rivers,sites):
    barrier = unary_union([g for g,_ in rivers])
    prepare(barrier)
    for cell in np.flatnonzero(data[:,0]!=1):
        row,col=divmod(int(cell),view.width)
        for direction in range(3):
            nc,nr=[(col+1,row),(col+row%2,row+1),(col+row%2-1,row+1)][direction]
            other=nr*view.width+nc
            if 0<=nc<view.width and nr<view.height and data[other,0]!=1:
                if LineString([view.points[cell],view.points[other]]).intersects(barrier):
                    data[cell,8] |= 1<<direction
                    data[other,8] |= 1<<(direction+3)
    bridges=[]
    used=set()
    for site in sites:
        if site['kind']=='port':continue
        pairs=[]
        for cell in np.flatnonzero(data[:,8]):
            if hex_distance(site['cell'],int(cell),view.width)>2:continue
            for direction,other in enumerate_neighbor(int(cell),view):
                if direction<3 and data[cell,8] & (1<<direction):
                    pairs.append((np.linalg.norm(view.points[cell]-site['world_pixel']),int(cell),other,direction))
        if not pairs:continue
        _,cell,other,d=min(pairs)
        if any(hex_distance(cell,old,view.width)<4 for old in used):continue
        used.add(cell)
        bridges.append(dict(cell=cell,other=other,direction=d,near=site['name'],kind='inspection-crossing'))
    return bridges


def enumerate_neighbor(cell,view):
    row,col=divmod(cell,view.width)
    for d,(nc,nr) in enumerate(((col+1,row),(col+row%2,row+1),(col+row%2-1,row+1),(col-1,row),(col+row%2-1,row-1),(col+row%2,row-1))):
        if 0<=nc<view.width and 0<=nr<view.height:yield d,nr*view.width+nc


def overlays(view,data,relief,sites,land):
    import xml.etree.ElementTree as ET
    defs=list(ET.parse(BASE/'original/def_mapterrain.xml').getroot())
    rows=[]
    occupied={s['cell'] for s in sites}
    for cell in np.flatnonzero(data[:,0]!=1):
        kind,index=map(int,data[cell,:2])
        if kind==0 or cell in occupied:continue
        entry=next(e for e in defs[kind] if int(e.get('idx'))==index)
        image=entry.get('image')
        rows.append(dict(cell=int(cell),cellKind=kind,cellIndex=index,image=image,
                         x=float(view.x[cell]),y=float(view.y[cell]),relief=relief[cell]))
    ocean=ocean_cells(data.reshape(view.height,view.width,12))
    rng=np.random.default_rng(20261026+(1 if view.key=='hre' else 0))
    random_values=rng.random(len(data))
    for cell in sorted(ocean):
        if random_values[cell]>=.04 or cell in occupied:continue
        if any(hex_distance(cell,s['cell'],view.width)<4 for s in sites):continue
        if land.distance(Point(*view.points[cell]))<90:continue
        index=cell%7
        dx,dy=map(int,rng.integers(-14,15,size=2))
        data[cell,4:8]=[1,index,dx%256,dy%256]
        rows.append(dict(cell=cell,image=f'wave_{index+1}.png',x=float(view.x[cell]+dx),y=float(view.y[cell]+dy),seaForeground=True))
    return rows


def labels(view):
    result=[]
    for zh,en,_,coords in view.config['relief']:
        lon,lat=coords[len(coords)//2]
        x,y=map(float,view.xy(lon,lat))
        result.append(dict(textZh=zh,text=en,x=x,y=y,sea=False,category='mountain'))
    for zh,lon,lat,_ in view.config['plains']:
        x,y=map(float,view.xy(lon,lat))
        result.append(dict(textZh=zh,text='',x=x,y=y,sea=False,category='plain'))
    for zh,en,lon,lat in view.config['seaLabels']:
        x,y=map(float,view.xy(lon,lat))
        result.append(dict(textZh=zh,text=en,x=x,y=y,sea=True))
    return [r for r in result if 0<r['x']<view.world_width and 0<r['y']<view.world_height]


def terrain_metadata(data,bridges):
    import xml.etree.ElementTree as ET
    definitions=list(ET.parse(BASE/'original/def_mapterrain.xml').getroot())
    types={e.get('type'):e for e in ET.parse(BASE/'original/def_terraintype.xml').getroot()}
    names={'land':'平地','sea':'水域','plant':'疏林','forest':'森林','desert':'沙漠','hill':'丘陵','mountain':'山地'}
    rules=[]
    for definition in definitions:
        kind=definition.get('type');rule=types[kind]
        rules.append(dict(type=kind,name=names[kind],movementCost=int(rule.get('movementcost')),
            penalties={unit:int(rule.get('penalty_'+unit)) for unit in ('infantry','cavalry','artillery')},
            sprites={e.get('idx'):e.get('image') for e in definition}))
    packed=np.zeros((len(data),4),dtype=np.uint8)
    packed[:,:2]=data[:,:2];packed[:,2]=data[:,8]
    for bridge in bridges:
        packed[bridge['cell'],3] |= 1<<bridge['direction']
        packed[bridge['other'],3] |= 1<<((bridge['direction']+3)%6)
    return dict(terrainStride=4,terrainCells=base64.b64encode(packed.tobytes()).decode(),terrainRules=rules)


def bitmap(view,land,rivers,rows,sites,bridges,out,data):
    width=6000
    scale=width/view.world_width
    height=round(view.world_height*scale)
    mask=Image.new('L',(width,height));draw=ImageDraw.Draw(mask)
    for polygon in plan.parts(land,'Polygon'):
        draw.polygon([(x*scale,y*scale) for x,y in polygon.exterior.coords],fill=255)
        for ring in polygon.interiors:
            draw.polygon([(x*scale,y*scale) for x,y in ring.coords],fill=0)
    patches=[]
    for site in sites:
        x,y=[p*scale for p in site['world_pixel']]
        if site['kind']=='port':
            if site.get('river_berth'):
                draw.ellipse((x-12*scale,y-12*scale,x+12*scale,y+12*scale),fill=0)
                continue
            assert mask.getpixel((int(x),int(y)))<128,site['name']+' visible port water'
            continue
        if mask.getpixel((int(x),int(y)))<128:
            draw.ellipse((x-12*scale,y-12*scale,x+12*scale,y+12*scale),fill=255)
            patches.append(site['id'])
    sea=Image.new('RGB',mask.size,(68,102,136))
    texture=Image.open(BASE/'client/images/map_pt@2x.png').convert('RGB')
    texture=texture.resize((256,256),Image.Resampling.LANCZOS)
    land_image=Image.new('RGB',mask.size)
    for y in range(0,height,256):
        for x in range(0,width,256):land_image.paste(texture,(x,y))
    if palette := view.config.get('terrainPalette'):
        original_ground = land_image.copy()
        land_image = Image.blend(original_ground,Image.new('RGB',mask.size,palette['0']),.78)
        offsets=((0,-36),(32,-18),(32,18),(0,36),(-32,18),(-32,-18))
        for kind in (8,9):
            cover=Image.new('L',mask.size)
            painter=ImageDraw.Draw(cover)
            for cell in np.flatnonzero(data[:,0]==kind):
                x,y=view.points[cell]
                painter.polygon([((x+dx)*scale,(y+dy)*scale) for dx,dy in offsets],fill=255)
            cover=ImageChops.multiply(cover,mask)
            tinted=Image.blend(original_ground,Image.new('RGB',mask.size,palette[str(kind)]),.78)
            land_image.paste(tinted,(0,0),cover)
        del original_ground,tinted,cover
    sea.paste(land_image,(0,0),mask)
    coast=Image.fromarray((np.asarray(mask.filter(ImageFilter.MaxFilter(3)),dtype=np.int16)-np.asarray(mask,dtype=np.int16)).astype(np.uint8))
    sea.paste((110,143,153),(0,0,width,height),coast)
    draw=ImageDraw.Draw(sea)
    for geometry,_ in rivers:
        for line in plan.parts(geometry,'LineString'):
            draw.line([(x*scale,y*scale) for x,y in line.coords],fill=(68,102,136),width=2)
    sea.save(out/'base.jpg',quality=93,optimize=True)
    tile_atlas=Image.open(BASE/'client/images/tiles.png').convert('RGBA')
    building_atlas=Image.open(BASE/'client/images/buildings.png').convert('RGBA')
    cache={}
    def sprite(image,x,y,atlas_name,sx=1,sy=1,opacity=1):
        key=atlas_name,image,sx,sy,opacity
        if key not in cache:
            s=ART['atlases'][atlas_name][image]
            atlas=tile_atlas if atlas_name=='tiles' else building_atlas
            crop=atlas.crop((round(s['x']),round(s['y']),round(s['x']+s['w']),round(s['y']+s['h'])))
            crop=crop.resize((max(1,round(crop.width*scale*sx)),max(1,round(crop.height*scale*sy))),Image.Resampling.LANCZOS)
            if opacity!=1:crop.putalpha(crop.getchannel('A').point(lambda a:round(a*opacity)))
            cache[key]=(crop,s)
        crop,s=cache[key]
        sea.paste(crop,(round((x-s['refx']*sx)*scale),round((y-s['refy']*sy)*scale)),crop)
    for row in rows:
        if row.get('overview') is False:
            continue
        sx=row.get('scaleX',1)*row.get('overviewScale',1);sy=row.get('scaleY',1)*row.get('overviewScale',1)
        sprite(row['image'],row['x'],row['y'],'tiles',sx,sy,row.get('opacity',1))
    draw=ImageDraw.Draw(sea)
    for geometry,_ in rivers:
        for line in plan.parts(geometry,'LineString'):
            draw.line([(x*scale,y*scale) for x,y in line.coords],fill=(68,102,136),width=2)
    for bridge in bridges:
        x,y=(view.points[bridge['cell']]+view.points[bridge['other']])/2
        sprite(f'bridge_2_{bridge["direction"]+1}.png',x,y,'buildings')
    sea.save(out/'terrain.jpg',quality=93,optimize=True)
    for site in sites:
        style='east' if view.key=='japan' else 'west'
        stem={'city':'city','industry':'factory','stable':'stable'}.get(site['kind'])
        image=f'{stem}_{style}_lv{site["level"]}.png' if stem else f'port_{site.get("building_style",1)}.png'
        site['sprite']=image
        sprite(image,*site['world_pixel'],'buildings')
    draw=ImageDraw.Draw(sea)
    occupancy=plan.Occupancy()
    for site in sites:
        x,y=[p*scale for p in site['world_pixel']]
        occupancy.add((x-12,y-13,x+12,y+9),site['id'])
    missing=[]
    for site in sorted(sites,key=lambda s:(s['kind']!='city',s['row'],s['col'])):
        x,y=[p*scale for p in site['world_pixel']]
        f=plan.font(16 if site['kind']=='city' else 14)
        l,t,r,b=draw.textbbox((0,0),site['name'],font=f)
        w,h=r-l,b-t
        chosen=None
        for radius in (16,25,38,55,78,110,150,200):
            for angle in (90,-90,0,180,45,-45,135,-135):
                a=math.radians(angle);tx=x+radius*math.cos(a)-w/2;ty=y+radius*math.sin(a)-h/2
                rect=(tx-3,ty-3,tx+w+3,ty+h+3)
                if rect[0]>=4 and rect[1]>=4 and rect[2]<width-4 and rect[3]<height-4 and occupancy.clear(rect,site['id']):
                    chosen=(rect,tx,ty,radius);break
            if chosen:break
        if not chosen:missing.append(site['name']);continue
        rect,tx,ty,radius=chosen
        occupancy.add(rect,site['id'])
        if radius>25:draw.line((x,y,tx+w/2,ty+h/2),fill='#647e7d',width=1)
        draw.text((tx-l,ty-t),site['name'],font=f,fill='white',stroke_width=2,stroke_fill='#323c3a')
    assert not missing,missing
    for label in labels(view):
        x,y=label['x']*scale,label['y']*scale
        f=plan.font(21)
        l,t,r,b=draw.textbbox((0,0),label['textZh'],font=f)
        rect=(x-(r-l)/2-4,y-4,x+(r-l)/2+4,y+b-t+4)
        if not occupancy.clear(rect,'geographic'):continue
        occupancy.add(rect,'geographic')
        draw.text((x-(r-l)/2-l,y-t),label['textZh'],font=f,fill='#324b4a',stroke_width=2,stroke_fill='#e1e5df')
    sea.save(out/'overview.jpg',quality=94,optimize=True)
    if view.config.get('terrainPalette') or view.config.get('terrainAppearance')=='native-range-bands':
        west,south,east,north=view.config['detailBounds']
        corners=[view.xy(lon,lat) for lon,lat in ((west,south),(east,south),(west,north),(east,north))]
        left=max(0,min(p[0] for p in corners)*scale);right=min(width,max(p[0] for p in corners)*scale)
        top=max(0,min(p[1] for p in corners)*scale);bottom=min(height,max(p[1] for p in corners)*scale)
        detail=sea.crop((round(left),round(top),round(right),round(bottom)));detail.thumbnail((1600,1200))
        detail.save(out/'landforms-detail.jpg',quality=94,optimize=True)
        detail.save(out/'regional-terrain-detail.jpg',quality=94,optimize=True)
        if view.config.get('terrainAppearance')=='native-range-bands':
            detail.save(out/'relief-bands-detail.jpg',quality=94,optimize=True)
            detail.save(out/'native-terrain-detail.jpg',quality=94,optimize=True)
    sea.thumbnail((2000,2200));sea.save(out/'preview.jpg',quality=90,optimize=True)
    if view.config.get('terrainPalette') or view.config.get('terrainAppearance')=='native-range-bands':
        sea.save(out/'landforms-preview.jpg',quality=92,optimize=True)
        sea.save(out/'regional-terrain-preview.jpg',quality=92,optimize=True)
        if view.config.get('terrainAppearance')=='native-range-bands':
            sea.save(out/'relief-bands-preview.jpg',quality=92,optimize=True)
            sea.save(out/'native-terrain-preview.jpg',quality=92,optimize=True)
    return dict(width=width,height=height,land_patches=patches,missing_labels=missing)


def validate(view,data,sites,components,component,policy):
    assert len({s['cell'] for s in sites})==len(sites)
    for site in sites:
        assert (data[site['cell'],0]==1)==(site['kind']=='port'),site['name']
        maximum_shift = policy['portMaximumShiftKm'] if site['kind']=='port' else policy['cityMaximumShiftKm'] if site['kind']=='city' else policy['maximumShiftKm']
        assert site['displacement_km']<=maximum_shift+.001,site['name']
    for i,a in enumerate(sites):
        for b in sites[i+1:]:
            required=site_spacing(a,b,policy)
            assert hex_distance(a['cell'],b['cell'],view.width)>=required,(a['name'],b['name'])
    for cell in np.flatnonzero(data[:,8]):
        for d,other in enumerate_neighbor(int(cell),view):
            assert bool(int(data[cell,8])&(1<<d))==bool(int(data[other,8])&(1<<((d+3)%6)))
    controls=[]
    dry=data[:,0]!=1
    for name,lon,lat in view.config['islands']:
        x,y=view.xy(lon,lat)
        island=int(STRtree(components).nearest(Point(x,y)))
        retained=np.flatnonzero((component==island)&dry)
        assert len(retained),name
        controls.append(dict(name=name,kind='land',cells=len(retained)))
    for name,lon,lat in view.config['waterControls']:
        cell=view.nearest(lon,lat,~dry)
        assert data[cell,0]==1
        controls.append(dict(name=name,kind='water',cell=cell))
    for s in sites[:max(0,30-len(controls))]:controls.append(dict(name=s['name'],kind=s['kind'],cell=s['cell']))
    land_only=[s for s in sites if s['kind']!='port']
    nearest=[min(hex_distance(a['cell'],b['cell'],view.width) for b in land_only if a is not b) for a in land_only]
    return dict(errors=0,controls=controls,land_building_minimum_hexes=min(nearest),
                land_building_median_nearest_hexes=float(np.median(nearest)),
                four_hex_clearance_count=sum(v>=4 for v in nearest),land_buildings=len(nearest))


def build(key,config,policy):
    out=ROOT/'maps'/key;out.mkdir(parents=True,exist_ok=True)
    view=MapView(key,config)
    print(f'{key}: {view.width}x{view.height} geography',flush=True)
    land,components,tree,rivers=geography(view)
    data,dry,component,fractions,changes=make_grid(view,land,components,tree)
    relief,relief_evidence,passes=terrain(view,data,dry,land,rivers)
    write_json(out/'elevation.json',relief_evidence,compact=True)
    print(f'{key}: fixing real city anchors, then spacing facilities',flush=True)
    sites,exceptions,adjustments=allocate(view,data,dry,component,components,tree,fractions,policy)
    from dual_map_ports import fit
    fit(view,data,sites,land)
    relief_evidence['overrides']=[dict(cell=s['cell'],reason='approved building anchor') for s in sites if s['kind']!='port']+[
        dict(cell=p['cell'],reason='single traversal cell at configured pass') for p in passes]
    write_json(out/'elevation.json',relief_evidence,compact=True)
    rows=overlays(view,data,relief,sites,land)
    rows=dual_map_cartography.game_tiles(rows,ART['atlases']['tiles'],config.get('terrainAppearance'),config.get('terrainScales'))
    if config.get('terrainAppearance') in ('native-height-hierarchy','native-range-bands'):
        dual_map_cartography.overview_landforms(rows,relief_evidence,view.width,config.get('hillOverviewSpacing',3))
    bridges=river_edges(view,data,rivers,sites)
    checks=validate(view,data,sites,components,component,policy)
    binary=struct.pack('<4s3i',b'YSAE',1,view.width,view.height)+data.tobytes()
    assert TerrainMap.from_bytes(binary).to_bytes()==binary
    (out/(key+'.bin')).write_bytes(binary)
    write_json(out/'buildings.json',sites)
    write_json(out/'generalizations.json',dict(grid=changes,buildings=adjustments,spacing_exceptions=exceptions,
        coastal_spacing_exceptions=[r for r in exceptions if r['reason']=='Geographic coastal berth']))
    write_json(out/'crossings.json',bridges)
    write_json(out/'passes.json',passes)
    metadata=dict(key=key,title=config['title'],gridWidth=view.width,gridHeight=view.height,
                  totalCells=len(data),landCells=int(np.count_nonzero(data[:,0]!=1)),waterCells=int(np.count_nonzero(data[:,0]==1)),
                  worldWidth=view.world_width,worldHeight=view.world_height,approximateStepKm=config['gridStepKm'],
                  imageWidth=6000,imageHeight=round(view.world_height/view.world_width*6000),
                  counts=dict(Counter(s['kind'] for s in sites)),policy=policy,labels=labels(view),overlays=rows,crossings=bridges,
                  rivers=[dict(name=name,paths=[[[float(x),float(y)] for x,y in line.coords] for line in plan.parts(g,'LineString')]) for g,name in rivers],
                  cartography=dict(method='Native game base and one matching game sprite per non-plain land cell; no DEM image shading',
                      base='native-game-texture',terrainCoverage='every-non-plain-land-cell',
                      appearance=config.get('terrainAppearance','uniform-footprint'),
                      relief_symbols=dict(Counter(r['relief'] for r in rows if r.get('relief') in ('hill','low','middle','high')))),
                  landBits=base64.b64encode(np.packbits(data[:,0]!=1,bitorder='little').tobytes()).decode())
    metadata['gridWindow'] = config.get('gridWindow')
    metadata['excludedSiteNames'] = config.get('excludedSiteNames', [])
    metadata['terrainPalette'] = config.get('terrainPalette')
    if config.get('terrainAppearance')=='native-range-bands':
        metadata['cartography']['reliefReference']=dict(source='rewrite/data/original/def_mapterrain.xml',
            atlas='rewrite/data/client/images/tiles.png',
            method='One unmodified original sprite at each real terrain cell; uniform scaling retains native aspect ratio',
            uniformScales=dict(hill=[.75,.65,.58],**config['terrainScales']),extraStamps=0,
            materialSelection='Six native hill and mountain variants; hill morphology by local relief, slope aspect and adjacent repeats')
    if key == 'japan' and (out/'sengoku_plan.json').exists():
        metadata['historicalGeography'] = japan_sengoku.grid_plan(view,data,sites)
    metadata.update(terrain_metadata(data,bridges))
    write_json(out/'map.json',metadata,compact=True)
    print(f'{key}: rendering native map artwork',flush=True)
    rendering=bitmap(view,land,rivers,rows,sites,bridges,out,data)
    write_json(out/'buildings.json',sites)
    report=dict(stage='A-C source map, not installed or a historical scenario',cells=len(data),buildings=len(sites),counts=metadata['counts'],
                land_cells=metadata['landCells'],water_cells=metadata['waterCells'],
                terrain_counts={str(k):int(v) for k,v in Counter(map(int,data[:,0])).items()},
                relief_method=relief_evidence['method'],elevation_sources=relief_evidence['sources'],
                grid=[view.width,view.height],approximate_step_km=config['gridStepKm'],
                projection='approved 15-degree affine composition registration; scale varies with latitude' if key=='japan' else 'Lambert conformal conic 12E/50N, parallels 44N/56N',
                river_edges=sum(int(v).bit_count() for v in data[:,8]),crossings=len(bridges),passes=len(passes),
                spacing_exceptions=len(exceptions),maximum_building_shift_km=max(s['displacement_km'] for s in sites),
                source_points_sha256=digest(plan.OUT/(key+'_points.json')),config_sha256=digest(CONFIG_PATH),
                terrain_sha256=digest(out/(key+'.bin')),checks=checks,rendering=dict(rendering,elevationImage=False,terrainCoverage=metadata['cartography']['terrainCoverage']),
                sources=[dict(path=str(p.relative_to(ROOT)),sha256=digest(p),license='Natural Earth public domain')
                         for p in (plan.CACHE/'ne_10m_land.zip',plan.CACHE/'ne_10m_lakes.zip')]+[
                         dict(path=f'maps/source_cache/hydrorivers_20261026/{key}.geojson',
                              sha256=digest(plan.CACHE/'hydrorivers_20261026'/f'{key}.geojson'),license='HydroSHEDS License Agreement')],
                pending=['No scenario year or political ownership','Modern coast, Zuiderzee history pending','No original BTL export: signed 16-bit cell indices are insufficient for these spacious full grids','Engine session, Android and performance integration pending'])
    write_json(out/'verification.json',report)
    print(json.dumps({k:report[k] for k in ('grid','cells','buildings','counts','spacing_exceptions','maximum_building_shift_km')}),flush=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--map',choices=['japan','hre','both'],default='both')
    args=parser.parse_args()
    config=json.loads(CONFIG_PATH.read_text(encoding='utf-8'))
    dual_map_cartography.material_catalog(ART['atlases']['tiles'],Image.open(BASE/'client/images/tiles.png').convert('RGBA'),
        ROOT/'maps/japan_hre_source_20261026/native-materials.jpg',plan.font(15))
    for key in ('japan','hre') if args.map=='both' else (args.map,):build(key,config[key],config['spacing'])
    write_viewer_data()


def write_viewer_data():
    data={}
    for key in ('japan','hre'):
        folder=ROOT/'maps'/key
        if not (folder/'map.json').exists():return
        data[key]=json.loads((folder/'map.json').read_text(encoding='utf-8'))
        data[key]['records']=json.loads((folder/'buildings.json').read_text(encoding='utf-8'))
    used_tiles={r['image'] for m in data.values() for r in m['overlays']}
    used_buildings={s['sprite'] for m in data.values() for s in m['records']}
    used_buildings.update(f'bridge_2_{b["direction"]+1}.png' for m in data.values() for b in m['crossings'])
    data['atlases']={'tiles':{k:ART['atlases']['tiles'][k] for k in sorted(used_tiles)},
                     'buildings':{k:ART['atlases']['buildings'][k] for k in sorted(used_buildings)}}
    folder=ROOT/'maps/japan_hre_source_20261026';folder.mkdir(parents=True,exist_ok=True)
    write_json(folder/'map_counts.json',dict(definition='Full rectangular grid, including water and edge cells',
        maps={key:{k:data[key][k] for k in ('gridWidth','gridHeight','totalCells','landCells','waterCells','approximateStepKm')} for key in ('japan','hre')}))
    (folder/'viewer-data.js').write_text('window.MAP_SOURCE_DATA='+json.dumps(data,ensure_ascii=False,separators=(',',':'))+';\n',encoding='utf-8')


if __name__=='__main__':
    main()
