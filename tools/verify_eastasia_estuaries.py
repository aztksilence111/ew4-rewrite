"""Check actual river-mouth water pixels, source seams and preserved map rules."""
import json
import math
import hashlib

import build_eastasia_runtime as r
from PIL import Image
from shapely.geometry import LineString, Point

OUT = r.ROOT/'build/eastasia-water-20261025/mouths'


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def main():
    checks = 0

    def check(value, message):
        nonlocal checks
        assert value, message
        checks += 1

    current = (r.BASE/'original/eastasia.bin').read_bytes()
    check(hashlib.sha256(current).hexdigest() == 'f70f36427ff30c33d1f0cb221f7789cad0339d62ee84adf336171bfdeaf8e275',
          'visual correction preserves current terrain and river rules')
    before = (OUT/'eastasia-before.bin').read_bytes()
    check(len(current) == len(before) and all(a == b for i, (a, b) in enumerate(zip(current, before))
          if i < 16 or (i-16) % 12 != 8), 'only river rules changed')
    manifest = read(r.BASE/'client/manifest.json')
    ridge = next(o for o in manifest['maps']['eastasia']['overlays'] if o['cell'] == 8014 and o['layer'] == 0)
    sprite = manifest['atlases']['tiles'][ridge['image']]
    with Image.open(r.BASE/'client/images/tiles.png') as atlas:
        for x,y in [(7301, 2703), (7290, 2712), (7295, 2707)]:
            sx,sy = round(sprite['x']+x-ridge['x']+sprite['refx']), round(sprite['y']+y-ridge['y']+sprite['refy'])
            check(atlas.getpixel((sx, sy))[3] >= 150, 'watershed ridge covers source tips and dividing gap')
    tiles = {}
    bridge_rects = []
    for bridge in read(r.REPORT/'bridges.json'):
        sprite = manifest['atlases']['buildings'][f"bridge_{bridge['style']}_{bridge['direction']+1}.png"]
        bx, by = bridge['world_pixel']
        left, top = round(bx-sprite['refx']-r.IMAGE_LEFT), round(by-sprite['refy']-r.IMAGE_TOP)
        bridge_rects.append((left, top, left+sprite['w'], top+sprite['h']))

    def pixel(x, y):
        wx, wy = x+r.IMAGE_LEFT, y+r.IMAGE_TOP
        tile = next(t for t in manifest['maps']['eastasia']['tiles'] if
                    t['x'] <= wx < t['x']+t['width'] and t['y'] <= wy < t['y']+t['height'])
        if tile['file'] not in tiles:
            tiles[tile['file']] = Image.open(r.BASE/'client'/tile['file']).convert('RGBA')
        return tiles[tile['file']].getpixel((round(wx-tile['x']), round(wy-tile['y'])))

    report = read(OUT/'generation.json')
    check(not report['mouthFailures'], 'every coastal endpoint reaches open water')
    names = {m['name'] for m in report['mouths']}
    check({'Hai', 'Fuchun', 'Min', 'Irrawaddy', 'Ganges'} <= names, 'all reported river mouths repaired')
    mask = Image.open(r.REPORT/'land_mask.png').convert('L')
    samples, covered, open_sea = 0, 0, 0
    for mouth in report['mouths']:
        path = LineString(mouth['path'])
        start = path.project(Point(mouth['endpoint']))
        for distance in range(math.ceil(start), math.floor(path.length)):
            p = path.interpolate(distance)
            x, y = round(p.x), round(p.y)
            if any(left <= x < right and top <= y < bottom for left, top, right, bottom in bridge_rects):
                covered += 1
                continue
            area = [pixel(x+dx, y+dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)]
            check(any(a >= 245 and 55 <= red <= 105 and 90 <= green <= 148 and 125 <= blue <= 185
                      for red, green, blue, a in area), mouth['name']+' uninterrupted river or coastal water '+str((x,y)))
            # Beyond the shore/antialias guard there must be only sea, with no appended banks.
            if mask.crop((x-7, y-7, x+8, y+8)).getextrema()[1] == 0:
                check(all(color[3] == 255 and all(abs(a-b) <= 1 for a,b in zip(color[:3], r.geo.SEA)) for color in area),
                      mouth['name']+' no river tail in open sea '+str((x, y, area)))
                open_sea += 1
            samples += 1
        p = path.interpolate(path.length)
        x, y = round(p.x), round(p.y)
        check(mask.crop((x-14, y-14, x+15, y+15)).getextrema()[1] == 0, mouth['name']+' reaches open water')
    _, rivers, _ = r.geography()
    check(open_sea > 100, 'sampled open-water termination across estuaries')
    tumen = next(g for g, a in rivers if a.get('name_en') == 'Tumen')
    yalu = [g for g, a in rivers if a.get('name_en') == 'Yalu']
    check(min(tumen.distance(g) for g in yalu)*2 > 10, 'Tumen and Yalu remain separate watersheds')
    check(not any(a.get('name') == 'Qiantang' for _, a in rivers), 'old diagonal Qiantang supplement removed')
    check(any(a.get('name_en') == 'Fuchun' for _, a in rivers), 'original Fuchun course used')
    repairs = [(g, a) for g, a in rivers if a.get('repair') == 'source_seam']
    for line, attrs in repairs:
        targets = [g for g, a in rivers if a.get('repair') != 'source_seam' and
                   str(a.get('name_en') or a.get('name')).lower() == attrs['name'].lower()]
        check(all(min(Point(line.coords[side]).distance(g) for g in targets) < 1e-6 for side in (0, -1)),
              attrs['name']+' source seam joins the same river')
        check(line.length*2 <= 12.001, 'only small source seams are joined automatically')
    result = dict(checks=checks, errors=0, mouths=len(report['mouths']), seams=len(repairs),
                  waterCoreSamples=samples, openSeaSamples=open_sea, bridgeCoveredSamples=covered,
                  changedCells=report['riverEdgeCellsChanged'])
    (OUT/'structure.json').write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
