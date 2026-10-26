"""Apply the reference-map mountain corrections without relocating existing sites."""

import argparse
import hashlib
import json
from collections import Counter
import shutil
import xml.etree.ElementTree as ET

import build_eastasia_runtime as r
from eastasia_lettering import regional_labels, verify_layout
from shapely import transform
from shapely.geometry import LineString, Point, Polygon, shape
from shapely.ops import unary_union

REPORT = r.ROOT/'build/eastasia-mountains-20261026'
NEW_RANGES = {'Yinshan', 'Altyn Tagh', 'Helan', 'Liupan', 'Luliang', 'Wushan',
              'Wumeng', 'Dalou', 'Gangdise', 'Nyainqentanglha', 'Tanggula', 'Bayan Har'}
UPDATED_RANGES = NEW_RANGES | {'Altai', 'Wuyi', 'Xuefeng range'}
OLD_BANDS = [([[85,49],[90,48],[96,46]], 70),
             ([[117,29],[117,27],[116,25]], 45),
             ([[110.2,28],[110.6,27],[111,26.2]], 42)]


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_compact(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(',', ':'))+'\n', encoding='utf-8', newline='\n')


def labels():
    manifest = read(r.BASE/'client/manifest.json')
    library = read(r.BASE/'client/animations/maptext.json')
    plan = read(r.REPORT/'cartography_plan.json')
    result = regional_labels(library, manifest['maps']['eastasia']['labels'], plan)
    collisions = []
    for i, label in enumerate(result):
        for other in result[:i]:
            if Polygon(label['polygon']).intersects(Polygon(other['polygon'])):
                collisions.append([label['text'], other['text']])
    return manifest, library, result, collisions


