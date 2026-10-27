"""Check DEM registration, geographic controls and map-count consistency."""

from collections import Counter
import json

import build_japan_hre_maps as b
import dual_map_relief as dem

CONTROLS={
    'japan':[
        ('Daisetsu',142.854,43.664,'mountain'),('Hidaka',142.882,42.85,'mountain'),
        ('Hida Hotaka',137.648,36.29,'mountain'),('Akaishi',138.228,35.674,'mountain'),
        ('Fuji',138.727,35.36,'mountain'),('Kii',135.897,34.172,'mountain'),
        ('Shikoku Ishizuchi',133.115,33.77,'mountain'),('Daisen',133.545,35.37,'mountain'),
        ('Yakushima',130.504,30.337,'mountain'),('Tokyo plain',139.76,35.68,'lowland'),
        ('Nobi plain',136.8,35.2,'lowland'),('Ishikari plain',141.65,43.15,'lowland'),
        ('Osaka plain',135.48,34.69,'lowland')],
    'hre':[
        ('Mont Blanc',6.865,45.833,'mountain'),('Matterhorn',7.658,45.976,'mountain'),
        ('Grossglockner',12.693,47.075,'mountain'),('Vosges',7.098,47.9,'mountain'),
        ('Black Forest',8.004,47.872,'mountain'),('Harz',10.615,51.8,'mountain'),
        ('Ore Mountains',13.0,50.4,'mountain'),('Apennines',10.7,44.2,'mountain'),
        ('Berlin plain',13.4,52.5,'lowland'),('Holland polders',4.715,52.01,'lowland'),
        ('Warsaw plain',21.01,52.23,'lowland'),('Marchfeld basin',16.7,48.25,'lowland'),
        ('Po plain',10.045,45.06,'lowland'),('Hungarian plain',20.5,47.3,'lowland')]
}


