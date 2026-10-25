"""Compose regional lettering without rebuilding terrain, cities or wave sprites."""

import math
from bisect import bisect_left
import json
from pathlib import Path


def text_path(points):
    """Arc-distance sampling shared with battlefield.js (64 chords per quadratic)."""
    assert len(points) >= 3 and len(points) % 2 == 1
    samples = [(0, *points[0])]
    for i in range(0, len(points)-2, 2):
        a, b, c = points[i:i+3]
        for step in range(1, 65):
            t = step/64
            x, y = [(1-t)**2*a[k]+2*(1-t)*t*b[k]+t*t*c[k] for k in (0, 1)]
            d, px, py = samples[-1]
            samples.append((d+math.hypot(x-px, y-py), x, y))
    distances = [p[0] for p in samples]

    def at(distance):
        i = max(1, min(len(samples)-1, bisect_left(distances, distance)))
        d0, x0, y0 = samples[i-1]
        d1, x1, y1 = samples[i]
        t = max(0, min(1, (distance-d0)/(d1-d0))) if d1 > d0 else 0
        return x0+(x1-x0)*t, y0+(y1-y0)*t, math.atan2(y1-y0, x1-x0)
    return distances[-1], at


def glyphs(library, text, height, sea, length=0, regional=False):
    family = 's_s_c' if sea else 'l_b' if regional else 'l_s'
    items = {item['name']: item for item in library['items']}
    sprites = {sprite['name']: sprite for sprite in library['atlas']['sprites']}
    scale = height/max(float(sprites[f'{family}_{char}.png']['h']) for char in 'abcdefghijklmnopqrstuvwxyz')
    letters, cursor = [], 0
    lettering = text.title().replace(' Of ', ' of ') if sea and regional else text
    for char in lettering:
        if char == ' ':
            cursor += height*.48
            continue
        glyph_family = 's_s_l' if sea and regional and char.islower() else family
        item = items[f'{glyph_family}_{char.lower()}']
        glyph_scale = scale
        if sea and regional and char == 'o':
            # The original lowercase o has an a-like entry stroke; use a round O at x-height.
            item = items['s_s_c_o']
            glyph_scale *= float(sprites['s_s_l_o.png']['h'])/float(sprites['s_s_c_o.png']['h'])
        elif sea and regional and char == 'f':
            glyph_scale *= .5
        sprite = sprites[item['name']+'.png']
        letters.append((item, cursor, float(sprite['h'])*glyph_scale, glyph_scale))
        cursor += float(sprite['w'])*glyph_scale
    if len(letters) > 1 and length > cursor:
        spacing = (length-cursor)/(len(letters)-1)
        letters = [(item, x+i*spacing, h, scale) for i, (item, x, h, scale) in enumerate(letters)]
        cursor = length
    elif regional and length > 0 and cursor > length:
        fit = length/cursor
        letters = [(item, x*fit, h*fit, scale*fit) for item, x, h, scale in letters]
        cursor = length
    return letters, cursor


