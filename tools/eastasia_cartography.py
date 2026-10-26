"""Native sea foregrounds and geographic labels for the East Asia map."""

from collections import Counter, deque
import json
import math
import random

import numpy as np
from shapely.affinity import rotate, translate
from shapely.geometry import Point, box
from shapely.ops import unary_union
from eastasia_lettering import glyphs


def world(runtime, lon, lat):
    return tuple(float(v)*runtime.SCALE for v in runtime.geo.PROJ.pixels(lon, lat))


def building_bounds(placements, facilities):
    from pathlib import Path
    translations = json.loads((Path(__file__).resolve().parents[1]/'maps/eastasia_rewrite/city_names.json').read_text(encoding='utf-8'))
    bounds = []
    for site in placements+facilities:
        x, y = site['world_pixel']
        if site.get('kind') == 'farmland':
            bounds.append(box(x-30, y-24, x+30, y+24))
        elif site.get('kind') == 'port':
            bounds.append(box(x-80, y-60, x+80, y+70))
        else:
            english = translations['byCityId'].get(str(site.get('city_id')), translations['names'].get(site.get('name'), ''))
            name_width = max(42, len(site.get('name', ''))*7, len(english)*4+5)
            bounds.append(box(x-name_width, y-32, x+name_width, y+55))
    return bounds


def labels(runtime, mask, placements, facilities):
    plan = json.loads((runtime.REPORT/'cartography_plan.json').read_text(encoding='utf-8'))
    library = json.loads((runtime.BASE/'client/animations/maptext.json').read_text(encoding='utf-8'))
    old_labels=[item for item in library['items'] if item['name'].startswith('eastasia_')]
    if old_labels:
        first_item=min(item['id'] for item in old_labels)
        first_element=min(pair[0] for item in old_labels for layer in item['layers'] for frame in layer for pair in frame['indices'])
        assert all(item['name'].startswith('eastasia_') for item in library['items'][first_item:])
        library['items']=library['items'][:first_item]
        library['elements']=library['elements'][:first_element]
    obstacles = unary_union(building_bounds(placements, facilities))
    occupied = []
    result = []
    lake_shapes = {}
    if any(e.get('lake') for e in plan['labels']):
        extent = box(runtime.geo.PROJ.xmin, runtime.geo.PROJ.ymin, runtime.geo.PROJ.xmax, runtime.geo.PROJ.ymax)
        for geometry, attrs in runtime.geo.read_shapes('lakes', extent):
            name = attrs.get('name_en') or attrs.get('name')
            lake_shapes[name] = runtime.to_source_geometry(geometry)
        from shapely.geometry import shape
        from shapely import transform as transform_geometry
        for feature in json.loads((runtime.REPORT/'lake_additions.geojson').read_text(encoding='utf-8'))['features']:
            geometry = transform_geometry(shape(feature['geometry']), runtime.geo.PROJ.xy, interleaved=False)
            lake_shapes[feature['properties']['name_en']] = runtime.to_source_geometry(geometry)
    offsets = sorted(((dx, dy) for dx in range(-160, 161, 32) for dy in range(-160, 161, 32)),
                     key=lambda p: (math.hypot(*p), abs(p[1]), p))
    # Place lake names first because they have much less room than regional labels.
    entries = sorted(enumerate(plan['labels']), key=lambda pair: not bool(pair[1].get('lake')))
    for index, entry in entries:
        text_zh = plan['chineseNames'][entry['text']]
        anchor_x, anchor_y = world(runtime, entry['lon'], entry['lat'])
        fixed = plan.get('regionalLayout', {}).get(entry['text'], {})
        if 'x' in fixed and 'y' in fixed:
            anchor_x, anchor_y = fixed['x'], fixed['y']
        angle = entry.get('angle', 0)
        sea = entry.get('sea', False)
        lake = lake_shapes.get(entry.get('lake'))
        if entry.get('lake') and lake is None:
            raise ValueError('Unknown lake geometry: '+entry['lake'])
        selected = None
        for reduction in (1, .9, .8):
            height = entry['size']*reduction
            letters, width = glyphs(library, entry['text'], height, sea, entry.get('length', 0)*reduction)
            local = rotate(box(-width/2-5, -height/2-5, width/2+5, height/2+5), angle, origin=(0, 0))
            for dx, dy in offsets:
                if math.hypot(dx, dy) > 180:
                    continue
                x, y = anchor_x+dx, anchor_y+dy
                footprint = translate(local, xoff=x, yoff=y)
                if not box(0, 0, runtime.IMAGE_W-64, runtime.IMAGE_H-90).contains(footprint):
                    continue
                if 'x' in fixed and 'y' in fixed:
                    selected = (x, y, height, letters, width, footprint, dx, dy)
                    break
                if footprint.intersects(obstacles) or any(footprint.intersects(p) for p in occupied):
                    continue
                # Check the whole word, so sea labels cannot run across a nearby island.
                samples = [footprint.exterior.interpolate(t, normalized=True) for t in np.linspace(0, 1, 24)]
                samples += [footprint.centroid]
                water = [mask.getpixel((round(p.x-runtime.IMAGE_LEFT), round(p.y-runtime.IMAGE_TOP))) < 128 for p in samples]
                if lake is not None:
                    # Dark lake lettering may sit on the adjacent bank of narrow lakes.
                    if lake.distance(Point(x/runtime.SCALE, y/runtime.SCALE))*runtime.SCALE > 100:
                        continue
                elif (sea and not all(water)) or (not sea and sum(water) > 2):
                    continue
                selected = (x, y, height, letters, width, footprint, dx, dy)
                break
            if selected:
                break
        if selected is None:
            raise ValueError('No unobstructed geographic label position: '+entry['text'])
        x, y, height, letters, width, footprint, dx, dy = selected
        occupied.append(footprint)
        indices = []
        sine, cosine = math.sin(math.radians(angle)), math.cos(math.radians(angle))
        for item, cursor, h, scale in letters:
            px, py = item['pivot']
            tx, ty = cursor-width/2-px*scale, height/2-h-py*scale
            element = dict(transform=[cosine*scale, sine*scale, -sine*scale, cosine*scale,
                                      cosine*tx-sine*ty, sine*tx+cosine*ty],
                           alpha=1, start_frame=0, playback_mode=0)
            indices.append([len(library['elements']), item['id']])
            library['elements'].append(element)
        name = 'eastasia_'+entry['text'].lower().replace(' ', '_')
        library['items'].append(dict(id=len(library['items']), name=name, kind=0,
                                     layers=[[dict(key=0, indices=indices)]], pivot=[0, 0], duration=1))
        left, top, right, bottom = footprint.bounds
        result.append(dict(index=str(index), libName=name, colorAlphaPercent='100',
                           tx=round(x, 3), ty=round(y, 3), left=round(left, 3), top=round(top, 3),
                           width=round(right-left, 3), height=round(bottom-top, 3), text=entry['text'],
                           textZh=text_zh,
                           sea=sea, size=round(height, 2), angle=angle,
                           opacity=entry.get('opacity', plan.get('labelOpacity', 60/255)),
                           lake=entry.get('lake'), letteringLength=round(width, 2),
                           displacement=round(math.hypot(dx, dy), 2), polygon=list(footprint.exterior.coords)))
    from eastasia_lettering import regional_labels
    result = regional_labels(library, result, plan)
    runtime.write_json(runtime.OUT/'client/animations/maptext.json', library)
    runtime.write_json(runtime.REPORT/'geographic_labels.json', result)
    return result


