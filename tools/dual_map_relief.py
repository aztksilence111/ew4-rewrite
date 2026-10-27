"""Classify generalized hex relief from cached Mapzen Terrarium elevation tiles."""

from concurrent.futures import ThreadPoolExecutor
import hashlib
import io
import json
from pathlib import Path
import time
import urllib.request

import numpy as np
from PIL import Image
from shapely import contains_xy

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / 'maps/source_cache/elevation_terrarium'
ZOOM = 8
URL = 'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png'
POLICY = dict(mountainPeakM=700, mountainReliefM=220, ruggedPeakM=300, ruggedReliefM=350,
              hillPeakM=180, hillReliefM=90, middlePeakM=1400, middleReliefM=450,
              highPeakM=2200, highReliefM=750, radiusKm=8, localRadiusKm=2, samples=25,
              valleyLocalReliefM=180, valleyMedianContrastM=80, ridgeLocalReliefM=90, riverValleyContrastM=250)


def read_url(url):
    for attempt in range(3):
        try:
            request = urllib.request.Request(url, headers={'User-Agent':'EW4-map-geography-review/1.0'})
            with urllib.request.urlopen(request, timeout=45) as response:
                return response.read(4*1024*1024)
        except OSError:
            if attempt == 2:
                raise
            time.sleep(attempt+1)


def tile_path(x,y):
    return CACHE / str(ZOOM) / str(x) / f'{y}.png'


def ensure_tile(pair):
    x,y = pair
    path = tile_path(x,y)
    if path.exists():
        with Image.open(path) as image:
            assert image.size == (256,256) and image.mode == 'RGB', path
        return path
    raw = read_url(URL.format(z=ZOOM,x=x,y=y))
    with Image.open(io.BytesIO(raw)) as image:
        assert image.size == (256,256) and image.mode == 'RGB', pair
        image.verify()
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(raw)
    return path


