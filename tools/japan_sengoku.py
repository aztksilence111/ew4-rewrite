"""Register Ufirst historical geography to the existing neutral Japan hex grid."""

from collections import defaultdict
import base64
import gzip
import hashlib
import json
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path.home()/'.codex/tools/ew4-inspect-deps'))
import numpy as np
from shapely import contains_xy, transform
from shapely.geometry import Point, box, mapping, shape
from shapely.ops import unary_union
from shapely.strtree import STRtree

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT/'maps/source_cache/sengoku_ufirst_20261026'
OUT = ROOT/'maps/japan'
URL = 'https://samurai.ufirst.jp/sengoku-map/zh/'

# Only reviewed existing nodes are reanchored; the mixed-period castle catalogue
# is not a list of settlements that all existed at a single scenario date.
CASTLES = {
    '近江八幡': ('安土城', '安土'),
    '福井': ('一乗谷朝倉氏遺跡', '一乘谷'),
    '高田': ('春日山城', '春日山'),
    '松江': ('月山富田城', '月山富田'),
    '甲府': ('躑躅ヶ崎館', '甲府'),
    '七尾': ('七尾城', '七尾'),
    '岐阜': ('岐阜城', '岐阜'),
    '清洲': ('清洲城', '清洲'),
    '冈崎': ('岡崎城', '冈崎'),
    '滨松': ('浜松城', '滨松'),
    '骏府': ('駿府城', '骏府'),
    '上田': ('上田城', '上田'),
    '松本': ('松本城', '松本'),
    '小诸': ('小諸城', '小诸'),
    '小田原': ('小田原城', '小田原'),
    '大垣': ('大垣城', '大垣'),
    '宇都宫': ('宇都宮城', '宇都宫'),
    '川越': ('川越城', '川越'),
    '忍': ('忍城', '忍'),
    '村上': ('村上城', '村上'),
    '米泽': ('米沢城', '米泽'),
    '馆山': ('館山城', '馆山'),
    '鸟取': ('鳥取城', '鸟取'),
    '大阪': ('石山本願寺', '大阪'),
    '伊贺': ('上野城', '伊贺上野'),
    '相川': ('雑太城', '杂太佐渡'),
}

ADDITIONS = [
    ('久留里城', '久留里', '关东'), ('宮津城', '宫津', '近畿'),
    ('韮山城', '韮山', '中部'), ('羽衣石城', '羽衣石', '中国'),
    ('松山城 (備中国)', '备中松山', '中国'), ('後瀬山城', '后濑山', '近畿'),
]


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(',', ':'))+'\n', encoding='utf-8')


def prepare():
    from pmtiles.reader import Reader, MmapSource
    import mapbox_vector_tile
    grouped = defaultdict(list)
    with (CACHE/'kuni_polygon.pmtiles').open('rb') as stream:
        reader = Reader(MmapSource(stream))
        zoom, size = 7, 128
        for x in range(108, 116):
            for y in range(47, 54):
                raw = reader.get(zoom, x, y)
                if not raw:
                    continue
                decoded = mapbox_vector_tile.decode(gzip.decompress(raw), default_options={'y_coord_down': True})
                for layer_name in ('kuni_polygons', 'gun_polygons'):
                    layer = decoded.get(layer_name)
                    if not layer:
                        continue
                    extent = layer['extent']
                    def lonlat(px, py):
                        u, v = (x+np.asarray(px)/extent)/size, (y+np.asarray(py)/extent)/size
                        return u*360-180, np.degrees(np.arctan(np.sinh(np.pi*(1-2*v))))
                    for feature in layer['features']:
                        geom = shape(feature['geometry']).intersection(box(0, 0, extent, extent))
                        if geom.is_empty:
                            continue
                        geom = transform(geom, lonlat, interleaved=False)
                        props = feature['properties']
                        key = layer_name, props['KUNI'], props.get('GUN', '')
                        grouped[key].append(geom)
    features = []
    for (layer, province, district), geometries in sorted(grouped.items()):
        geom = unary_union(geometries)
        features.append(dict(type='Feature', properties=dict(layer=layer, province=province, district=district), geometry=mapping(geom)))
    write(CACHE/'boundaries.geojson', dict(type='FeatureCollection', source=URL, extractionZoom=7, features=features))
    original = json.loads((ROOT/'maps/settlement_plan_20261026/japan_points.json').read_text(encoding='utf-8'))['records']
    castles = json.loads((CACHE/'shiro.geojson').read_text(encoding='utf-8'))['features']
    overrides = {}
    for old_name, (castle, new_name) in CASTLES.items():
        site = next(s for s in original if s['name'] == old_name)
        matches = [f for f in castles if f['properties']['title'] == castle]
        assert len(matches) == 1, castle
        lon, lat = matches[0]['geometry']['coordinates']
        overrides[site['id']] = dict(name=new_name, lon=lon, lat=lat, originalName=old_name,
                                    castleReference=castle, referenceUrl=URL,
                                    notes=site['notes']+['按所给网站城郭代表点落格；启用年代及名称由具体剧本决定'])
        if old_name in ('伊贺', '相川'):
            overrides[site['id']].update(kind='city', minimumSpacing=4)
    additions = []
    for castle, name, group in ADDITIONS:
        matches = [f for f in castles if f['properties']['title'] == castle]
        assert len(matches) == 1, castle
        lon, lat = matches[0]['geometry']['coordinates']
        additions.append(dict(id='japan-sengoku-'+hashlib.sha256(castle.encode()).hexdigest()[:12],
            name=name, lon=lon, lat=lat, kind='city', group=group, surface='land',
            minimumSpacing=4, castleReference=castle, referenceUrl=URL,
            notes=['补旧国城市支点；与其他陆建至少4格，具体启用年代由剧本决定']))
    write(OUT/'sengoku_plan.json', dict(source=URL, siteOverrides=overrides, additionalSites=additions,
          policy='Neutral historical-geography planning; no fixed year, ownership or automatic castle proliferation',
          caveat='Castle catalogue includes later sites; province and district borders are planning references, not exact Sengoku front lines',
          sources=[dict(path=str(p.relative_to(ROOT)), sha256=hashlib.sha256(p.read_bytes()).hexdigest())
                   for p in sorted(CACHE.iterdir()) if p.suffix in ('.json', '.geojson', '.pmtiles')]))
    baseline_path = OUT/'compact_baseline.json'
    if not baseline_path.exists():
        baseline = json.loads((OUT/'verification.json').read_text(encoding='utf-8'))
        write(baseline_path, dict(grid=baseline['grid'],cells=baseline['cells'],buildings=baseline['buildings'],
              hreHashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((ROOT/'maps/hre').iterdir()) if p.is_file()}))
    print(json.dumps(dict(provinces=sum(f['properties']['layer']=='kuni_polygons' for f in features),
                          districts=sum(f['properties']['layer']=='gun_polygons' for f in features), castleAnchors=len(overrides))))


