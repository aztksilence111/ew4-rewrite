"""Verify mountain geography, preserved river rules and synchronized map resources."""

import hashlib
import json
import xml.etree.ElementTree as ET

import update_eastasia_mountains as u
from eastasia_lettering import regional_labels, verify_layout
from shapely import transform
from shapely.geometry import LineString, Point, shape

r = u.r


def main():
    checks = 0

    def check(value, message):
        nonlocal checks
        assert value, message
        checks += 1

    before = (u.REPORT/'before/original/eastasia.bin').read_bytes()
    current = (r.BASE/'original/eastasia.bin').read_bytes()
    raw = r.np.frombuffer(current[16:], dtype=r.np.uint8).reshape(-1, 12)
    old = r.np.frombuffer(before[16:], dtype=r.np.uint8).reshape(-1, 12)
    check(before[:16] == current[:16] and r.np.array_equal(old[:, 2:], raw[:, 2:]), 'river edges, coasts, foreground and offsets unchanged')
    check((r.OUT/'original/eastasia.bin').read_bytes() == current, 'source BIN matches runtime')
    for name in ('cities.json', 'facilities.json', 'mountain_landmark_cells.json'):
        for site in u.read(r.REPORT/name):
            check(r.np.array_equal(raw[site['cell']], old[site['cell']]), 'site retained: '+str(site['cell']))
    config = u.read(r.BASE/'rewrite/geography.json')
    old_config = u.read(u.REPORT/'before/rewrite/geography.json')
    config_map = next(m for m in config['maps'] if m['map'] == 'eastasia')
    check(config_map['terrainOverrides'] == old_config['maps'][0]['terrainOverrides'], 'snow barrier and passes unchanged')
    check(config_map['baseTerrainSha256'] == hashlib.sha256(current).hexdigest(), 'geography binding matches new map')
    manifest = u.read(r.BASE/'client/manifest.json')
    game_map = manifest['maps']['eastasia']
    labels = game_map['labels']
    verify_layout(labels, game_map['width'], game_map['height'])
    check(len(labels) == 87 and len({l['libName'] for l in labels}) == 87, '87 unique bilingual names')
    library = u.read(r.BASE/'client/animations/maptext.json')
    check(json.loads(json.dumps(regional_labels(library, json.loads(json.dumps(labels)), u.read(r.REPORT/'cartography_plan.json')))) == labels,
          'lettering regeneration is idempotent')
    source = u.read(r.OUT/'client/manifest.json')
    check(source['maps']['eastasia'] == game_map, 'source map metadata synchronized')
    check(u.read(r.OUT/'client/animations/maptext.json') == library, 'source glyph layout synchronized')
    old_map = u.read(u.REPORT/'before/client/manifest.json')['maps']['eastasia']
    for key in ('tiles', 'cityNames', 'cameraBounds'):
        check(old_map[key] == game_map[key], key+' retained')
    for overlay in [o for o in old_map['overlays'] if o['seaForeground']]:
        check(overlay in game_map['overlays'], 'sea foreground retained')
    features = u.read(r.ROOT/'maps/terrain_regions.geojson')['features']+u.read(r.TERRAIN_ADDITIONS)['features']
    counts = {}
    points = [Point(*r.world_to_source(*r.center(i % r.GRID_W, i // r.GRID_W))) for i in range(len(raw))]
    overlays = {o['cell']: o for o in game_map['overlays'] if o['layer'] == 0}
    for f in features:
        name = f['properties']['name']
        if name not in u.UPDATED_RANGES | {'Daba range', 'Dabie range', 'Nanling range'}:
            continue
        band = r.to_source_geometry(transform(shape(f['geometry']), r.geo.PROJ.xy, interleaved=False).buffer(f['properties']['buffer_km']*1000))
        mountain_cells = [i for i, p in enumerate(points) if raw[i, 0] == 9 and band.covers(p)]
        check(bool(mountain_cells), name+' has physical mountain cells')
        for i in mountain_cells:
            check('eastasia_' in overlays[i]['image'], name+' has relief sprites')
        counts[name] = len(mountain_cells)
    template = u.read(r.BASE/'original/eastasia.map.json')
    check(template == u.read(r.OUT/'eastasia.map.json'), 'modern template synchronized')
    changed = u.read(u.REPORT/'generation.json')['changedCells']
    kinds = list(ET.parse(r.BASE/'original/def_mapterrain.xml').getroot())
    costs = {e.get('type'): int(e.get('movementcost')) for e in ET.parse(r.BASE/'original/def_terraintype.xml').getroot()}
    for i in changed:
        kind = kinds[int(raw[i, 0])].get('type')
        check(template['cells'][i]['terrain']['terrainCost'] == costs[kind] and template['cells'][i]['defenseTerrain'] == kind,
              'movement and defense match terrain at '+str(i))
    # Geographic order is checked in the shared projection, not by screen north.
    _, rivers, _ = r.geography()
    irtysh = [g for g,a in rivers if str(a.get('name_en') or a.get('name')).lower() == 'irtysh']
    check(len(irtysh) >= 3, 'Irtysh source, Zaysan and downstream segments selected')
    upper = next(g for g in irtysh if g.distance(Point(*r.geo.PROJ.pixels(90.3253,47.6502))) < 1)
    check(upper.distance(Point(*r.geo.PROJ.pixels(87.17,47.58))) < 3, 'upper Irtysh follows south-west side of Chinese Altai')
    altai = next(f for f in features if f['properties']['name'] == 'Altai')['geometry']['coordinates']
    check(len(altai) > 3 and all(altai[i][1] >= altai[i+1][1] for i in range(len(altai)-1)), 'Altai descends southeast into Mongolian Altai')
    check(altai[2][1] > 48.5 and 47.3 < 47.58 < altai[2][1], 'Chinese Altai crest is north of upper Irtysh')
    for entry in u.read(r.BASE/'runtime-content.json')['files']:
        path = r.BASE/entry['path']
        check(path.stat().st_size == entry['bytes'] and u.digest(path) == entry['sha256'], 'content digest '+entry['path'])
    result = dict(checks=checks, errors=0, labels=len(labels), changedCells=len(changed), mountainCells=counts)
    r.write_json(u.REPORT/'structure.json', result)
    print(json.dumps(result))


if __name__ == '__main__':
    main()