class Elevation:
    def __init__(self):
        self.tiles = {}
        self.used = set()

    def sample(self,lon,lat):
        world = (1<<ZOOM)*256
        px = (np.asarray(lon)+180)/360*world-.5
        lat_rad = np.radians(np.clip(lat,-85,85))
        py = (1-np.arcsinh(np.tan(lat_rad))/np.pi)/2*world-.5
        ix,iy = np.floor(px).astype(int),np.floor(py).astype(int)
        fx,fy = px-ix,py-iy
        positions = [(ix,iy),(ix+1,iy),(ix,iy+1),(ix+1,iy+1)]
        needed = set()
        for x,y in positions:
            needed.update(zip((x//256).ravel().tolist(),(y//256).ravel().tolist()))
        missing = sorted(needed-self.tiles.keys())
        if missing:
            print(f'  elevation: {len(missing)} tiles to load',flush=True)
            with ThreadPoolExecutor(max_workers=4) as pool:
                paths = list(pool.map(ensure_tile,missing))
            for pair,path in zip(missing,paths):
                with Image.open(path) as image:
                    rgb = np.asarray(image,dtype=np.float32)
                self.tiles[pair] = rgb[:,:,0]*256+rgb[:,:,1]+rgb[:,:,2]/256-32768
        self.used.update(needed)
        values=[]
        for x,y in positions:
            result=np.empty(x.shape,dtype=np.float32)
            tx,ty=x//256,y//256
            for pair in needed:
                selected=(tx==pair[0])&(ty==pair[1])
                if np.any(selected):
                    result[selected]=self.tiles[pair][y[selected]%256,x[selected]%256]
            values.append(result)
        a,b,c,d=values
        return a*(1-fx)*(1-fy)+b*fx*(1-fy)+c*(1-fx)*fy+d*fx*fy

    def sources(self, policy=None):
        CACHE.mkdir(parents=True,exist_ok=True)
        documents=[]
        for filename in ('attribution.md','data-sources.md'):
            path=CACHE/filename
            url='https://raw.githubusercontent.com/tilezen/joerd/master/docs/'+filename
            if not path.exists():
                path.write_bytes(read_url(url))
            documents.append(dict(path=str(path.relative_to(ROOT)),url=url,sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        return dict(provider='Mapzen terrain tiles, AWS Open Data',encoding='Terrarium: R*256+G+B/256-32768 metres',
                    zoom=ZOOM,policy=policy or POLICY,attribution=documents,
                    tiles=[dict(z=ZOOM,x=x,y=y,path=str(tile_path(x,y).relative_to(ROOT)),
                                url=URL.format(z=ZOOM,x=x,y=y),sha256=hashlib.sha256(tile_path(x,y).read_bytes()).hexdigest())
                           for x,y in sorted(self.used)])


def classify(view,data,dry,land,rivers=()):
    policy = dict(POLICY, **view.config.get('terrainPolicy', {}))
    active=np.flatnonzero(dry)
    offsets=[(0,0)]
    for radius in (policy['localRadiusKm'],policy['radiusKm']):
        offsets.extend((radius,a) for a in np.linspace(0,2*np.pi,13)[:-1])
    offsets=np.asarray(offsets)
    center_lon,center_lat=view.lonlat(view.x[active],view.y[active])
    lon0,lat0=np.radians(center_lon[:,None]),np.radians(center_lat[:,None])
    arc=offsets[None,:,0]/6371.0088
    bearing=offsets[None,:,1]
    lat=np.arcsin(np.sin(lat0)*np.cos(arc)+np.cos(lat0)*np.sin(arc)*np.cos(bearing))
    lon=lon0+np.arctan2(np.sin(bearing)*np.sin(arc)*np.cos(lat0),np.cos(arc)-np.sin(lat0)*np.sin(lat))
    lon,lat=np.degrees(lon),np.degrees(lat)
    xs,ys=view.xy(lon,lat)
    dem=Elevation()
    heights=dem.sample(lon,lat)
    on_land=contains_xy(land,xs,ys)
    # Exclude bathymetry, while retaining below-sea-level dry polders and valleys.
    on_land[:,0]=True
    valid=np.where(on_land,heights,np.nan)
    low=np.nanpercentile(valid,10,axis=1)
    peak=np.nanpercentile(valid,90,axis=1)
    maximum=np.nanmax(valid,axis=1)
    local_relief=peak-low
    near_low=np.nanpercentile(valid[:,:13],10,axis=1)
    near_peak=np.nanpercentile(valid[:,:13],90,axis=1)
    near_relief=near_peak-near_low
    ring=valid[:,1:13]
    centered=ring-np.nanmean(ring,axis=1,keepdims=True)
    angles=offsets[1:13,1]
    east=np.nanmean(centered*np.sin(angles),axis=1)
    north=np.nanmean(centered*np.cos(angles),axis=1)
    aspect=(np.degrees(np.arctan2(east,north))+360)%360
    median=np.nanmedian(valid,axis=1)
    ridge=(heights[:,0]>=median)&(near_relief>=policy['ridgeLocalReliefM'])&(local_relief>=policy.get('ridgeContextReliefM',300))
    mountain=((peak>=policy['mountainPeakM'])&((near_relief>=policy['mountainReliefM'])|ridge))|((peak>=policy['ruggedPeakM'])&(near_relief>=policy['ruggedReliefM']))
    valley=(heights[:,0]<median-policy['valleyMedianContrastM'])&(near_relief<policy['valleyLocalReliefM'])
    mountain &= ~valley
    hill=(near_peak>=policy['hillPeakM'])&(near_relief>=policy['hillReliefM'])&~mountain&~valley
    levels=np.full(len(data),'',dtype=object)
    levels[active[hill]]='hill'
    levels[active[mountain]]='low'
    middle=mountain&(peak>=policy['middlePeakM'])&(local_relief>=policy['middleReliefM'])
    high=mountain&(peak>=policy['highPeakM'])&(local_relief>=policy['highReliefM'])
    levels[active[middle]]='middle'
    levels[active[high]]='high'
    # A coarse hex containing an actual river valley can carry a traversable valley,
    # even when its center falls on the adjacent slope. Keep the surrounding ridges.
    floor=np.full(len(data),np.inf)
    for geometry,_ in rivers:
        lines=[geometry] if geometry.geom_type=='LineString' else list(geometry.geoms)
        for line in lines:
            if line.geom_type!='LineString':continue
            points=[line.interpolate(float(d)) for d in np.arange(0,line.length+1,16)]
            if not points:continue
            px=np.array([p.x for p in points]);py=np.array([p.y for p in points])
            rr=np.clip(np.rint((py+18)/54).astype(int),0,view.height-1)
            cc=np.clip(np.rint((px-(rr%2)*32)/64).astype(int),0,view.width-1)
            cells=rr*view.width+cc
            lo,la=view.lonlat(px,py)
            z=dem.sample(lo,la)
            np.minimum.at(floor,cells,z)
    valley_cells=np.isfinite(floor[active])&(floor[active]<peak-policy['riverValleyContrastM'])
    modified=valley_cells&np.isin(levels[active],['low','middle','high'])
    levels[active[modified]]='hill'
    mountain &= ~modified
    hill |= modified
    data[active[hill],0]=8
    data[active[mountain],0]=9
    evidence=dict(method='Zoom-8 DEM; 2 km local slopes and 8 km ridge context; HydroRIVERS river-bearing valley hexes retain traversable corridors',
                  policy=policy,sources=dem.sources(policy),
                  cells=[dict(cell=int(cell),center_m=round(float(heights[i,0]),1),p10_m=round(float(low[i]),1),
                              p90_m=round(float(peak[i]),1),maximum_m=round(float(maximum[i]),1),
                              local_relief_m=round(float(local_relief[i]),1),classification=str(levels[cell] or 'plain'))
                         for i,cell in enumerate(active)])
    for i,record in enumerate(evidence['cells']):
        record.update(near_relief_m=round(float(near_relief[i]),1),valley_floor=bool(valley[i]),
                      slope_aspect_degrees=round(float(aspect[i]),1),
                      river_valley=bool(modified[i]),river_floor_m=round(float(floor[active[i]]),1) if np.isfinite(floor[active[i]]) else None)
    return levels,evidence