def regional_labels(library, labels, plan):
    layouts = plan.get('regionalLayout', {})
    if not layouts:
        return labels
    existing = {label['text'] for label in labels}
    for entry in plan['labels']:
        if entry['text'] in existing:
            continue
        style = layouts[entry['text']]
        labels.append(dict(index=str(max(int(label['index']) for label in labels)+1),
                           libName='eastasia_'+entry['text'].lower().replace(' ', '_'),
                           colorAlphaPercent='100', text=entry['text'], textZh=plan['chineseNames'][entry['text']],
                           tx=style['x'], ty=style['y'], size=style['size'], angle=style.get('angle', 0),
                           sea=entry.get('sea', False), lake=entry.get('lake'), displacement=0,
                           letteringLength=style['length'], opacity=plan['labelOpacity']))
    first_item = min(item['id'] for item in library['items'] if item['name'].startswith('eastasia_'))
    first_element = min(pair[0] for item in library['items'][first_item:]
                        for layer in item['layers'] for frame in layer for pair in frame['indices'])
    assert all(item['name'].startswith('eastasia_') for item in library['items'][first_item:])
    library['items'] = library['items'][:first_item]
    library['elements'] = library['elements'][:first_element]
    for label in labels:
        style = layouts.get(label['text'])
        if style:
            label.update(size=style['size'], letteringLength=style['length'],
                         tx=style.get('x', label['tx']), ty=style.get('y', label['ty']),
                         angle=style.get('angle', label['angle']), geographicStyle='regional')
        target_length = style['length'] if style else label.get('pathChordLength', label['letteringLength'])
        letters, width = glyphs(library, label['text'], label['size'], label['sea'], target_length, bool(style))
        label['pathChordLength'] = round(width, 3)
        label['letteringLength'] = round(width, 2)
        sine, cosine = math.sin(math.radians(label['angle'])), math.cos(math.radians(label['angle']))
        bend = plan.get('pathBends', {}).get(label['text'])
        if bend is not None:
            label.pop('textPath', None)
        if bend is not None:
            label['textPath'] = [[round(cosine*x-sine*y, 3), round(sine*x+cosine*y, 3)]
                                 for x, y in [(-width/2, -bend), (0, bend), (width/2, -bend)]]
        geographic = plan.get('geographicPaths', {}).get(label['text'])
        if geographic:
            import build_eastasia as geography
            source = json.loads((Path(__file__).resolve().parents[1]/'maps/eastasia_rewrite'/geographic['source']).read_text(encoding='utf-8'))
            coordinates = (next(f['geometry']['coordinates'] for f in source['features'] if f['properties']['name'] == geographic['feature'])
                           if 'feature' in geographic else source[geographic['key']])
            start, end = geographic.get('slice', [0, len(coordinates)])
            points = [[float(v)*2 for v in geography.PROJ.pixels(*p)] for p in coordinates[start:end]]
            if 'trim' in geographic:
                from shapely.geometry import LineString
                from shapely.ops import substring
                points = list(substring(LineString(points), *geographic['trim'], normalized=True).coords)
            if geographic.get('reverse'):
                points.reverse()
            label['tx'] = round((min(p[0] for p in points)+max(p[0] for p in points))/2, 3)
            label['ty'] = round((min(p[1] for p in points)+max(p[1] for p in points))/2, 3)
            # Midpoint quadratic spline follows the same crest used to generate the terrain.
            smooth = [points[0]]
            for i, control in enumerate(points):
                following = points[i+1] if i+1 < len(points) else control
                smooth.extend([control, [(control[k]+following[k])/2 for k in (0, 1)]])
            label['textPath'] = [[round(x-label['tx'], 3), round(y-label['ty'], 3)] for x, y in smooth]
        path = text_path(label['textPath']) if label.get('textPath') else None
        if geographic:
            letters, width = glyphs(library, label['text'], label['size'], label['sea'], path[0], bool(style))
            label['pathChordLength'] = round(width, 3)
        sprites = {s['name']: s for s in library['atlas']['sprites']}
        indices = []
        for item, cursor, height, scale in letters:
            px, py = item['pivot']
            glyph_width = float(sprites[item['name']+'.png']['w'])*scale
            if path:
                cx, cy, angle = path[1]((cursor+glyph_width/2)/width*path[0])
                sine, cosine = math.sin(angle), math.cos(angle)
                x, y = -glyph_width/2-px*scale, label['size']/2-height-py*scale
            else:
                cx, cy = 0, 0
                x, y = cursor-width/2-px*scale, label['size']/2-height-py*scale
            indices.append([len(library['elements']), item['id']])
            library['elements'].append(dict(transform=[cosine*scale, sine*scale, -sine*scale, cosine*scale,
                                                       cx+cosine*x-sine*y, cy+sine*x+cosine*y],
                                            alpha=1, start_frame=0, playback_mode=0))
        library['items'].append(dict(id=len(library['items']), name=label['libName'], kind=0,
                                     layers=[[dict(key=0, indices=indices)]], pivot=[0, 0], duration=1))
        if path:
            upper, lower = [], []
            for step in range(17):
                x, y, angle = path[1](path[0]*step/16)
                s, c = math.sin(angle), math.cos(angle)
                radius = label['size']*.52+5
                upper.append((label['tx']+x+s*radius, label['ty']+y-c*radius))
                lower.append((label['tx']+x-s*radius, label['ty']+y+c*radius))
            corners = [(round(x, 3), round(y, 3)) for x, y in upper+lower[::-1]]
            label['letteringLength'] = round(path[0], 3)
        else:
            corners = [(label['tx']+cosine*x-sine*y, label['ty']+sine*x+cosine*y)
                       for x, y in [(-width/2-5, -label['size']/2-5), (width/2+5, -label['size']/2-5),
                                    (width/2+5, label['size']/2+5), (-width/2-5, label['size']/2+5)]]
        left, top = min(x for x, y in corners), min(y for x, y in corners)
        label.update(left=round(left, 3), top=round(top, 3),
                     width=round(max(x for x, y in corners)-left, 3),
                     height=round(max(y for x, y in corners)-top, 3), polygon=corners+[corners[0]])
    return labels


def verify_layout(labels, width, height):
    from shapely.geometry import Polygon
    occupied = []
    for i, label in enumerate(labels):
        assert all(0 <= x <= width and 0 <= y <= height for x, y in label['polygon']), label['text']+' exceeds map'
        polygon = Polygon(label['polygon'])
        assert polygon.is_valid, label['text']+' invalid text ribbon'
        for other, bounds in occupied:
            assert not polygon.intersects(bounds), label['text']+' overlaps '+other['text']
        occupied.append((label, polygon))
