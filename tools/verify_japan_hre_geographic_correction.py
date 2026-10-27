"""Check corrected real city positions, named rivers and multi-cell valley corridors."""

from collections import deque
import json

import build_japan_hre_maps as b
import audit_japan_hre_geography as audit
from shapely.geometry import LineString, Point
from shapely.ops import nearest_points, unary_union

OUT=b.ROOT/'maps/japan_hre_source_20261026/geography_correction'
RIVER_CONTROLS={
 'japan':[('信浓川',138.83,37.45),('木曾川',136.94,35.392),('淀川',135.518,34.716),
          ('北上川',141.133,39.693),('汉江',126.936,37.519)],
 'hre':[('美因河',8.678,50.105),('因河',11.397,47.271),('伏尔塔瓦河',14.414,50.086),
        ('内卡河',8.695,49.412),('摩泽尔河',6.629,49.760)]}
CORRIDORS={
 'japan':[('甲府盆地',[(138.45,35.66),(138.57,35.66),(138.69,35.64)]),
          ('松本—安昙野谷地',[(137.87,36.42),(137.97,36.24),(137.98,36.12)]),
          ('长野盆地',[(138.13,36.57),(138.2,36.65),(138.31,36.77)])],
 'hre':[('因斯布鲁克因河谷',[(11.15,47.27),(11.4,47.27),(11.7,47.32)]),
        ('上莱茵河谷',[(7.62,48.22),(7.75,48.58),(8.05,49.02)])]}


def main():
    config=json.loads(b.CONFIG_PATH.read_text(encoding='utf-8'))
    checks=0;results={}
    def check(value,message):
        nonlocal checks
        assert value,message
        checks+=1
    for key in ('japan','hre'):
        folder=b.ROOT/'maps'/key;view=b.MapView(key,config[key])
        sites=json.loads((folder/'buildings.json').read_text(encoding='utf-8'))
        evidence=json.loads((folder/'elevation.json').read_text(encoding='utf-8'))
        by_cell={r['cell']:r for r in evidence['cells']}
        metadata=json.loads((folder/'map.json').read_text(encoding='utf-8'))
        actual={};shifts=[]
        for site in sites:
            lo,la=map(float,view.lonlat(*site['world_pixel']))
            shift=audit.kilometres(site['lon'],site['lat'],lo,la)
            check(abs(shift-site['displacement_km'])<.001,key+' true kilometre displacement')
            actual[site['name']]=(lo,la)
            if site['kind']=='city':
                check(shift<=12,key+' real city anchor '+site['name'])
                shifts.append(shift)
        if key=='japan':
            kyoto,osaka,nara,otsu=[actual[n] for n in ('京都','大阪','奈良','大津')]
            check(kyoto[0]>osaka[0] and kyoto[1]>osaka[1],'Kyoto northeast of Osaka')
            check(nara[0]>osaka[0] and otsu[0]>kyoto[0],'Nara east of Osaka, Otsu east of Kyoto')
        lowlands=[]
        for name,lo,la in audit.VALLEYS[key]:
            row=by_cell[view.nearest(lo,la)]
            check(row['classification'] in ('plain','hill'),name+' raw DEM not a mountain; independent of building override')
            lowlands.append(dict(name=name,cell=row['cell'],classification=row['classification']))
        rivers=[]
        for name,lo,la in RIVER_CONTROLS[key]:
            geometries=[LineString(path) for river in metadata['rivers'] if river['name']==name for path in river['paths']]
            check(bool(geometries),name+' exported')
            point=Point(*view.xy(lo,la));nearest=nearest_points(point,unary_union(geometries))[1]
            rlo,rla=map(float,view.lonlat(nearest.x,nearest.y))
            gap=audit.kilometres(lo,la,rlo,rla)
            check(gap<3,name+' real reference river position '+str(gap))
            rivers.append(dict(name=name,reference=[lo,la],distance_km=round(gap,3)))
        corridors=[]
        for name,coords in CORRIDORS[key]:
            line=LineString([view.xy(*p) for p in coords])
            mask=b.contains_xy(line.buffer(8/view.km_per_pixel),view.x,view.y)
            allowed={c for c,r in by_cell.items() if mask[c] and r['classification'] in ('plain','hill')}
            anchors=[]
            for lo,la in (coords[0],coords[-1]):
                cell=view.nearest(lo,la)
                options=[c for c in allowed if b.hex_distance(c,cell,view.width)<=1]
                check(bool(options),name+' lowland endpoint at map resolution')
                x,y=view.xy(lo,la)
                anchors.append(min(options,key=lambda c:(view.x[c]-x)**2+(view.y[c]-y)**2))
            start,goal=anchors
            queue=deque([start]);paths={start:[start]}
            while queue and goal not in paths:
                cell=queue.popleft()
                for other in b.neighbors(cell,view.width,view.height):
                    if other in allowed and other not in paths:paths[other]=paths[cell]+[other];queue.append(other)
            check(goal in paths,name+' continuous raw-DEM lowland corridor')
            path=paths[goal]
            check(len(path)>=3,name+' spans multiple terrain cells')
            corridors.append(dict(name=name,reference=coords,cells=path,raw_classes=[by_cell[c]['classification'] for c in path]))
        previous=json.loads((b.ROOT/'maps/japan_hre_source_20261026/geography_audit/audit.json').read_text(encoding='utf-8'))['maps'][key]
        results[key]=dict(city_count=len(shifts),maximum_city_shift_km=round(max(shifts),3),
            median_city_shift_km=round(float(b.np.median(shifts)),3),cities_over_10km=sum(s>10 for s in shifts),
            previous_cities_over_10km=previous['city_shift_over_10km'],lowland_controls=lowlands,
            river_controls=rivers,corridors=corridors,river_names=sorted({r['name'] for r in metadata['rivers']}))
    result=dict(checks=checks,errors=0,scope='Real geography at coarse hex resolution; no claim of exact historical shoreline or every city independently surveyed',maps=results)
    OUT.mkdir(parents=True,exist_ok=True)
    b.write_json(OUT/'verification.json',result)
    print(json.dumps(result,ensure_ascii=False))


if __name__=='__main__':main()