_layers = None


def layers():
    global _layers
    if _layers is None:
        raw = json.loads((CACHE/'boundaries.geojson').read_text(encoding='utf-8'))['features']
        _layers = {}
        for layer in ('kuni_polygons', 'gun_polygons'):
            features = [f for f in raw if f['properties']['layer'] == layer]
            geometries = [shape(f['geometry']) for f in features]
            _layers[layer] = features, geometries, STRtree(geometries)
    return _layers


def locate(lon, lat):
    point = Point(lon, lat)
    result = {}
    for name, field in (('kuni_polygons', 'province'), ('gun_polygons', 'district')):
        features, geometries, tree = layers()[name]
        exact = sorted(tree.query(point, predicate='intersects'))
        index = exact[0] if len(exact) else int(tree.nearest(point))
        # Coastal representative points can be just outside the archived polygon.
        if len(exact) or geometries[index].distance(point) < .025:
            result[field] = features[index]['properties'][field]
    return result


def grid_plan(view, data, sites):
    for site in sites:
        site['anchorProvince'] = site.get('province')
        lon, lat = map(float, view.lonlat(*site['world_pixel']))
        site.pop('province', None)
        site.pop('district', None)
        site.update(locate(lon, lat))
    provinces, geometries, _ = layers()['kuni_polygons']
    ids = np.zeros(len(data), dtype='<u2')
    dry = data[:, 0] != 1
    lon, lat = view.lonlat(view.x, view.y)
    definitions, borders = [], []
    frame = box(0, 0, view.world_width, view.world_height)
    for index, (feature, geometry) in enumerate(zip(provinces, geometries), 1):
        selected = contains_xy(geometry, lon, lat) & dry
        ids[selected] = index
        name = feature['properties']['province']
        definitions.append(dict(id=index, name=name, landCells=int(np.count_nonzero(selected)),
                                cities=[s['id'] for s in sites if s['kind']=='city' and s.get('province')==name]))
        projected = view.project(geometry).intersection(frame)
        boundary = projected.boundary
        paths = [list(line.coords) for line in (boundary.geoms if hasattr(boundary, 'geoms') else [boundary]) if line.geom_type=='LineString']
        if paths:
            borders.append(dict(name=name, paths=paths))
    districts, district_geometries, _ = layers()['gun_polygons']
    district_ids = np.zeros(len(data), dtype='<u2')
    district_definitions = []
    for index, (feature, geometry) in enumerate(zip(districts, district_geometries), 1):
        selected = contains_xy(geometry, lon, lat) & dry
        district_ids[selected] = index
        district_definitions.append(dict(id=index, **feature['properties'], landCells=int(np.count_nonzero(selected))))
    # Historical geography identifies fixed districts independently of scenario ownership.
    metadata = dict(source=URL, provinceStride=2, provinceCells=base64.b64encode(ids.tobytes()).decode(),
                    provinces=definitions, borders=borders, districtStride=2,
                    districtCells=base64.b64encode(district_ids.tobytes()).decode(), districts=district_definitions)
    write(OUT/'province_plan.json', dict(source=URL, provinces=definitions,
          coveredLandCells=int(np.count_nonzero(ids)), unassignedLandCells=int(np.count_nonzero(dry & (ids==0))),
          sites=[dict(id=s['id'], name=s['name'], province=s.get('province'), district=s.get('district'), castle=s.get('castleReference')) for s in sites],
          unrepresentedProvinces=[r['name'] for r in definitions if r['landCells'] and not r['cities']]))
    return metadata


if __name__ == '__main__':
    prepare()
