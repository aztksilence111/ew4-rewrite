"""Summarize source river endpoints and original baked coastline examples."""
import json
import math
from pathlib import Path

import build_eastasia_runtime as r
from PIL import Image
from shapely.geometry import Point
from shapely.strtree import STRtree

OUT = r.ROOT/'build/eastasia-water-20261025/mouths'


def lonlat(p):
    x = p.x/r.geo.WIDTH*(r.geo.PROJ.xmax-r.geo.PROJ.xmin)+r.geo.PROJ.xmin
    y = r.geo.PROJ.ymax-p.y/r.geo.HEIGHT*(r.geo.PROJ.ymax-r.geo.PROJ.ymin)
    proj = r.geo.PROJ
    rho = math.hypot(x, proj.rho0-y)
    return [round(math.degrees(proj.lon0+math.atan2(x, proj.rho0-y)/proj.n), 3),
            round(math.degrees(2*math.atan((proj.radius*proj.f/rho)**(1/proj.n))-math.pi/2), 3)]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    land, rivers, _ = r.geography()
    parts = [(p, a) for g, a in rivers for p in r.geo.geometries(g, 'LineString')]
    tree = STRtree([p for p, _ in parts])
    coast = r.to_source_geometry(r.unary_union([g for g, _ in r.geo.read_shapes('land')])).boundary
    output = []
    for index, (line, attrs) in enumerate(parts):
        for side in [0, -1]:
            p = Point(line.coords[side])
            near = sorted((p.distance(parts[j][0]), int(j)) for j in tree.query(p.buffer(40)) if j != index)
            if near and near[0][0] < .1:
                continue
            distance = p.distance(coast)*2
            output.append(dict(index=index, side=side, name=attrs.get('name_en') or attrs.get('name'),
                               xy=[round(p.x*2), round(p.y*2)], lonlat=lonlat(p), coast=round(distance, 1),
                               near=[dict(gap=round(d*2, 2), index=j, name=parts[j][1].get('name_en') or parts[j][1].get('name'))
                                     for d, j in near[:2]]))
    (OUT/'endpoints.json').write_text(json.dumps(output, indent=2)+'\n', encoding='utf-8')
    m = json.loads((r.BASE/'client/manifest.json').read_text(encoding='utf-8'))
    for name in ['europe', 'asia']:
        if name not in m['maps']:
            continue
        spec = m['maps'][name]
        image = Image.new('RGBA', (int(spec['width']), int(spec['height'])), (180, 180, 170, 255))
        for tile in spec['tiles']:
            with Image.open(r.BASE/'client'/tile['file']) as t:
                image.alpha_composite(t.convert('RGBA'), (int(tile['x']), int(tile['y'])))
        image.thumbnail((1400, 1100))
        image.convert('RGB').save(OUT/(name+'-original.jpg'))
    print(json.dumps([e for e in output if e['coast'] < 140 or e['near'] and e['near'][0]['gap'] < 35]))


if __name__ == '__main__':
    main()