def main():
    config=json.loads(b.CONFIG_PATH.read_text(encoding='utf-8'))
    results={}
    checks=0
    def check(value,message):
        nonlocal checks
        assert value,message
        checks+=1
    for key in ('japan','hre'):
        folder=b.ROOT/'maps'/key
        view=b.MapView(key,config[key])
        evidence=json.loads((folder/'elevation.json').read_text(encoding='utf-8'))
        by_cell={r['cell']:r for r in evidence['cells']}
        source=b.source_sites(key,config[key])
        lon=b.np.array([s['lon'] for s in source]);lat=b.np.array([s['lat'] for s in source])
        inverse_lon,inverse_lat=view.lonlat(*view.xy(lon,lat))
        check(float(b.np.max(abs(inverse_lon-lon)))<1e-8 and float(b.np.max(abs(inverse_lat-lat)))<1e-8,key+' inverse projection')
        check(evidence['policy']==dict(dem.POLICY,**config[key].get('terrainPolicy',{})),key+' configured terrain thresholds')
        check(evidence['sources']['policy']==evidence['policy'],key+' source metadata records effective regional thresholds')
        check(all(r['local_relief_m']>=0 for r in evidence['cells']),key+' local relief nonnegative')
        controls=[]
        for name,lon,lat,expected in CONTROLS[key]:
            center=view.nearest(lon,lat)
            options=[r for cell,r in by_cell.items() if b.hex_distance(cell,center,view.width)<=1]
            check(bool(options),name+' has land samples')
            if expected=='mountain':
                mountains=[r for r in options if r['classification'] in ('low','middle','high')]
                check(bool(mountains),name+' mountainous in DEM')
                selected=max(mountains,key=lambda r:r['center_m'])
            else:
                selected=by_cell.get(center,min(options,key=lambda r:r['local_relief_m']))
                check(selected['classification'] in ('plain','hill'),name+' not a mountain wall')
            controls.append(dict(name=name,expected=expected,**selected))
        raw=(folder/(key+'.bin')).read_bytes()
        terrain=b.TerrainMap.from_bytes(raw)
        check(terrain.width*terrain.height==len(terrain.cells)==view.width*view.height,key+' exact full-grid count')
        kinds=Counter(c.background_kind for c in terrain.cells)
        report=json.loads((folder/'verification.json').read_text(encoding='utf-8'))
        artwork=json.loads((folder/'map.json').read_text(encoding='utf-8'))
        glyphs=[r for r in artwork['overlays'] if r.get('relief') in ('hill','low','middle','high')]
        glyph_cells={r['cell'] for r in glyphs}
        expected_cells={cell for cell,c in enumerate(terrain.cells) if c.background_kind in (8,9)}
        check(glyph_cells==expected_cells and len(glyphs)==len(glyph_cells),key+' every hill and mountain attribute has exactly one game sprite')
        check(all(by_cell[r['cell']]['classification']==r['relief'] for r in glyphs),key+' relief symbols grounded in DEM')
        check(all(r.get('sourceImage',r['image'])==artwork['terrainRules'][r['cellKind']]['sprites'][str(r['cellIndex'])] for r in glyphs),key+' original game sprite matches cell definition')
        hierarchy=config[key].get('terrainAppearance')=='native-height-hierarchy'
        bands=config[key].get('terrainAppearance')=='native-range-bands'
        check(all(r['opacity']==1 and r['scaleX']==r['scaleY'] and b.ART['atlases']['tiles'][r['image']]['w']*r['scaleX']<=(124.001 if bands else 74.001 if hierarchy else 60.001) and b.ART['atlases']['tiles'][r['image']]['h']*r['scaleY']<=(84.001 if bands else 52.001 if hierarchy else 46.001) for r in glyphs),key+' native artwork respects grade-specific footprint')
        if hierarchy:
            check(all(r['cellIndex']<=1 and b.ART['atlases']['tiles'][r['image']]['h']*r['scaleY']<=24.001 for r in glyphs if r['cellKind']==8),key+' hills do not use mountain-like native variants')
        if bands:
            check(all(r['image']==f"mountain_{r['cellIndex']+1}.png" for r in glyphs if r['cellKind']==9),key+' exact original mountain sprites without derived shapes')
        import base64
        properties=b.np.frombuffer(base64.b64decode(artwork['terrainCells']),dtype=b.np.uint8).reshape(-1,4)
        check(len(properties)==len(terrain.cells),key+' full-grid terrain properties exported')
        check(all(tuple(properties[i,:3])==(c.background_kind,c.background_index,c.river_flags) for i,c in enumerate(terrain.cells)),key+' displayed cell attributes match BIN')
        check(artwork['cartography']['base']=='native-game-texture' and report['rendering']['elevationImage'] is False,key+' native base without DEM image shading')
        check({r[0] for r in config[key]['plains']}.issubset({r['textZh'] for r in artwork['labels']}),key+' named plains preserved')
        peak_cells=[r['cell'] for r in glyphs if r['relief']!='hill']
        for name,lon,lat,expected in CONTROLS[key]:
            if expected!='mountain':continue
            center=view.nearest(lon,lat)
            check(min(b.hex_distance(center,c,view.width) for c in peak_cells)<=1,name+' retains a visible mountain symbol')
        check(report['land_cells']+report['water_cells']==report['cells']==len(terrain.cells),key+' land plus water')
        check(report['water_cells']==kinds[1],key+' exact water count')
        sites=json.loads((folder/'buildings.json').read_text(encoding='utf-8'))
        identities=lambda rows:{(r['id'],r['name'],r['kind']) for r in rows}
        check(identities(sites)==identities(source),key+' approved building identities unchanged')
        actual={r['cell']:r for r in sites}
        overrides={r['cell'] for r in evidence['overrides']}
        for cell,r in by_cell.items():
            if cell in overrides:continue
            expected=9 if r['classification'] in ('low','middle','high') else 8 if r['classification']=='hill' else None
            if expected is not None:check(terrain.cells[cell].background_kind==expected,key+' classification persisted')
        check(len(actual)==len(source),key+' approved building count')
        results[key]=dict(controls=controls,terrain_counts={str(k):v for k,v in kinds.items()},
            visible_game_terrain=dict(Counter(r['cellKind'] for r in glyphs)),
            dem_classes=dict(Counter(r['classification'] for r in evidence['cells'])),
            full_grid=[view.width,view.height],total_cells=len(terrain.cells),land_cells=report['land_cells'],water_cells=report['water_cells'])
    result=dict(checks=checks,errors=0,policy=dem.POLICY,maps=results)
    b.write_json(b.ROOT/'maps/japan_hre_source_20261026/relief_verification.json',result)
    print(json.dumps(dict(checks=checks,errors=0,maps={k:{v:r[v] for v in ('total_cells','land_cells','water_cells','dem_classes')} for k,r in results.items()})))


if __name__=='__main__':
    main()