def ocean_cells(runtime, data):
    ocean = set()
    queue = deque()
    for row in range(runtime.GRID_H):
        for col in range(runtime.GRID_W):
            if (col in (0, runtime.GRID_W-1) or row in (0, runtime.GRID_H-1)) and data[row, col, 0] == 1:
                ocean.add((col, row))
                queue.append((col, row))
    while queue:
        col, row = queue.popleft()
        for d in range(6):
            nc, nr = runtime.neighbor(col, row, d)
            if 0 <= nc < runtime.GRID_W and 0 <= nr < runtime.GRID_H and data[nr, nc, 0] == 1 and (nc, nr) not in ocean:
                ocean.add((nc, nr))
                queue.append((nc, nr))
    return ocean


def waves(runtime, data, mask, placements, facilities, labels):
    plan = json.loads((runtime.REPORT/'cartography_plan.json').read_text(encoding='utf-8'))
    rng = random.Random(plan['waveSeed'])
    manifest = json.loads((runtime.BASE/'client/manifest.json').read_text(encoding='utf-8'))
    occupied = building_bounds(placements, facilities)
    occupied += [box(e['left'], e['top'], e['left']+e['width'], e['top']+e['height'])
                 for e in labels if e.get('geographicStyle') != 'regional']
    obstacles = unary_union(occupied)
    ocean = sorted(ocean_cells(runtime, data))
    target = round(len(ocean)*plan['waveDensity'])
    rng.shuffle(ocean)
    # Match the combined Europe/America distribution, including the rare broad wave.
    styles = [1, 2, 3, 4, 5, 6, 7]
    weights = [93, 61, 48, 88, 32, 38, 9]
    sea = data[:, :, 0] == 1
    data[sea, 4:8] = [11, 255, 0, 0]
    placed = []
    mask_array = np.asarray(mask)
    for col, row in ocean:
        style = rng.choices(styles, weights=weights)[0]
        dx, dy = rng.randint(-22, 22), rng.randint(-14, 14)
        x, y = runtime.center(col, row)
        x, y = x+dx, y+dy
        sprite = manifest['atlases']['tiles'][f'wave_{style}.png']
        left, top = x-sprite['refx'], y-sprite['refy']
        right, bottom = left+sprite['w'], top+sprite['h']
        if left < 0 or top < 0 or right >= runtime.IMAGE_W-64 or bottom >= runtime.IMAGE_H-90:
            continue
        rectangle = box(left-5, top-5, right+5, bottom+5)
        if rectangle.intersects(obstacles) or any(rectangle.intersects(p) for p in placed):
            continue
        l, t, r, b = (round(left-runtime.IMAGE_LEFT)-5, round(top-runtime.IMAGE_TOP)-5,
                      round(right-runtime.IMAGE_LEFT)+5, round(bottom-runtime.IMAGE_TOP)+5)
        if mask_array[t:b+1, l:r+1].max() > 0:
            continue
        data[row, col, 4:8] = [1, style-1, dx % 256, dy % 256]
        placed.append(rectangle)
        if len(placed) == target:
            break
    count = Counter(int(v)+1 for v in data[:, :, 5][data[:, :, 4] == 1])
    runtime.write_json(runtime.REPORT/'cartography.json', dict(
        seed=plan['waveSeed'], oceanCells=len(ocean), waves=sum(count.values()), target=target,
        waveStyles=dict(sorted(count.items())), labels=len(labels),
        landLabels=sum(not e['sea'] and not e.get('lake') for e in labels), seaLabels=sum(e['sea'] for e in labels),
        lakeLabels=sum(bool(e.get('lake')) for e in labels),
        nativeGlyphs=True, ownership='unset; natural geography only'))


