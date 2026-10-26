"""Check the reported water repairs, provincial recruitment and site budget."""

from collections import Counter, deque
import json

import update_eastasia_feedback as u
from shapely import transform
from shapely.geometry import Point, shape

r = u.r


def main():
    checks = 0

    def check(value, message):
        nonlocal checks
        assert value, message
        checks += 1

    cities, facilities = u.read(r.REPORT/'cities.json'), u.read(r.REPORT/'facilities.json')
    old_cities, old_facilities = u.read(u.REPORT/'before/cities.json'), u.read(u.REPORT/'before/facilities.json')
    binary = (r.BASE/'original/eastasia.bin').read_bytes()
    data = r.np.frombuffer(binary[16:],dtype=r.np.uint8).reshape(-1,12)
    check(binary==(r.OUT/'original/eastasia.bin').read_bytes(),'source/runtime terrain match')
    check(len(cities)==280 and len({c['city_id'] for c in cities})==280,'stable city identities plus one town')
    for old in old_cities:
        city = next(c for c in cities if c['city_id']==old['city_id'])
        check((city['name'],city['cell'])==(old['name'],old['cell']),'existing named place preserved')
    check(len(cities)+len(facilities)<len(old_cities)+len(old_facilities),'site budget reduced')
    check(len({s['cell'] for s in cities+facilities})==len(cities)+len(facilities),'no overlapping buildings')
    template = u.read(r.BASE/'original/eastasia.map.json')
    check(template==u.read(r.OUT/'eastasia.map.json'),'modern map synchronized')
    for site in cities+facilities:
        named = 'city_id' in site
        c = template['cells'][site['cell']]
        check(c['buildingKind']==site.get('kind','city') and c['buildingLevel']==site['building_level' if named else 'level'], 'template building matches authoring')
        check(c['name']==(site['name'] if named else None),'names retained on specialized buildings')
    entries = u.provinces.provinces()
    for code,name in u.provinces.coverage_units(entries).items():
        kinds = [s.get('kind','city') for s in cities+facilities if u.provinces.cell_province(r,entries,s['cell'])==code]
        check(all(k in kinds for k in ('city','industry','stable')),name+' has all three recruitment centers')
    manifest = u.read(r.BASE/'client/manifest.json')
    check(manifest['maps']['eastasia']==u.read(r.OUT/'client/manifest.json')['maps']['eastasia'],'map display synchronized')
    for b in u.read(r.REPORT/'bridges.json'):
        check(data[b['cell'],8] & (1<<b['direction']) and data[b['other'],8] & (1<<(b['direction']+3)),b['name']+' still crosses river')
    for i,cell in enumerate(data):
        for direction in range(6):
            if cell[8] & (1<<direction):
                col,row = r.neighbor(i%r.GRID_W,i//r.GRID_W,direction)
                check(0<=col<r.GRID_W and 0<=row<r.GRID_H and data[row*r.GRID_W+col,8] & (1<<((direction+3)%6)),'river barrier reciprocal')
    _,rivers,_ = r.geography()
    native = next(g for g,a in rivers if a.get('name_en')=='Nen')
    check(not any(a.get('name')=='Nen lower course' for g,a in rivers),'no duplicate supplemental Nen course')
    check(any(g.distance(Point(native.coords[-1]))<.001 for g,a in rivers if a.get('name_en')=='Songhua'),'native Nen joins the existing downstream segment')
    lake = u.read(r.REPORT/'lake_additions.geojson')['features'][0]
    polygon = r.to_source_geometry(transform(shape(lake['geometry']),r.geo.PROJ.xy,interleaved=False))
    label = next(l for l in manifest['maps']['eastasia']['labels'] if l['text']=='LAKE ZAYSAN')
    check(polygon.covers(Point(label['tx']/2,label['ty']/2)),'Zaysan label centered on lake water')
    check(all(polygon.covers(Point((label['tx']+x)/2,(label['ty']+y)/2)) for x,y in label['textPath']),'Zaysan name follows the lake axis')
    mask = r.Image.open(r.REPORT/'land_mask.png')
    check(mask.getpixel((round(label['tx']-r.IMAGE_LEFT),round(label['ty']-r.IMAGE_TOP)))<128,'Zaysan water visible in raster land mask')
    geography = next(m for m in u.read(r.BASE/'rewrite/geography.json')['maps'] if m['map']=='eastasia')
    check(geography['baseTerrainSha256']==u.digest(r.BASE/'original/eastasia.bin'),'barrier content binding')
    check(geography['terrainOverrides']==u.read(u.REPORT/'before/rewrite/geography.json')['maps'][0]['terrainOverrides'],'passes and snow barriers preserved')
    blocked = {o['row']*r.GRID_W+o['column'] for o in geography['terrainOverrides'] if o.get('barrier')=='mountain'}
    def distance_stats(named, anonymous, kind):
        targets = [s['cell'] for s in named+anonymous if s.get('kind','city')==kind]
        distance, queue = dict.fromkeys(targets,0),deque(targets)
        while queue:
            cell = queue.popleft()
            for other in r.eastasia_settlements.neighbors(cell,r.GRID_W,r.GRID_H):
                if other not in distance and other not in blocked and data[other,0]!=1:
                    distance[other] = distance[cell]+1
                    queue.append(other)
        values = sorted(distance[c['cell']] for c in cities if u.provinces.province_at(entries,c['lon'],c['lat']) is not None and c['cell'] in distance)
        return dict(places=len(values),median=values[len(values)//2],p90=values[int(len(values)*.9)],maximum=max(values),over10=sum(v>10 for v in values))
    reach = {k:dict(before=distance_stats(old_cities,old_facilities,k),after=distance_stats(cities,facilities,k)) for k in ('industry','stable')}
    check(reach['stable']['after']['p90']<reach['stable']['before']['p90'],'stable recruitment coverage improved')
    for entry in u.read(r.BASE/'runtime-content.json')['files']:
        path = r.BASE/entry['path']
        check(path.stat().st_size==entry['bytes'] and u.digest(path)==entry['sha256'],'runtime resource digest')
    result = dict(checks=checks,errors=0,provinces=32,counts=dict(Counter(s.get('kind','city') for s in cities+facilities)),recruitmentDistances=reach)
    u.write(u.REPORT/'structure.json',result)
    print(json.dumps(result))


if __name__=='__main__':
    main()
