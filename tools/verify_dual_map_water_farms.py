"""Check actual farm/port cells and native-resolution water continuity."""

from collections import Counter
from functools import lru_cache
import math

import build_japan_hre_maps as source
import numpy as np
from PIL import Image
from shapely.geometry import Point


def main():
    checks=0;results={}
    def check(value,label):
        nonlocal checks
        checks+=1
        assert value,label
    for key in ('japan','hre'):
        folder=source.ROOT/'maps'/key
        config=source.json.loads((source.ROOT/'maps/dual_map_config.json').read_text(encoding='utf-8'))[key]
        view=source.MapView(key,config)
        land,*_=source.geography(view)
        sites=source.json.loads((folder/'buildings.json').read_text(encoding='utf-8'))
        farms=source.json.loads((folder/'farmland.json').read_text(encoding='utf-8'))['records']
        water=source.json.loads((source.ROOT/'maps/japan_hre_source_20261026'/(key+'-water.json')).read_text(encoding='utf-8'))
        raw=np.frombuffer((folder/(key+'.bin')).read_bytes(),dtype=np.uint8,offset=16).reshape((-1,12))
        occupied={s['cell'] for s in sites}
        for i,farm in enumerate(farms):
            check(1<=farm['level']<=3 and raw[farm['cell'],0]==0 and farm['cell'] not in occupied,'Native tier, real flat land, empty farm cell')
            check(land.contains(Point(farm['world_pixel'])),'Actual natural land farm')
            check(all(source.hex_distance(farm['cell'],other['cell'],view.width)>=3 for other in farms[:i]),'Farm spacing')
        for site in sites:
            if site['kind']!='port':
                continue
            check(raw[site['cell'],0]==1,'Port is on an actual water cell')
            dx,dy=site['coast_vector']
            style=(2 if dx>0 else 3) if abs(dx)>abs(dy) else (4 if dy>0 else 1)
            check(style==site['building_style'] and site['displacement_km']<=24,'Native orientation and geographic shift bound')
            check(site.get('river_berth') or 6<=site['shore_gap_world']<=38,'Native sprite reaches real coast')
        @lru_cache(maxsize=12)
        def tile(left,top):
            with Image.open(source.BASE/'client/maps'/key/'water-detail'/f'{left}_{top}.png') as image:
                return np.asarray(image.convert('RGBA')).copy()
        def pixel(x,y):
            left,top=int(x//1024)*1024,int(y//1024)*1024
            image=tile(left,top)
            px,py=int(x-left),int(y-top)
            area=image[max(0,py-2):min(image.shape[0],py+3),max(0,px-2):min(image.shape[1],px+3)]
            rgb=area[:,:,:3].astype(float)
            difference=np.minimum(np.linalg.norm(rgb-[68,102,136],axis=2),np.linalg.norm(rgb-[85,136,170],axis=2))
            return bool(np.any((difference<28)&(area[:,:,3]>220)))
        samples=0
        for mouth in water['mouths']:
            points=mouth['path']
            for a,b in zip(points,points[1:]):
                steps=max(1,math.ceil(math.dist(a,b)/3))
                for n in range(steps+1):
                    x,y=[a[d]+(b[d]-a[d])*n/steps for d in (0,1)]
                    if not (0<=x<view.world_width-2 and 0<=y<view.world_height-2):
                        continue
                    check(pixel(x,y),'Open water core through mouth '+mouth['name']+' '+str((round(x),round(y))))
                    samples+=1
        check(not water['unresolvedMouths'],'No unresolved coastal source seams')
        levels=dict(sorted(Counter(f['level'] for f in farms).items()))
        check(set(levels)=={1,2,3},'Three native farm tiers')
        results[key]=dict(farms=len(farms),farmLevels=levels,mouths=len(water['mouths']),mouthSamples=samples,ports=sum(s['kind']=='port' for s in sites),riverPropertyCellsUpdated=water.get('riverPropertyCellsUpdated',0))
        tile.cache_clear()
    result=dict(checks=checks,errors=0,maps=results)
    source.write_json(source.ROOT/'maps/japan_hre_source_20261026/water-farm-source-verification.json',result)
    print(source.json.dumps(result))


if __name__=='__main__':
    main()
