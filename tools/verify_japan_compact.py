"""Validate the cropped, finer Japan grid and source-based historical planning."""

import base64
import json

import build_japan_hre_maps as b
import japan_sengoku as sengoku


def main():
    config=json.loads(b.CONFIG_PATH.read_text(encoding='utf-8'))
    view=b.MapView('japan',config['japan'])
    out=b.ROOT/'maps/japan'
    baseline=json.loads((out/'compact_baseline.json').read_text(encoding='utf-8'))
    metadata=json.loads((out/'map.json').read_text(encoding='utf-8'))
    sites=json.loads((out/'buildings.json').read_text(encoding='utf-8'))
    terrain=b.TerrainMap.from_bytes((out/'japan.bin').read_bytes())
    revision=json.loads((out/'sengoku_plan.json').read_text(encoding='utf-8'))
    checks=0
    def check(value,message):
        nonlocal checks
        assert value,message
        checks+=1
    check((view.width,view.height)==(267,217),'cropped and finer grid')
    check(len(terrain.cells)==metadata['totalCells']==57939<baseline['cells'],'total cells reduced')
    check(config['japan']['gridStepKm']==8,'extra deployment cells from finer sampling')
    check(metadata['landCells']+metadata['waterCells']==57939,'all cells accounted for')
    original=json.loads((b.plan.OUT/'japan_points.json').read_text(encoding='utf-8'))['records']
    excluded=set(config['japan']['excludedSiteNames'])
    retained={s['id'] for s in original if s['name'] not in excluded}
    new_ids={s['id'] for s in revision['additionalSites']}
    check({s['id'] for s in sites}==retained|new_ids,'only requested removals and declared new nodes')
    check(not excluded.intersection(s['name'] for s in sites),'excluded island buildings removed')
    check(sum(s['kind']=='city' and s['group']=='朝鲜半岛' for s in sites)==23,'sparse Korea retained')
    check(any(s['name']=='海参崴' for s in sites),'Vladivostok retained')
    check(all(0<=s['world_pixel'][0]<view.world_width and 0<=s['world_pixel'][1]<view.world_height for s in sites),'all buildings inside frame')
    for name,lon,lat in config['japan']['islands']:
        x,y=view.xy(lon,lat)
        check(0<x<view.world_width and 0<y<view.world_height,'retained island framed: '+name)
    for site in sites:
        if site.get('minimumSpacing'):
            check(all(b.hex_distance(site['cell'],other['cell'],view.width)>=4 for other in sites if other['id']!=site['id'] and other['kind']!='port'),'new or converted node deployment space: '+site['name'])
        if site.get('castleReference'):
            expected=revision['siteOverrides'].get(site['id'],next((s for s in revision['additionalSites'] if s['id']==site['id']),None))
            check(expected is not None and (site['lon'],site['lat'])==(expected['lon'],expected['lat']),'castle source anchor unchanged')
    historical=metadata['historicalGeography']
    dry=b.np.array([c.background_kind!=1 for c in terrain.cells])
    lon,lat=view.lonlat(view.x,view.y)
    for field,layer in [('province','kuni_polygons'),('district','gun_polygons')]:
        ids=b.np.frombuffer(base64.b64decode(historical[field+'Cells']),dtype='<u2')
        check(len(ids)==len(terrain.cells),'historical geography covers full grid')
        check(not b.np.any(ids[~dry]),'sea has no land jurisdiction')
        features,geometries,_=sengoku.layers()[layer]
        check(int(ids.max())<=len(features),'historical geography IDs valid')
        for index,geometry in enumerate(geometries,1):
            selected=ids==index
            check(bool(b.np.all(b.contains_xy(geometry,lon[selected],lat[selected]))),'assigned grid centres inside real source boundary')
    required={'上総国','丹後国','伊豆国','伯耆国','備中国','若狭国','佐渡国','伊賀国'}
    check(required.issubset({s.get('province') for s in sites if s['kind']=='city'}),'eight missing old-country city supports resolved')
    changed_hre=[name for name,sha in baseline['hreHashes'].items() if b.digest(b.ROOT/'maps/hre'/name)!=sha]
    check(b.digest(b.ROOT/'maps/hre/buildings.json')==baseline['hreHashes']['buildings.json'],'HRE approved building positions and identities retained')
    hre=json.loads((b.ROOT/'maps/hre/map.json').read_text(encoding='utf-8'))
    check((hre['gridWidth'],hre['gridHeight'],hre['totalCells'])==(201,261,52461),'HRE frame retained during independent terrain revision')
    scope=dict(previousGrid=baseline['grid'],previousCells=baseline['cells'],grid=[view.width,view.height],cells=57939,
        reductionPercent=round((1-57939/baseline['cells'])*100,2),referenceStepKm=8,
        removed=[dict(id=s['id'],name=s['name'],kind=s['kind']) for s in original if s['name'] in excluded],
        newSites=revision['additionalSites'],converted=['伊贺马厩→伊贺上野城','相川工厂→杂太佐渡城'],
        castleAnchors=len(revision['siteOverrides']),buildings=len(sites),hreChangedFiles=changed_hre,
        checks=checks,errors=0,source=sengoku.URL)
    b.write_json(out/'compact_verification.json',scope)
    print(json.dumps({k:scope[k] for k in ('previousCells','cells','reductionPercent','buildings','castleAnchors','checks','errors')},ensure_ascii=False))


if __name__=='__main__':
    main()
