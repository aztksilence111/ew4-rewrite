"""Place real farmland in grain regions, then balance nearby flat-land coverage."""

import json
from pathlib import Path
import xml.etree.ElementTree as ET
from collections import Counter

import build_japan_hre_maps as source
import numpy as np
from shapely import contains_xy
from eastasia_settlements import hex_distance

ROOT = Path(__file__).resolve().parents[1]


def main():
    original=ET.parse(ROOT/'rewrite/data/original/def_construction.xml').getroot().find("construction[@type='farmland']")
    assert int(original.get('maxlevel'))==3 and len(original.findall('level'))==3
    policy = source.json.loads((ROOT/'maps/dual_map_agriculture.json').read_text(encoding='utf-8'))
    config = source.json.loads((ROOT/'maps/dual_map_config.json').read_text(encoding='utf-8'))
    summary = {}
    for key in ('japan', 'hre'):
        folder = ROOT/'maps'/key
        sites = json.loads((folder/'buildings.json').read_text(encoding='utf-8'))
        document = json.loads((folder/(key+'.map.json')).read_text(encoding='utf-8'))
        for cell in document['cells']:
            if cell['buildingKind'] == 'farmland':
                cell.update(buildingKind=None, buildingLevel=0, name=None)
        view = source.MapView(key, config[key])
        land, _, _, _ = source.geography(view)
        raw = np.frombuffer((folder/(key+'.bin')).read_bytes(), dtype=np.uint8, offset=16).reshape((-1,12))
        candidates = np.flatnonzero((raw[:,0] == 0) & contains_xy(land, view.x, view.y))
        candidates = np.array([c for c in candidates if document['cells'][int(c)]['buildingKind'] is None], dtype=int)
        lon, lat = view.lonlat(view.x[candidates], view.y[candidates])
        used = []
        farms = []
        cities = sorted((s for s in sites if s['kind']=='city'), key=lambda s:(-s['level'],s['id']))

        def place(pool, scores, region, level, reason):
            for index in np.argsort(scores, kind='stable'):
                cell = int(pool[index])
                if any(hex_distance(cell, c, view.width)<policy['farmSpacingHexes'] for c in used):
                    continue
                used.append(cell)
                x, y = map(float, view.points[cell])
                longitude, latitude = map(float, view.lonlat(x,y))
                anchor = min(cities, key=lambda s:hex_distance(cell,s['cell'],view.width))
                farms.append(dict(id=f'{key}-farm-{cell}',cell=cell,kind='farmland',name=None,level=level,
                    lon=longitude,lat=latitude,world_pixel=[x,y],region=region,reason=reason,
                    anchorCity=anchor['name'],anchorCityId=anchor['id'],group=anchor['group']))
                return True
            return False

        for region, longitude, latitude, rx, ry, count, level in policy[key]:
            radial = ((lon-longitude)*111*np.cos(np.radians(latitude))/rx)**2+((lat-latitude)*111/ry)**2
            selected = np.flatnonzero(radial<=1)
            pool = candidates[selected]
            for _ in range(count):
                scores = radial[selected].copy()
                if used:
                    clearance = np.array([min(hex_distance(int(c),f,view.width) for f in used) for c in pool])
                    scores -= np.minimum(clearance,7)*.12
                if not place(pool,scores,region,level,'grain-region'):
                    break
        coverage = []
        for city in cities:
            nearest = min((hex_distance(city['cell'],c,view.width) for c in used),default=999)
            if nearest>policy['cityCoverageHexes']:
                distance = np.array([hex_distance(city['cell'],int(c),view.width) for c in candidates])
                selected = np.flatnonzero(distance<=policy['cityCoverageHexes'])
                place(candidates[selected],distance[selected],city['name']+'周边平地',1,'regional-food-coverage')
            nearest = min((hex_distance(city['cell'],c,view.width) for c in used),default=999)
            coverage.append(dict(city=city['name'],cityId=city['id'],distanceHexes=nearest,covered=nearest<=policy['cityCoverageHexes']))
        for group in sorted({s['group'] for s in sites}):
            anchors = [s for s in sites if s['group']==group]
            if any(hex_distance(s['cell'],c,view.width)<=policy['cityCoverageHexes'] for s in anchors for c in used):
                continue
            distance = np.array([min(hex_distance(s['cell'],int(c),view.width) for s in anchors) for c in candidates])
            selected = np.flatnonzero(distance<=policy['cityCoverageHexes'])
            place(candidates[selected],distance[selected],group,1,'regional-food-coverage')
        for farm in farms:
            cell = document['cells'][farm['cell']]
            assert cell['buildingKind'] is None and not cell['terrain']['sea'] and int(raw[farm['cell'],0])==0
            cell.update(buildingKind='farmland',buildingLevel=farm['level'],name=None)
        # Old facility groups and newer province city groups can describe the same region.
        groups = {group:sum(any(s['group']==group and hex_distance(f['cell'],s['cell'],view.width)<=policy['cityCoverageHexes']
                              for s in sites) for f in farms) for group in sorted({s['group'] for s in sites})}
        result = dict(schema=1,records=farms,coverage=coverage,groups=groups,
            exceptions=[c for c in coverage if not c['covered']], corridorExclusion=False)
        assert all(result['groups'].values()), key+' has a region without food supply: '+str(result['groups'])
        for target,value in ((folder/'farmland.json',result),(folder/(key+'.map.json'),document)):
            target.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
        summary[key]=dict(farms=len(farms),grainRegionFarms=sum(f['reason']=='grain-region' for f in farms),
            levels=dict(sorted(Counter(f['level'] for f in farms).items())),foodIncomeByLevel={str(i+1):int(row.get('food')) for i,row in enumerate(original.findall('level'))},
            coveredCities=sum(c['covered'] for c in coverage),cities=len(cities),groups=result['groups'],exceptions=result['exceptions'])
    (ROOT/'maps/japan_hre_source_20261026/agriculture-summary.json').write_text(
        json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(summary,ensure_ascii=True))


if __name__ == '__main__':
    main()
