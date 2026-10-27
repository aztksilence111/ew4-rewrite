"""Validate native material variety, regional relief and protected map data."""

import argparse
import base64
from collections import Counter
import hashlib
import json
from pathlib import Path
import zlib

import build_japan_hre_maps as b


REGIONS=[
    ('North German Plain',(10.5,52.4,13.6,53.5),'plain',.95),
    ('Po Plain',(9.4,44.8,11.5,45.2),'plain',.85),
    ('Upper Rhine',(7.55,48.2,7.85,48.9),'plain',.95),
    ('Hungarian Plain',(19.5,46.8,21,47.6),'plain',.95),
    ('Alps',(9,46.2,12.5,47.2),'alpine',.7),
    ('Vosges',(7,47.8,7.25,48.6),'upland',.5),
    ('Bohemian Forest',(13,48.6,13.7,49.3),'upland',.5),
    ('Harz',(10.2,51.5,11.1,51.9),'upland',.25),
]


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline',type=Path)
    args=parser.parse_args()
    baseline=json.loads(args.baseline.read_text(encoding='utf-8')) if args.baseline else None
    config=json.loads(b.CONFIG_PATH.read_text(encoding='utf-8'))
    checks=0;results={}
    def check(value,message):
        nonlocal checks
        assert value,message
        checks+=1
    for key in ('japan','hre'):
        folder=b.ROOT/'maps'/key
        metadata=json.loads((folder/'map.json').read_text(encoding='utf-8'))
        terrain=b.np.frombuffer((folder/(key+'.bin')).read_bytes()[16:],dtype=b.np.uint8).reshape(-1,12)
        artwork={r['cell']:r for r in metadata['overlays'] if r.get('cellKind') in (8,9)}
        evidence=json.loads((folder/'elevation.json').read_text(encoding='utf-8'))
        samples={s['cell']:s for s in evidence['cells']}
        for kind,stem in ((8,'hill'),(9,'mountain')):
            check({r['image'] for r in artwork.values() if r['cellKind']==kind}=={f'{stem}_{i}.png' for i in range(1,7)},key+' all six original '+stem+' shapes')
        check(not metadata.get('ridgeOverlays'),key+' no repeated infill stamps')
        check(not b.np.any(terrain[:,0]==6),key+' no random forest scattering')
        for cell,row in artwork.items():
            check(row['image']==metadata['terrainRules'][row['cellKind']]['sprites'][str(int(terrain[cell,1]))],key+' native texture and BIN agree')
            check(row['scaleX']==row['scaleY'],key+' native aspect ratio')
            check(0<=samples[cell]['slope_aspect_degrees']<=360,key+' DEM aspect recorded')
        remaining=set(artwork);patches={8:[],9:[]}
        while remaining:
            seed=remaining.pop();item=artwork[seed];frontier=[seed];size=0
            while frontier:
                cell=frontier.pop();size+=1
                for other in b.neighbors(cell,metadata['gridWidth'],metadata['gridHeight']):
                    if other in remaining and artwork[other]['image']==item['image']:
                        remaining.remove(other);frontier.append(other)
            patches[item['cellKind']].append(size)
        check(max(patches[8])<=10 and max(patches[9])<=10,key+' no large connected patches of one repeated material')
        hill_heights=[b.ART['atlases']['tiles'][r['image']]['h']*r['scaleY'] for r in artwork.values() if r['cellKind']==8]
        low_heights=[b.ART['atlases']['tiles'][r['image']]['h']*r['scaleY'] for r in artwork.values() if r['relief']=='low']
        check(max(hill_heights)<min(low_heights),key+' rugged hills remain below low mountains')
        result=dict(terrainCounts=dict(Counter(map(int,terrain[:,0]))),
            materialCounts=dict(Counter(r['image'] for r in artwork.values())),
            maximumSameMaterialPatch={str(kind):max(sizes) for kind,sizes in patches.items()})
        if baseline:
            before=b.np.frombuffer(zlib.decompress(base64.b64decode(baseline[key]['binZ']))[16:],dtype=b.np.uint8).reshape(-1,12)
            check(before.shape==terrain.shape,key+' frame retained')
            check(b.np.array_equal(before[:,2:],terrain[:,2:]),key+' rivers and other legacy flags retained')
            check(b.np.array_equal(before[:,0]==1,terrain[:,0]==1),key+' sea and coastline retained')
            if key=='japan':
                check(b.np.array_equal(before[:,0],terrain[:,0]),'Japan terrain types retained; material detail only')
            check(hashlib.sha256((folder/'buildings.json').read_bytes()).hexdigest()==baseline[key]['buildings'],key+' building positions and identities retained')
            result['baselineComparison']=dict(changedTerrainKinds=int(b.np.count_nonzero(before[:,0]!=terrain[:,0])),
                changedSpriteIndices=int(b.np.count_nonzero(before[:,1]!=terrain[:,1])),changedRiverOrOtherFlags=0,buildingsUnchanged=True)
        if key=='hre':
            view=b.MapView(key,config[key]);lon,lat=view.lonlat(view.x,view.y);regions=[]
            for name,(left,bottom,right,top),category,minimum in REGIONS:
                cells=b.np.flatnonzero((lon>=left)&(lon<=right)&(lat>=bottom)&(lat<=top)&(terrain[:,0]!=1))
                classes=Counter(samples[int(cell)]['classification'] for cell in cells)
                kinds=Counter(map(int,terrain[cells,0]))
                check(len(cells)>=20,name+' regional window covers multiple cells')
                if category=='plain':
                    check(kinds[0]/len(cells)>=minimum,name+' continuous lowland remains open')
                    remaining={int(c) for c in cells if terrain[c,0]==0};components=[]
                    while remaining:
                        frontier=[remaining.pop()];size=0
                        while frontier:
                            cell=frontier.pop();size+=1
                            for other in b.neighbors(cell,metadata['gridWidth'],metadata['gridHeight']):
                                if other in remaining:
                                    remaining.remove(other);frontier.append(other)
                        components.append(size)
                    check(max(components)>=kinds[0]*.7,name+' connected open plain component')
                elif category=='alpine':
                    check((classes['middle']+classes['high'])/len(cells)>=minimum,name+' measured high mountain core')
                else:
                    check((classes['hill']+classes['low'])/len(cells)>=minimum,name+' lower mountain and hill relief')
                    check(classes['high']==0,name+' not painted as alpine high peaks')
                regions.append(dict(name=name,cells=len(cells),terrainKinds=dict(kinds),measuredRelief=dict(classes)))
            result['regions']=regions
        results[key]=result
    report=dict(checks=checks,errors=0,maps=results,scope='Native material variety and DEM regional differences; no claim of complete geological or historical survey')
    b.write_json(b.ROOT/'maps/japan_hre_source_20261026/regional_materials_verification.json',report)
    print(json.dumps(dict(checks=checks,errors=0,maps={k:{'maximumSameMaterialPatch':v['maximumSameMaterialPatch'],'terrainCounts':v['terrainCounts'],'baselineComparison':v.get('baselineComparison')} for k,v in results.items()})))


if __name__=='__main__':
    main()
