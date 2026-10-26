"""Generalized visual relief bands; these are not measured elevations."""

from collections import Counter
import json
from PIL import Image
from shapely import transform as transform_geometry
from shapely.geometry import Point, shape

HIGH_RANGES = {'Himalaya', 'Kunlun', 'Tianshan', 'Altai', 'Qilian', 'Hengduan',
               'Altyn Tagh', 'Gangdise', 'Nyainqentanglha', 'Tanggula', 'Bayan Har',
               'Minshan Qionglai ranges', 'Daxue range', 'Taiwan central range'}
LOW_RANGES = {'Dabie range', 'Nanling range', 'Wuyi', 'Arakan range',
              'Yinshan', 'Helan', 'Liupan', 'Luliang', 'Wushan', 'Wumeng', 'Dalou', 'Xuefeng range',
              'Tenasserim range', 'Sakhalin ranges', 'Taebaek range',
              'Yuecheng range', 'Dupang range', 'Dayao range', 'Jiuwan range',
              'Damingshan', 'Yunkai range', 'Yunwu range', 'Luofu Jiulian ranges',
              'Lianhua range', 'Fenghuang range'}


def landmarks(runtime, data, placements, facilities, mask):
    plan = json.loads((runtime.REPORT/'mountain_landmarks.json').read_text(encoding='utf-8'))
    occupied = {s['cell'] for s in placements+facilities}
    result = []
    for entry in plan['mountains']:
        x, y = runtime.eastasia_cartography.world(runtime, entry['lon'], entry['lat'])
        candidates = []
        for row in range(runtime.GRID_H):
            for col in range(runtime.GRID_W):
                cell = row*runtime.GRID_W+col
                cx, cy = runtime.center(col, row)
                distance = ((cx-x)**2+(cy-y)**2)**.5
                if distance > 90 or cell in occupied or data[row, col, 0] == 1:
                    continue
                if mask.getpixel((cx-runtime.IMAGE_LEFT, cy-runtime.IMAGE_TOP)) < 128:
                    continue
                candidates.append((distance, col, row))
        if not candidates:
            raise ValueError('No free mountain hex: '+entry['name'])
        distance, col, row = min(candidates)
        data[row, col, :2] = [9, (col*13+row*17)%6]
        occupied.add(row*runtime.GRID_W+col)
        result.append(dict(entry, cell=row*runtime.GRID_W+col, col=col, row=row,
                           world_pixel=list(runtime.center(col,row)), displacement_pixels=round(distance, 3)))
    runtime.write_json(runtime.REPORT/'mountain_landmark_cells.json', result)
    return result


def classify(runtime, data):
    features = json.loads((runtime.ROOT/'maps/terrain_regions.geojson').read_text(encoding='utf-8'))['features']
    features += json.loads(runtime.TERRAIN_ADDITIONS.read_text(encoding='utf-8'))['features']
    ranges = []
    for feature in features:
        props = feature['properties']
        if props['terrain'] != 9:
            continue
        line = transform_geometry(shape(feature['geometry']), runtime.geo.PROJ.xy, interleaved=False)
        ranges.append((runtime.to_source_geometry(line), props['name'], props['buffer_km']*1000/runtime.geo.METRES_PER_PIXEL))
    landmarks = {e['cell']:e for e in json.loads((runtime.REPORT/'mountain_landmark_cells.json').read_text(encoding='utf-8'))}
    result = []
    for row in range(runtime.GRID_H):
        for col in range(runtime.GRID_W):
            kind = int(data[row, col, 0])
            if kind not in (8, 9):
                continue
            x, y = runtime.center(col, row)
            level, range_name = 'hill', None
            if kind == 9:
                point = Point(x/runtime.SCALE, y/runtime.SCALE)
                distance, range_name = min((line.distance(point)/radius, name) for line, name, radius in ranges)
                if range_name in HIGH_RANGES:
                    level = 'high' if distance <= .58 else 'middle' if distance <= .86 else 'low'
                elif range_name in LOW_RANGES:
                    level = 'middle' if distance <= .32 else 'low'
                else:
                    level = 'middle' if distance <= .75 else 'low'
                if row*runtime.GRID_W+col in landmarks:
                    landmark = landmarks[row*runtime.GRID_W+col]
                    level, range_name = landmark['relief'], landmark['name']
            result.append(dict(cell=row*runtime.GRID_W+col, level=level, range=range_name,
                               spriteIndex=int(data[row, col, 1])))
    runtime.write_json(runtime.REPORT/'relief.json', dict(
        method='Generalized range core and flank bands, not surveyed elevations. Existing hill/mountain rules retained.',
        counts=dict(Counter(item['level'] for item in result)), cells=result))
    return result


def atlas(runtime, manifest):
    # Extend copies of the original atlases so original maps retain every sprite unchanged.
    scales = {'low': (.68, .48), 'middle': (.80, .74), 'high': (.92, 1.15)}
    for name in ('tiles', 'tiles_hd'):
        source = Image.open(runtime.BASE/f'client/images/{name}.png').convert('RGBA')
        if all(f'eastasia_{level}_{index+1}.png' in manifest['atlases'][name]
               for level in scales for index in range(6)):
            target = runtime.OUT/f'client/images/{name}.png'
            target.parent.mkdir(parents=True, exist_ok=True)
            source.save(target, compress_level=6)
            continue
        entries, y, row_height, x = {}, source.height+2, 0, 2
        pieces = []
        for level, (sx, sy) in scales.items():
            for index in range(6):
                original = manifest['atlases'][name][f'mountain_{index+1}.png']
                w, h = round(original['w']*sx), round(original['h']*sy)
                if x+w+2 > source.width:
                    y += row_height+2
                    x, row_height = 2, 0
                image = source.crop(tuple(round(original[k]) for k in ('x','y')) +
                                    (round(original['x']+original['w']), round(original['y']+original['h'])))
                pieces.append((image.resize((w,h), Image.Resampling.LANCZOS), (x,y)))
                entries[f'eastasia_{level}_{index+1}.png'] = dict(x=x, y=y, w=w, h=h,
                    refx=original['refx']*sx, refy=original['refy']*sy)
                row_height = max(row_height,h)
                x += w+2
        expanded = Image.new('RGBA',(source.width,y+row_height+2))
        expanded.paste(source,(0,0))
        for image, position in pieces:
            expanded.alpha_composite(image,position)
        target = runtime.OUT/f'client/images/{name}.png'
        target.parent.mkdir(parents=True,exist_ok=True)
        expanded.save(target,compress_level=6)
        manifest['atlases'][name].update(entries)


def position(runtime, overlays):
    plan = json.loads((runtime.REPORT/'cartography_plan.json').read_text(encoding='utf-8'))
    for anchor in plan.get('reliefAnchors', []):
        overlay = next(o for o in overlays if o['cell'] == anchor['cell'] and o['layer'] == 0)
        assert overlay.get('relief') in ('low', 'middle', 'high'), anchor['name']+' requires a mountain sprite'
        overlay.update(x=anchor['x'], y=anchor['y'])


def apply(runtime, manifest, overlays, data):
    levels = {item['cell']:item for item in classify(runtime,data)}
    atlas(runtime,manifest)
    for overlay in overlays:
        item = levels.get(overlay['cell'])
        if item is None:
            continue
        overlay['relief'] = item['level']
        if item['level'] != 'hill':
            overlay['image'] = f"eastasia_{item['level']}_{item['spriteIndex']+1}.png"
    position(runtime, overlays)
    return levels