def patch_terrain():
    original = (r.BASE/'original/eastasia.bin').read_bytes()
    cells = r.np.frombuffer(original[16:], dtype=r.np.uint8).reshape(-1, 12).copy()
    previous = cells.copy()
    features = read(r.ROOT/'maps/terrain_regions.geojson')['features'] + read(r.TERRAIN_ADDITIONS)['features']
    footprints = [transform(shape(f['geometry']), r.geo.PROJ.xy, interleaved=False).buffer(f['properties']['buffer_km']*1000)
                  for f in features if f['properties']['name'] in UPDATED_RANGES]
    footprints += [transform(LineString(points), r.geo.PROJ.xy, interleaved=False).buffer(radius*1000)
                   for points, radius in OLD_BANDS]
    affected = r.to_source_geometry(unary_union(footprints))
    protected = {s['cell'] for name in ('cities.json', 'facilities.json', 'mountain_landmark_cells.json')
                 for s in read(r.REPORT/name)}
    config = read(r.BASE/'rewrite/geography.json')
    geography = next(m for m in config['maps'] if m['map'] == 'eastasia')
    protected.update(e['row']*r.GRID_W+e['column'] for e in geography['terrainOverrides'])
    # Use the generator's priorities and density so incremental and full builds agree.
    _, _, regions = r.geography()
    for cell in range(len(cells)):
        col, row = cell % r.GRID_W, cell // r.GRID_W
        if cell in protected or cells[cell, 0] == 1 or not affected.covers(Point(*r.world_to_source(*r.center(col, row)))):
            continue
        cells[cell, :2] = r.terrain_at(col, row, regions)
    changed = r.np.flatnonzero(r.np.any(previous != cells, axis=1)).tolist()
    assert r.np.array_equal(previous[:, 2:], cells[:, 2:]), 'Only natural background kind and sprite may change'
    assert all(cell not in protected for cell in changed)
    assert all(cells[cell, 4] == 11 for cell in changed), 'Unexpected foreground on modified land'
    return original, cells, changed, protected, config


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check-plan', action='store_true')
    args = parser.parse_args()
    manifest, library, text, collisions = labels()
    if args.check_plan:
        print(json.dumps(dict(labels=len(text), collisions=collisions), ensure_ascii=False))
        return
    verify_layout(text, manifest['maps']['eastasia']['width'], manifest['maps']['eastasia']['height'])
    original, cells, changed, protected, config = patch_terrain()
    REPORT.mkdir(parents=True, exist_ok=True)
    for name in ('original/eastasia.bin', 'client/manifest.json', 'original/eastasia.map.json', 'rewrite/geography.json'):
        target = REPORT/'before'/name
        if not target.exists():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(r.BASE/name, target)
    sites = {name: digest(r.REPORT/name) for name in ('cities.json', 'facilities.json', 'bridges.json', 'impassable_plan.json')}
    relief = {item['cell']: item for item in r.eastasia_relief.classify(r, cells.reshape(r.GRID_H, r.GRID_W, 12))}
    terrain_defs = list(ET.parse(r.BASE/'original/def_mapterrain.xml').getroot())
    costs = {e.get('type'): int(e.get('movementcost')) for e in ET.parse(r.BASE/'original/def_terraintype.xml').getroot()}
    replacements = []
    for cell in changed:
        kind, index = map(int, cells[cell, :2])
        if kind == 0:
            continue
        entry = next(e for e in terrain_defs[kind] if int(e.get('idx')) == index)
        image = entry.get('image')
        level = relief.get(cell, {}).get('level')
        if level and level != 'hill':
            image = f'eastasia_{level}_{index+1}.png'
        replacements.append(dict(cell=cell, layer=0, seaForeground=False, image=image,
                                 x=r.center(cell % r.GRID_W, cell // r.GRID_W)[0],
                                 y=r.center(cell % r.GRID_W, cell // r.GRID_W)[1],
                                 **(dict(relief=level) if level else {})))
    changed_set = set(changed)
    current_map = manifest['maps']['eastasia']
    current_map['overlays'] = sorted([o for o in current_map['overlays'] if not (o['cell'] in changed_set and o['layer'] == 0)]
                                     + replacements, key=lambda o: (o['cell'], o['layer']))
    for overlay in current_map['overlays']:
        item = relief.get(overlay['cell']) if overlay['layer'] == 0 else None
        if item is not None:
            overlay['relief'] = item['level']
            if item['level'] != 'hill':
                overlay['image'] = f"eastasia_{item['level']}_{item['spriteIndex']+1}.png"
    current_map['labels'] = text
    binary = original[:16]+cells.tobytes()
    template = read(r.BASE/'original/eastasia.map.json')
    for cell in changed:
        kind = terrain_defs[int(cells[cell, 0])].get('type')
        template['cells'][cell]['terrain']['terrainCost'] = costs[kind]
        template['cells'][cell].update(defenseTerrain=kind, fireTerrain='open')
    for folder in (r.BASE, r.OUT):
        (folder/'original/eastasia.bin').write_bytes(binary)
        write_compact(folder/'client/animations/maptext.json', library)
        incoming = manifest if folder == r.BASE else read(folder/'client/manifest.json')
        incoming['maps']['eastasia'] = current_map
        write_compact(folder/'client/manifest.json', incoming)
    write_compact(r.BASE/'original/eastasia.map.json', template)
    write_compact(r.OUT/'eastasia.map.json', template)
    xml = ET.Element('MapTextPos')
    for label in text:
        ET.SubElement(xml, 'Element', **{k: str(label[k]) for k in
                      ('index', 'libName', 'colorAlphaPercent', 'tx', 'ty', 'left', 'top', 'width', 'height')})
    ET.indent(xml, space='  ')
    for folder in (r.BASE, r.OUT):
        (folder/'original/maptextpos_eastasia.xml').write_bytes(ET.tostring(xml, encoding='utf-8', xml_declaration=True))
    r.write_json(r.REPORT/'geographic_labels.json', text)
    geography = next(m for m in config['maps'] if m['map'] == 'eastasia')
    geography['baseTerrainSha256'] = hashlib.sha256(binary).hexdigest()
    (r.BASE/'rewrite/geography.json').write_text(json.dumps(config, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\r\n')
    barrier_path = r.REPORT/'impassable/himalaya-cells.json'
    barrier = read(barrier_path)
    barrier['baseTerrainSha256'] = geography['baseTerrainSha256']
    r.write_json(barrier_path, barrier)
    changed_files = {'client/manifest.json', 'client/animations/maptext.json', 'original/eastasia.bin',
                     'original/eastasia.map.json', 'original/maptextpos_eastasia.xml', 'rewrite/geography.json'}
    index = read(r.BASE/'runtime-content.json')
    for entry in index['files']:
        if entry['path'] in changed_files:
            target = r.BASE/entry['path']
            entry.update(bytes=target.stat().st_size, sha256=digest(target))
    (r.BASE/'runtime-content.json').write_text(json.dumps(index, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')
    assert all(digest(r.REPORT/name) == value for name, value in sites.items())
    baseline = r.np.frombuffer((REPORT/'before/original/eastasia.bin').read_bytes()[16:], dtype=r.np.uint8).reshape(-1, 12)
    all_changed = r.np.flatnonzero(r.np.any(baseline != cells, axis=1)).tolist()
    result = dict(labels=len(text), newLabels=15, changedCells=all_changed,
                  terrainAfter=geography['baseTerrainSha256'], protectedSites=sites,
                  protectedCells=len(protected), kinds=dict(Counter(int(cells[i,0]) for i in all_changed)), errors=0)
    r.write_json(REPORT/'generation.json', result)
    print(json.dumps({k: v for k, v in result.items() if k not in ('changedCells', 'protectedSites')} | dict(changedCells=len(all_changed))))


if __name__ == '__main__':
    main()
