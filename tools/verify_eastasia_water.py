"""Check river topology, retained gameplay data and dry-land-only river artwork."""
import json
import hashlib
from pathlib import Path

import build_eastasia_runtime as r
from PIL import Image
from shapely.geometry import Point

ROOT, DATA = r.ROOT, r.BASE
REPORT = ROOT/'build/eastasia-water-20261025'


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def main():
    checks = 0

    def check(condition, message):
        nonlocal checks
        assert condition, message
        checks += 1

    current = (DATA/'original/eastasia.bin').read_bytes()
    old = (REPORT/'eastasia-before.bin').read_bytes()
    check(len(old) == len(current), 'binary size retained')
    check(all(a == b for i, (a, b) in enumerate(zip(old, current)) if i < 16 or (i-16) % 12 != 8),
          'only river-edge bytes changed')
    cells = r.np.frombuffer(current[16:], dtype=r.np.uint8).reshape(-1, 12)
    for row in range(r.GRID_H):
        for col in range(r.GRID_W):
            for direction in range(6):
                if cells[row*r.GRID_W+col, 8] & (1 << direction):
                    x, y = r.neighbor(col, row, direction)
                    check(0 <= x < r.GRID_W and 0 <= y < r.GRID_H and
                          cells[y*r.GRID_W+x, 8] & (1 << ((direction+3) % 6)), 'reciprocal river edge')
    for bridge in read(r.REPORT/'bridges.json'):
        check(cells[bridge['cell'], 8] & (1 << bridge['direction']), bridge['name']+' crossing preserved')
    for path in [DATA/'original/eastasia.map.json', r.OUT/'eastasia.map.json']:
        check(all(c['terrain']['riverEdges'] == int(cells[i, 8]) for i, c in enumerate(read(path)['cells'])),
              'modern template river rules synchronized')
    check((r.OUT/'original/eastasia.bin').read_bytes() == current, 'source binary synchronized')
    configuration = next(m for m in read(DATA/'rewrite/geography.json')['maps'] if m['map'] == 'eastasia')
    check(configuration['baseTerrainSha256'] == hashlib.sha256(current).hexdigest(), 'geography binding updated')
    _, rivers, _ = r.geography()
    native = next(g for g, a in rivers if a.get('name') == 'Huai')
    lower = next(g for g, a in rivers if a.get('name') == 'Huai lower course')
    check(Point(lower.coords[0]).distance(native) < 1e-6, 'supplement starts on original river')
    check(lower.intersection(native).length < 1e-6 and lower.intersection(native).geom_type == 'Point',
          'supplement meets original exactly once without crossing or overlap')
    outlet = next(g for g, a in rivers if a.get('joinRiver') == 'Yangtze')
    check(Point(outlet.coords[0]).distance(Point(lower.coords[-1])) < 1e-6, 'Hongze outlet connected')
    check(any(Point(outlet.coords[-1]).distance(g) < 1e-6 for g, a in rivers
              if str(a.get('name_en') or a.get('name')).lower() == 'yangtze'), 'outlet joins Yangtze')
    manifest = read(DATA/'client/manifest.json')
    yanshan = next(l for l in manifest['maps']['eastasia']['labels'] if l['text'] == 'YANSHAN MOUNTAINS')
    points = yanshan['textPath']
    check(all(abs((x-points[0][0])*(points[-1][1]-points[0][1]) -
                  (y-points[0][1])*(points[-1][0]-points[0][0])) < .01 for x, y in points), 'Yanshan straight')
    mask = Image.open(r.REPORT/'land_mask.png').convert('L')
    tiles = {}
    samples = 0
    for geometry in [lower, outlet]:
        for part in r.geo.geometries(geometry, 'LineString'):
            for distance in range(0, int(part.length), 1):
                p = part.interpolate(distance)
                ix, iy = [round(v) for v in r.source_to_image(p.x, p.y)]
                if mask.getpixel((ix, iy)) or mask.crop((ix-3, iy-3, ix+4, iy+4)).getextrema()[1]:
                    continue
                wx, wy = ix+r.IMAGE_LEFT, iy+r.IMAGE_TOP
                tile = next(t for t in manifest['maps']['eastasia']['tiles'] if
                            t['x'] <= wx < t['x']+t['width'] and t['y'] <= wy < t['y']+t['height'])
                if tile['file'] not in tiles:
                    tiles[tile['file']] = Image.open(DATA/'client'/tile['file']).convert('RGBA')
                pixel = tiles[tile['file']].getpixel((int(wx-tile['x']), int(wy-tile['y'])))
                check(pixel == (*r.geo.SEA, 255), 'lake centreline hidden')
                samples += 1
    check(samples > 5, 'tested lake surface points')
    for entry in read(DATA/'runtime-content.json')['files']:
        raw = (DATA/entry['path']).read_bytes()
        check(len(raw) == entry['bytes'] and hashlib.sha256(raw).hexdigest() == entry['sha256'], entry['path'])
    for tile in manifest['maps']['eastasia']['tiles']:
        check((DATA/'client'/tile['file']).read_bytes() == (r.OUT/'client'/tile['file']).read_bytes(), 'source artwork synchronized')
    report = dict(checks=checks, errors=0, lakeSamples=samples,
                  changedCells=[i for i in range(len(cells)) if old[16+i*12+8] != current[16+i*12+8]])
    (REPORT/'structure.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
