"""Update river artwork and river-edge bits without replacing settlements or other geography."""

import hashlib
import json
import shutil
import sys
from pathlib import Path

import build_eastasia_runtime as runtime
from PIL import Image
from shapely.geometry import LineString, Point
from shapely.ops import nearest_points, unary_union

ROOT, DATA, SOURCE = runtime.ROOT, runtime.BASE, runtime.OUT
REPORT = ROOT/'build/eastasia-water-20261025'
if '--mouths' in sys.argv:
    REPORT /= 'mouths'


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def write(path, value, compact=False):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=None if compact else 2,
                               separators=(',', ':') if compact else None)+'\n', encoding='utf-8')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def rebind():
    if '--art-only' in sys.argv:
        return
    config_path = DATA/'rewrite/geography.json'
    config = read(config_path)
    next(m for m in config['maps'] if m['map'] == 'eastasia')['baseTerrainSha256'] = digest(DATA/'original/eastasia.bin')
    write(config_path, config)
    index = read(DATA/'runtime-content.json')
    for entry in index['files']:
        if entry['path'] == 'rewrite/geography.json':
            entry.update(bytes=config_path.stat().st_size, sha256=digest(config_path))
    write(DATA/'runtime-content.json', index)
    generation = REPORT/'generation.json'
    if generation.exists():
        report = read(generation)
        report['artworkTilesChanged'] = sum(digest(p) != digest(DATA/'client'/p.relative_to(REPORT/'before'))
                                          for p in (REPORT/'before').rglob('*.png'))
        write(generation, report)


def snapshot_map(manifest, name, bounds, target):
    left, top, right, bottom = bounds
    image = Image.new('RGBA', (right-left, bottom-top))
    for tile in manifest['maps'][name]['tiles']:
        x, y = int(tile['x']), int(tile['y'])
        if x >= right or y >= bottom or x+tile['width'] <= left or y+tile['height'] <= top:
            continue
        with Image.open(DATA/'client'/tile['file']) as part:
            image.alpha_composite(part.convert('RGBA'), (x-left, y-top))
    image.save(target)


