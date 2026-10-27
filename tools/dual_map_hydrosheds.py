"""Select real major river reaches from HydroRIVERS v1.0; never draw river paths by hand."""

import hashlib
import io
import json
from pathlib import Path
import sys
from zipfile import ZipFile

sys.path.insert(0,str(Path.home()/'.codex/tools/ew4-inspect-deps'))
import shapefile
from shapely.geometry import Point, shape, box, mapping
from shapely.ops import unary_union, linemerge
from shapely.strtree import STRtree

ROOT=Path(__file__).resolve().parents[1]
CACHE=ROOT/'maps/source_cache'
LICENSE='HydroSHEDS License Agreement; free for non-commercial and commercial use'
SEEDS={
 'japan':[
  ('石狩川',141.4,43.2),('天盐川',141.75,44.85),('十胜川',143.45,42.75),
  ('钏路川',144.38,43.02),('信浓川',138.83,37.45),('阿贺野川',139.16,37.95),
  ('最上川',139.85,38.91),('北上川',141.3,38.65),('雄物川',140.15,39.65),
  ('阿武隈川',140.86,37.99),('利根川',140.5,35.88),('荒川',139.64,35.88),
  ('多摩川',139.48,35.63),('木曾川',136.94,35.392),('长良川',136.774,35.446),
  ('揖斐川',136.63,35.22),('天龙川',137.82,34.82),('富士川',138.5,35.3),
  ('淀川',135.55,34.75),('大和川',135.58,34.6),('熊野川',135.91,33.85),
  ('吉野川',134.3,34.07),('四万十川',132.94,33.02),('筑后川',130.57,33.29),
  ('球磨川',130.7,32.4),('大淀川',131.36,31.92),('汉江',126.94,37.52),
  ('洛东江',128.94,35.36),('锦江',127.1,36.1),('大同江',125.75,39.02),
  ('鸭绿江',124.43,40.13),('图们江',130.4,42.9)],
 'hre':[
  ('莱茵河',6.68,51.55),('摩泽尔河',7.5,50.32),('美因河',8.4,50.0),('内卡河',8.695,49.412),
  ('易北河',9.6,53.58),('伏尔塔瓦河',14.4,50.13),('萨勒河',11.9,51.85),
  ('施普雷河',13.43,52.49),('哈弗尔河',12.336,52.606),('威悉河',8.8,52.95),
  ('富尔达河',9.55,51.34),('韦拉河',9.8,51.4),('奥得河',14.43,52.85),
  ('维斯瓦河',18.8,54.1),('多瑙河',19.05,47.5),('因河',13.4,48.53),
  ('伊萨尔河',12.45,48.73),('莱希河',10.89,48.62),('萨尔察赫河',12.85,48.05),
  ('恩斯河',14.48,48.12),('穆尔河',15.97,46.69),('德拉瓦河',16.7,46.32),
  ('萨瓦河',18.7,45.05),('摩拉瓦河',16.94,48.58),('波河',11.9,44.99),
  ('阿迪杰河',11.85,45.16),('阿达河',9.82,45.22),('提契诺河',9.07,45.23),
  ('阿尔诺河',10.65,43.68),('罗讷河',4.8,45.65),('塞纳河',1.1,49.4),
  ('默兹河',5.9,51.75),('斯海尔德河',4.29,51.34)]}


