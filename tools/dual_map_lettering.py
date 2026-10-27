"""Place the two maps' regional type on geographic axes using East Asia glyphs."""

import math

from shapely.geometry import LineString, Polygon, box
from shapely.ops import substring
from shapely.ops import unary_union

from eastasia_lettering import glyphs, text_path


def reset_library(library):
    generated = lambda item: item['name'].startswith(('japan_geography_', 'hre_geography_'))
    first = next((i for i, item in enumerate(library['items']) if generated(item)), len(library['items']))
    tail = library['items'][first:]
    assert all(generated(item) for item in tail), 'Unrelated map lettering follows generated entries'
    if tail:
        elements = [p[0] for item in tail for layer in item['layers'] for frame in layer for p in frame['indices']]
        start = min(elements)
        assert all(p[0] < start for item in library['items'][:first]
                   for layer in item['layers'] for frame in layer for p in frame['indices'])
        library['items'] = library['items'][:first]
        library['elements'] = library['elements'][:start]


def longest_line(geometry):
    if geometry.geom_type == 'LineString':
        return geometry
    return max((line for line in geometry.geoms if line.geom_type == 'LineString'), key=lambda line: line.length)


def smooth_path(points):
    # Midpoint quadratics follow the same geographic axis as the terrain bands.
    result = [points[0]]
    for i, control in enumerate(points):
        following = points[min(i+1, len(points)-1)]
        result.extend([control, [(control[k]+following[k])/2 for k in (0, 1)]])
    return result


def compose(key, entries, library, view, config, layout, land, plain_names):
    relief = {r[0]: r[3] for r in config['relief']}
    relief.update({r[0]:r[2] for r in layout.get('regionalAxes',[])})
    frame = box(160, 160, view.world_width-160, view.world_height-160)
    result = []
    errors = []
    occupied = []
    sprites = {s['name']: s for s in library['atlas']['sprites']}
    for i, entry in enumerate(entries):
        zh, sea = entry['textZh'], entry['sea']
        ridge = zh in relief
        style = layout.get('seaLayout' if sea else 'ridgeLayout', {}).get(zh, {})
        coordinates = relief[zh] if ridge else layout['seaPaths' if sea else 'plainPaths'][zh]
        raw = LineString([view.xy(*p) for p in coordinates])
        axis = longest_line(raw.intersection(frame))
        if ridge:
            axis = longest_line(axis.intersection(land.buffer(15)))
            axis = substring(axis, *style.get('trim', [.09, .91]), normalized=True)
            maximum = style.get('maximumLength', 2000)
            if axis.length > maximum:
                axis = substring(axis, (axis.length-maximum)/2, (axis.length+maximum)/2)
        points = list(axis.coords)
        if points[-1][0] < points[0][0]:
            points.reverse()
        absolute = smooth_path(points)
        x = (min(p[0] for p in absolute)+max(p[0] for p in absolute))/2
        y = (min(p[1] for p in absolute)+max(p[1] for p in absolute))/2
        path = [[round(px-x, 3), round(py-y, 3)] for px, py in absolute]
        length, at = text_path(path)
        size = style.get('size', max(40, min(70, length*.07)) if ridge else
                         max(38, min(64, length*.1)) if not sea else max(64, min(104, length*.07)))
        name = f'{key}_geography_{i}'
        text = entry['text'] or plain_names[zh]
        letters, width = glyphs(library, text, size, sea, length, regional=True)
        indices = []
        for item, cursor, height, scale in letters:
            glyph_width = float(sprites[item['name']+'.png']['w'])*scale
            cx, cy, angle = at((cursor+glyph_width/2)/width*length)
            sine, cosine = math.sin(angle), math.cos(angle)
            px, py = item['pivot']
            gx, gy = -glyph_width/2-px*scale, size/2-height-py*scale
            indices.append([len(library['elements']), item['id']])
            library['elements'].append(dict(transform=[cosine*scale, sine*scale, -sine*scale, cosine*scale,
                cx+cosine*gx-sine*gy, cy+sine*gx+cosine*gy], alpha=1, start_frame=0, playback_mode=0))
        library['items'].append(dict(id=len(library['items']), name=name, kind=0,
            layers=[[dict(key=0, indices=indices)]], pivot=[0, 0], duration=1))
        upper, lower = [], []
        for step in range(65):
            px, py, angle = at(length*step/64)
            s, c, radius = math.sin(angle), math.cos(angle), size*.85+8
            upper.append((x+px+s*radius, y+py-c*radius))
            lower.append((x+px-s*radius, y+py+c*radius))
        polygon = upper+lower[::-1]
        left, top = min(p[0] for p in polygon), min(p[1] for p in polygon)
        label = dict(index=str(i), libName=name, tx=round(x, 3), ty=round(y, 3), left=round(left, 3), top=round(top, 3),
            width=round(max(p[0] for p in polygon)-left, 3), height=round(max(p[1] for p in polygon)-top, 3),
            text=text, textZh=zh, sea=sea, size=round(size, 3), angle=0, opacity=.72,
            letteringLength=round(length, 3), pathChordLength=round(width, 3), colorAlphaPercent='100',
            textPath=path, geographicStyle='regional', geographicAxis=coordinates,
            polygon=[[round(px, 3), round(py, 3)] for px, py in polygon+[polygon[0]]])
        ribbon = Polygon(polygon)
        footprints = [ribbon]
        positions = style.get('charactersZh')
        if positions:
            assert sea and len(positions) == len(zh), 'One marine location per Chinese character: '+zh
            character_size = style.get('characterSizeZh', size)
            projected = [view.xy(*point) for point in positions]
            label['characterSizeZh'] = character_size
            label['characterPositionsZh'] = []
            label['characterAxesZh'] = positions
            for j, (cx, cy) in enumerate(projected):
                previous, following = projected[max(0, j-1)], projected[min(len(projected)-1, j+1)]
                angle = math.degrees(math.atan2(following[1]-previous[1], following[0]-previous[0]))
                label['characterPositionsZh'].append(dict(x=round(cx-x, 3), y=round(cy-y, 3), angle=round(angle, 3)))
                radius = character_size*.85+8
                footprints.append(box(cx-radius, cy-radius, cx+radius, cy+radius))
            bounds = unary_union(footprints).bounds
            label.update(left=round(bounds[0], 3), top=round(bounds[1], 3),
                         width=round(bounds[2]-bounds[0], 3), height=round(bounds[3]-bounds[1], 3))
        region = unary_union(footprints)
        if not ribbon.is_valid:
            errors.append(text+' invalid lettering ribbon')
        if not box(8, 8, view.world_width-8, view.world_height-8).covers(region):
            errors.append(text+' text outside visible map: '+str(ribbon.bounds))
        if sea and land.intersects(region):
            hit = land.intersection(region).centroid
            errors.append(text+' lettering crosses land at '+str(view.lonlat(hit.x, hit.y)))
        for other, previous_region in zip(result, occupied):
            if region.intersects(previous_region):
                errors.append(text+' overlaps '+other['text'])
        result.append(label)
        occupied.append(region)
    if errors:
        raise ValueError('\n'.join(errors))
    return result