def main():
    REPORT.mkdir(parents=True, exist_ok=True)
    manifest = read(DATA/'client/manifest.json')
    current_binary = (DATA/'original/eastasia.bin').read_bytes()
    before_map = json.loads(json.dumps(manifest['maps']['eastasia']))
    protected = {p: digest(runtime.REPORT/p) for p in ['cities.json', 'facilities.json', 'bridges.json', 'impassable_plan.json']}
    baseline = REPORT/'eastasia-before.bin'
    if not baseline.exists():
        shutil.copyfile(DATA/'original/eastasia.bin', baseline)
    original = baseline.read_bytes()
    report = dict(protected=protected, terrainBefore=hashlib.sha256(original).hexdigest())
    if not (REPORT/'manifest-before.json').exists():
        shutil.copyfile(DATA/'client/manifest.json', REPORT/'manifest-before.json')
    x, y = [round(float(v)*2) for v in runtime.geo.PROJ.pixels(119.1, 33.2)]
    report['huaiBounds'] = [x-550, y-450, x+550, y+450]
    if not (REPORT/'huai-before.png').exists():
        snapshot_map(manifest, 'eastasia', report['huaiBounds'], REPORT/'huai-before.png')
    snapshot_map(manifest, 'europe', [2560, 1536, 3072, 2048], REPORT/'original-river-reference.png')
    land, rivers, _ = runtime.geography()
    # Join the generalized outlet to the real Yangtze geometry, avoiding a subpixel gap.
    yangtze = unary_union([g for g, a in rivers if 'yangtze' in str(a.get('name_en', a.get('name', ''))).lower()])
    outlet = next(g for g, a in rivers if a.get('name') == 'Huai Sanhe Gaoyou Yangtze outlet')
    endpoint = Point(outlet.coords[-1])
    nearest = nearest_points(endpoint, yangtze)[1]
    report['outletJoinGapPixels'] = endpoint.distance(nearest)*2
    assert report['outletJoinGapPixels'] < 40, 'Outlet not near Yangtze'
    for index, (g, a) in enumerate(rivers):
        if a.get('name') == 'Huai Sanhe Gaoyou Yangtze outlet':
            rivers[index] = (LineString(list(g.coords)+[nearest.coords[0]]), a)
    data = runtime.np.frombuffer(original[16:], dtype=runtime.np.uint8).reshape((runtime.GRID_H, runtime.GRID_W, 12)).copy()
    previous = data.copy()
    data[:, :, 8] = 0
    centres = {(col, row): runtime.world_to_source(*runtime.center(col, row))
               for row in range(runtime.GRID_H) for col in range(runtime.GRID_W)}
    runtime.add_rivers(data, centres, rivers)
    assert runtime.np.array_equal(previous[:, :, :8], data[:, :, :8])
    assert runtime.np.array_equal(previous[:, :, 9:], data[:, :, 9:])
    bridges = read(runtime.REPORT/'bridges.json')
    for bridge in bridges:
        row, col = divmod(bridge['cell'], runtime.GRID_W)
        assert int(data[row, col, 8]) & 1 << bridge['direction'], bridge['name']+' lost its river edge'
    changed = runtime.np.flatnonzero(previous[:, :, 8].ravel() != data[:, :, 8].ravel()).tolist()
    repairs = [(g, a) for g, a in rivers if a.get('repair') == 'source_seam']
    for cell in changed:
        cx, cy = runtime.center(cell % runtime.GRID_W, cell // runtime.GRID_W)
        if '--mouths' in sys.argv:
            fuchun = [g for g, a in rivers if a.get('name_en') == 'Fuchun']
            assert (any(Point(cx/2, cy/2).distance(g) < 40 for g, _ in repairs) or
                    any(Point(cx/2, cy/2).distance(g) < 140 for g in fuchun)), 'River edge changed away from repaired rivers'
        else:
            assert abs(cx-x) < 800 and abs(cy-y) < 500, 'Unexpected river change outside Huai basin'
    mask = Image.open(runtime.REPORT/'land_mask.png').convert('L')
    print('Rendering original-style rivers and open lake/sea mouths', flush=True)
    image, _ = runtime.background(land, rivers, read(runtime.REPORT/'cities.json'), bridges, mask)
    report['mouths'] = getattr(runtime, 'water_mouth_report', [])
    report['mouthFailures'] = getattr(runtime, 'water_mouth_failures', [])
    report['seams'] = [dict(name=a['name'], gap=a['gapWorldPixels']) for _, a in repairs]
    changed_files = set()
    runtime.eastasia_relief.position(runtime, manifest['maps']['eastasia']['overlays'])
    runtime.eastasia_relief.position(runtime, before_map['overlays'])
    if manifest != read(DATA/'client/manifest.json'):
        write(DATA/'client/manifest.json', manifest, True)
        changed_files.add('client/manifest.json')
    incoming = read(SOURCE/'client/manifest.json')
    previous_incoming = json.dumps(incoming)
    runtime.eastasia_relief.position(runtime, incoming['maps']['eastasia']['overlays'])
    if json.dumps(incoming) != previous_incoming:
        write(SOURCE/'client/manifest.json', incoming, True)
    for tile in manifest['maps']['eastasia']['tiles']:
        path = 'client/'+tile['file']
        destination = DATA/path
        left, top = int(tile['x']-runtime.IMAGE_LEFT), int(tile['y']-runtime.IMAGE_TOP)
        crop = image.crop((left, top, left+int(tile['width']), top+int(tile['height'])))
        staged = REPORT/destination.name
        crop.save(staged, compress_level=6)
        if digest(staged) != digest(destination):
            backup = REPORT/'before'/tile['file']
            backup.parent.mkdir(parents=True, exist_ok=True)
            if not backup.exists():
                shutil.copyfile(destination, backup)
            shutil.copyfile(staged, destination)
            shutil.copyfile(staged, SOURCE/path)
            changed_files.add(path)
        staged.unlink()
    binary = original[:16]+data.tobytes()
    art_only = '--art-only' in sys.argv
    if art_only:
        assert binary == current_binary, 'Artwork-only update would change terrain rules'
    else:
        for folder in [DATA, SOURCE]:
            (folder/'original/eastasia.bin').write_bytes(binary)
        changed_files.add('original/eastasia.bin')
    # Reusable modern template carries the same crossing rules as the legacy BIN.
    for path in ([] if art_only else [DATA/'original/eastasia.map.json', SOURCE/'eastasia.map.json']):
        document = read(path)
        assert len(document['cells']) == runtime.GRID_H*runtime.GRID_W
        for cell, entry in enumerate(document['cells']):
            entry['terrain']['riverEdges'] = int(data.reshape(-1, 12)[cell, 8])
        write(path, document, True)
    if not art_only:
        changed_files.add('original/eastasia.map.json')
    index = read(DATA/'runtime-content.json')
    for entry in index['files']:
        if entry['path'] in changed_files:
            path = DATA/entry['path']
            entry.update(bytes=path.stat().st_size, sha256=digest(path))
    write(DATA/'runtime-content.json', index)
    assert manifest['maps']['eastasia'] == before_map, 'Map labels/overlays/camera changed'
    assert all(digest(runtime.REPORT/p) == value for p, value in protected.items())
    snapshot_map(manifest, 'eastasia', report['huaiBounds'], REPORT/'huai-after.png')
    report.update(terrainAfter=hashlib.sha256(binary).hexdigest(), riverEdgeCellsChanged=changed,
                  riverEdgesBefore=sum(int(v).bit_count() for v in previous[:, :, 8].flat),
                  riverEdgesAfter=sum(int(v).bit_count() for v in data[:, :, 8].flat),
                  artworkTilesChanged=sum(digest(p) != digest(DATA/'client'/p.relative_to(REPORT/'before'))
                                          for p in (REPORT/'before').rglob('*.png')),
                  bridges=len(bridges), errors=0)
    write(REPORT/'generation.json', report)
    rebind()
    print(json.dumps({k: v for k, v in report.items() if k not in ('protected', 'mouths', 'seams')} |
                     dict(mouths=len(report['mouths']), seams=len(report['seams']))))


if __name__ == '__main__':
    main()