def verify(runtime, data, manifest):
    from shapely.geometry import Polygon

    native = json.loads((runtime.BASE/'client/animations/maptext.json').read_text(encoding='utf-8'))
    extended = json.loads((runtime.OUT/'client/animations/maptext.json').read_text(encoding='utf-8'))
    assert extended['items'][:len(native['items'])] == native['items']
    assert extended['elements'][:len(native['elements'])] == native['elements']
    assert extended['atlas'] == native['atlas'], 'Original glyph atlas must be unchanged'
    labels = json.loads((runtime.REPORT/'geographic_labels.json').read_text(encoding='utf-8'))
    planned = json.loads((runtime.REPORT/'cartography_plan.json').read_text(encoding='utf-8'))
    assert len(labels) == len(planned['labels'])
    assert all(label['textZh'] == planned['chineseNames'][label['text']] for label in labels)
    obstacles = unary_union(building_bounds(
        json.loads((runtime.REPORT/'cities.json').read_text(encoding='utf-8')),
        json.loads((runtime.REPORT/'facilities.json').read_text(encoding='utf-8'))))
    occupied = []
    for label in labels:
        polygon = Polygon(label['polygon'])
        if label.get('geographicStyle') != 'regional':
            assert not polygon.intersects(obstacles), label['text']+' overlaps a building'
        assert not any(polygon.intersects(p) for p in occupied), label['text']+' overlaps another label'
        occupied.append(polygon)
        assert any(item['name'] == label['libName'] for item in extended['items'])
    ocean = ocean_cells(runtime, data)
    overlays = [o for o in manifest['maps']['eastasia']['overlays'] if o['seaForeground']]
    expected = {row*runtime.GRID_W+col for col, row in ocean if data[row, col, 4] == 1}
    assert {o['cell'] for o in overlays} == expected
    assert {o['image'] for o in overlays} == {f'wave_{i}.png' for i in range(1, 8)}
    mask = np.asarray(runtime.Image.open(runtime.REPORT/'land_mask.png'))
    wave_rectangles = []
    reserved = unary_union([obstacles]+[box(e['left'], e['top'], e['left']+e['width'], e['top']+e['height'])
                                      for e in labels if e.get('geographicStyle') != 'regional'])
    for overlay in overlays:
        row, col = divmod(overlay['cell'], runtime.GRID_W)
        record = data[row, col]
        dx, dy = (int(v) if v < 128 else int(v)-256 for v in record[6:8])
        x, y = runtime.center(col, row)
        assert (overlay['x'], overlay['y']) == (x+dx, y+dy)
        assert overlay['image'] == f'wave_{int(record[5])+1}.png'
        sprite = manifest['atlases']['tiles'][overlay['image']]
        left, top = overlay['x']-sprite['refx'], overlay['y']-sprite['refy']
        right, bottom = left+sprite['w'], top+sprite['h']
        l, t, r, b = (round(left-runtime.IMAGE_LEFT)-5, round(top-runtime.IMAGE_TOP)-5,
                      round(right-runtime.IMAGE_LEFT)+5, round(bottom-runtime.IMAGE_TOP)+5)
        assert mask[t:b+1, l:r+1].max() == 0, 'Wave crosses visible land'
        rectangle = box(left-5, top-5, right+5, bottom+5)
        assert not rectangle.intersects(reserved), 'Wave overlaps a building or geographic label'
        assert not any(rectangle.intersects(p) for p in wave_rectangles), 'Wave sprites overlap'
        wave_rectangles.append(rectangle)
    return dict(waves=len(overlays), labels=len(labels), preservedNativeGlyphs=True)
