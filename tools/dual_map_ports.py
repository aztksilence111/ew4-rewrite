"""Fit the four native port sprites to actual coastal water cells."""

from collections import Counter
import base64
import copy
import json

import build_japan_hre_maps as source
import numpy as np
from shapely import contains_xy
from shapely.geometry import Point
from shapely.ops import nearest_points


def fit(view, data, sites, land):
    dry = data[:, 0] != 1
    ocean = source.ocean_cells(data.reshape(view.height, view.width, 12))
    occupied = {s['cell'] for s in sites}
    coastal = np.array([c for c in sorted(ocean) if any(dry[n] for n in source.neighbors(c,view.width,view.height))], dtype=int)
    coastal = coastal[~contains_xy(land.buffer(6),view.x[coastal],view.y[coastal])]
    lon, lat = view.lonlat(view.x[coastal],view.y[coastal])
    report = []
    rivers=None
    for site in sites:
        if site['kind'] != 'port':
            continue
        old = site['cell']
        occupied.remove(old)
        if site.get('river_berth'):
            if rivers is None:
                rivers=source.unary_union([g for g,_ in source.geography(view)[3]])
            shifts=source.geodesic_distance(site['lon'],site['lat'],*view.lonlat(view.x,view.y))
            choices=[int(c) for c in np.flatnonzero(shifts<=24) if c not in occupied and any(dry[n] for n in source.neighbors(int(c),view.width,view.height))
                     and rivers.distance(Point(*view.points[c]))<=24]
            if not choices:
                raise ValueError('No real river-axis berth near '+site['name'])
            cell=min(choices,key=lambda c:float(shifts[c])+rivers.distance(Point(*view.points[c]))*.25)
            if old!=cell:
                data[old,:2]=[0,255]
                data[cell,:2]=[1,cell%7]
        else:
            shifts = source.geodesic_distance(site['lon'],site['lat'],lon,lat)
            choices = []
            for i in np.flatnonzero(shifts<=24):
                c = int(coastal[i])
                if c in occupied:
                    continue
                gap = land.distance(Point(*view.points[c]))
                if gap>38:
                    continue
                score = float(shifts[i]) + abs(gap-18)*.12
                choices.append((score,c))
            if not choices:
                raise ValueError('No native-sprite coastal berth within 24 km: '+site['name'])
            cell = min(choices)[1]
        occupied.add(cell)
        p = Point(*view.points[cell])
        shore = nearest_points(p,land)[1]
        dx,dy = shore.x-p.x,shore.y-p.y
        if site.get('river_berth') and dx==dy==0:
            n = min((n for n in source.neighbors(cell,view.width,view.height) if dry[n]),key=lambda n:np.linalg.norm(view.points[n]-view.points[cell]))
            dx,dy = view.points[n]-view.points[cell]
        style = (2 if dx>0 else 3) if abs(dx)>abs(dy) else (4 if dy>0 else 1)
        row,col = divmod(cell,view.width)
        longitude,latitude = view.lonlat(p.x,p.y)
        site.update(cell=cell,row=row,col=col,world_pixel=[p.x,p.y],building_style=style,sprite=f'port_{style}.png',
            shore_pixel=[shore.x,shore.y],coast_vector=[float(dx),float(dy)],shore_gap_world=round(p.distance(land),3),
            displacement_km=round(float(source.geodesic_distance(site['lon'],site['lat'],longitude,latitude)),3))
        report.append(dict(name=site['name'],oldCell=old,cell=cell,style=style,gap=site['shore_gap_world'],riverBerth=bool(site.get('river_berth'))))
    return dict(ports=len(report),moved=sum(r['oldCell']!=r['cell'] for r in report),styles=dict(Counter(r['style'] for r in report)),records=report)


def main():
    config = source.json.loads((source.ROOT/'maps/dual_map_config.json').read_text(encoding='utf-8'))
    report = {}
    for key in ('japan','hre'):
        folder = source.ROOT/'maps'/key
        sites = json.loads((folder/'buildings.json').read_text(encoding='utf-8'))
        document = json.loads((folder/(key+'.map.json')).read_text(encoding='utf-8'))
        metadata = json.loads((folder/'map.json').read_text(encoding='utf-8'))
        blob=bytearray((folder/(key+'.bin')).read_bytes())
        data = np.frombuffer(blob,dtype=np.uint8,offset=16).reshape((-1,12))
        before=data[:,0].copy()
        for site in sites:
            if site['kind']=='port':
                document['cells'][site['cell']].update(buildingKind=None,buildingLevel=0,name=None)
        view = source.MapView(key,config[key])
        land,*_ = source.geography(view)
        report[key] = fit(view,data,sites,land)
        for cell in np.flatnonzero(before!=data[:,0]):
            template=next(c for i,c in enumerate(document['cells']) if int(data[i,0])==int(data[cell,0]) and not c['buildingKind'] and c['defenseTerrain']==('sea' if data[cell,0]==1 else 'land'))
            target=document['cells'][int(cell)]
            target['terrain']=copy.deepcopy(template['terrain'])
            target['terrain']['riverEdges']=int(data[cell,8])
            target['terrain']['bridgeEdges']=0
            target['defenseTerrain']='sea' if data[cell,0]==1 else 'land'
        (folder/(key+'.bin')).write_bytes(blob)
        metadata.update(source.terrain_metadata(data,metadata['crossings']))
        metadata['landBits']=base64.b64encode(np.packbits(data[:,0]!=1,bitorder='little').tobytes()).decode()
        for site in sites:
            if site['kind']=='port':
                document['cells'][site['cell']].update(buildingKind='port',buildingLevel=site['level'],name=site['name'])
        for site in sites:
            site['nearest_building_hexes'],site['nearest_building_id']=min((source.hex_distance(site['cell'],s['cell'],view.width),s['id']) for s in sites if s['id']!=site['id'])
        metadata['overlays']=[o for o in metadata['overlays'] if not (o.get('seaForeground') and any(source.hex_distance(o['cell'],s['cell'],view.width)<2 for s in sites if s['kind']=='port'))]
        for path,value in ((folder/'buildings.json',sites),(folder/(key+'.map.json'),document),(folder/'map.json',metadata)):
            source.write_json(path,value)
    source.write_json(source.ROOT/'maps/japan_hre_source_20261026/port-alignment.json',report)
    print(json.dumps({k:{f:v for f,v in r.items() if f!='records'} for k,r in report.items()}))


if __name__=='__main__':
    main()