def acquire(key):
    out=CACHE/'hydrorivers_20261026'/f'{key}.geojson'
    revision=hashlib.sha256(json.dumps(dict(schema=2,seeds=SEEDS[key]),sort_keys=True).encode()).hexdigest()
    if out.exists():
        cached=json.loads(out.read_text(encoding='utf-8'))
        if cached.get('selection_revision')==revision and cached.get('license')==LICENSE:return out
    region='as' if key=='japan' else 'eu'
    archive_path=CACHE/f'HydroRIVERS_v10_{region}_shp.zip'
    bounds=(122,24,153,50) if key=='japan' else (-8,39,32,63)
    fields=['HYRIV_ID','NEXT_DOWN','UPLAND_SKM','DIS_AV_CMS']
    print(f'{key}: selecting HydroRIVERS source reaches',flush=True)
    with ZipFile(archive_path) as archive:
        stem=next(n[:-4] for n in archive.namelist() if n.endswith('.shp'))
        reader=shapefile.Reader(**{ext:io.BytesIO(archive.read(stem+'.'+ext)) for ext in ('shp','shx','dbf')})
        reaches=[]
        for record in reader.iterShapeRecords(bbox=bounds,fields=fields):
            attrs=record.record.as_dict()
            if attrs['UPLAND_SKM']<100:continue
            geom=shape(record.shape.__geo_interface__)
            reaches.append((geom,attrs))
    geometries=[g for g,_ in reaches]
    tree=STRtree(geometries)
    by_id={r['HYRIV_ID']:(g,r) for g,r in reaches}
    upstream={}
    for g,r in reaches:upstream.setdefault(r['NEXT_DOWN'],[]).append(r['HYRIV_ID'])
    names={};anchors=[]
    for name,lon,lat in SEEDS[key]:
        point=Point(lon,lat)
        index=int(tree.nearest(point))
        geometry,record=reaches[index]
        gap=geometry.distance(point)*111
        assert gap<12,(name,gap)
        current=record['HYRIV_ID'];seen=set()
        while current in by_id and current not in seen:
            seen.add(current)
            names.setdefault(current,name)
            candidates=upstream.get(current,[])
            if not candidates:break
            current=max(candidates,key=lambda c:by_id[c][1]['UPLAND_SKM'])
        current=record['NEXT_DOWN']
        while current in by_id and current not in seen and current not in names:
            g,down=by_id[current]
            if down['UPLAND_SKM']>record['UPLAND_SKM']*2.5:break
            seen.add(current);names[current]=name;current=down['NEXT_DOWN']
        anchors.append(dict(name=name,reference=[lon,lat],seed_reach=record['HYRIV_ID'],gap_approx_km=round(gap,3),reaches=len(seen)))
    threshold=700 if key=='japan' else 1800
    groups={}
    clip=box(*bounds)
    for g,r in reaches:
        if r['UPLAND_SKM']<threshold and r['HYRIV_ID'] not in names:continue
        name=names.get(r['HYRIV_ID'],'主要支流水系')
        groups.setdefault(name,[]).append(g.intersection(clip))
    features=[]
    for name,geoms in sorted(groups.items()):
        geom=unary_union(geoms)
        if geom.geom_type=='MultiLineString':geom=linemerge(geom)
        features.append(dict(type='Feature',properties=dict(name=name),geometry=mapping(geom.simplify(.0002))))
    result=dict(type='FeatureCollection',selection_revision=revision,provider='HydroSHEDS HydroRIVERS v1.0',license=LICENSE,
        attribution='HydroRIVERS: Lehner, B. and Grill, G. (2013), Hydrological Processes 27(15), 2171-2186, doi:10.1002/hyp.9740',
        source=dict(url=f'https://data.hydrosheds.org/file/HydroRIVERS/HydroRIVERS_v10_{region}_shp.zip',
            path=str(archive_path.relative_to(ROOT)),sha256=hashlib.sha256(archive_path.read_bytes()).hexdigest()),
        method='River geometry from HydroRIVERS; names assigned using reference seeds and largest upstream catchment; additional major tributaries retained',
        minimum_unnamed_catchment_km2=threshold,anchors=anchors,features=features)
    out.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
    print(f'{key}: {len(features)} river groups from {sum(len(g) for g in groups.values())} source reaches',flush=True)
    return out


def load(key):
    data=json.loads(acquire(key).read_text(encoding='utf-8'))
    return [(shape(f['geometry']),f['properties']['name']) for f in data['features']]


if __name__=='__main__':
    for key in ('japan','hre'):acquire(key)
