"""Allocate scenario-neutral initial city levels and economic sites on the approved grid."""

from collections import Counter, deque
import hashlib
import json


KINDS = {"city": 0, "industry": 1, "stable": 2, "port": 3, "farmland": 4}


def neighbors(cell, width, height):
    row, col = divmod(cell, width)
    for nc, nr in ((col+1, row), (col+row%2, row+1), (col+row%2-1, row+1),
                   (col-1, row), (col+row%2-1, row-1), (col+row%2, row-1)):
        if 0 <= nc < width and 0 <= nr < height:
            yield nr*width+nc


def distances(start, width, height, maximum):
    result, queue = {start: 0}, deque([start])
    while queue:
        cell = queue.popleft()
        if result[cell] == maximum:
            continue
        for other in neighbors(cell, width, height):
            if other not in result:
                result[other] = result[cell]+1
                queue.append(other)
    return result


def ocean_cells(data):
    height, width, _ = data.shape
    sea = {i for i, kind in enumerate(data[:, :, 0].flat) if kind == 1}
    queue = deque(i for i in sea if i//width in (0, height-1) or i%width in (0, width-1))
    ocean = set(queue)
    while queue:
        for other in neighbors(queue.popleft(), width, height):
            if other in sea and other not in ocean:
                ocean.add(other)
                queue.append(other)
    return ocean


def land_distances(start, data, maximum):
    height, width, _ = data.shape
    result, queue = {start: 0}, deque([start])
    while queue:
        cell = queue.popleft()
        if result[cell] == maximum:
            continue
        for other in neighbors(cell, width, height):
            if other not in result and data[other//width, other%width, 0] != 1:
                result[other] = result[cell]+1
                queue.append(other)
    return result


def hex_distance(a, b, width):
    ar, ac = divmod(a, width)
    br, bc = divmod(b, width)
    dq, dr = ac-(ar-ar%2)//2-bc+(br-br%2)//2, ar-br
    return max(abs(dq), abs(dr), abs(dq+dr))


def build(plan_path, placements, data, visible_land_mask):
    plan_bytes = plan_path.read_bytes()
    plan = json.loads(plan_bytes)
    height, width, _ = data.shape
    by_name = {city.get('source_name', city['name']): city for city in placements}
    overrides = {}
    for level, names in plan['cityLevelOverrides'].items():
        for name in names:
            if name not in by_name or name in overrides:
                raise ValueError(f'Unknown or duplicate city level override: {name}')
            overrides[name] = int(level)
    for source_name,city in by_name.items():
        city['building_level'] = overrides.get(source_name, plan['cityLevelByTier'][str(city['tier'])])
        site = plan.get('namedBuildingOverrides', {}).get(source_name, {})
        city['kind'] = site.get('kind', 'city')
        city['building_kind'] = KINDS[city['kind']]
        if site:
            city['building_level'] = site['level']
            city['building_reason'] = site['reason']
        city['building_style'] = 2
        if source_name in plan.get('baseNames', {}):
            city['source_name'] = source_name
            city['name'] = plan['baseNames'][source_name]
    requests, requested = [], set()
    for group in plan['facilityGroups']:
        for name in group['cities']:
            identity = (group['kind'], name)
            if name not in by_name or identity in requested or group['kind'] not in KINDS:
                raise ValueError(f'Unknown or duplicate facility request: {identity}')
            requested.add(identity)
            requests.append({**group, 'city': name})
    for site in plan.get('regionalSites', []):
        if site['city'] not in by_name or site['kind'] not in KINDS or site['kind'] in ('port', 'city'):
            raise ValueError(f'Invalid regional site: {site}')
        requests.append({**site, 'placements': {site['city']: site['position']}})
    # Coastal positions have the fewest valid cells and are reserved first.
    requests.sort(key=lambda item: (item['kind'] != 'port', 'position' not in item, item['kind'], item['city']))
    city_cells = {city['cell'] for city in placements}
    used, facilities = set(city_cells), []
    distribution = plan.get('landPlacement', {})
    ocean = ocean_cells(data)
    for request in requests:
        city, kind = by_name[request['city']], request['kind']
        target = request.get('placements', {}).get(request['city']) or plan.get('facilityPlacements', {}).get(kind, {}).get(request['city'])
        radius = 3 if kind == 'port' else distribution.get('searchRadius', 6)
        if target:
            col, row = target
            if not (0 <= col < width and 0 <= row < height):
                raise ValueError(f'Facility target outside map: {target}')
            radius = max(radius, hex_distance(city['cell'], row*width+col, width))
        nearby = distances(city['cell'], width, height, radius)
        land = {} if kind == 'port' else land_distances(city['cell'], data, radius*2)
        valid = []
        for cell, distance in nearby.items():
            if cell in used or target and cell != target[1]*width+target[0]:
                continue
            row, col = divmod(cell, width)
            if kind == 'port':
                adjacent = list(neighbors(cell, width, height))
                if cell not in ocean or not any(data[n//width, n%width, 0] == 0 for n in adjacent):
                    continue
                px, py = col*64+(row%2)*32, row*54-18
                if visible_land_mask.getpixel((px+32, py+54)) >= 128:
                    continue
            elif data[row, col, 0] != 0 or cell not in land:
                continue
            x, y = col*64+(row%2)*32, row*54-18
            pixel_distance = ((x-city['world_pixel'][0])**2+(y-city['world_pixel'][1])**2)**.5
            # Seeded tie breaks avoid placing every district's sites in the same direction.
            tie = int.from_bytes(hashlib.sha256(f'{kind}:{city["id"]}:{cell}'.encode()).digest()[:4], 'little')/2**32
            if kind == 'port':
                score = (distance*1000+pixel_distance+tie,)
            else:
                clearance = min(hex_distance(cell, other, width) for other in used)
                city_clearance = min(hex_distance(cell, other, width) for other in city_cells)
                preferred = distribution.get('preferredDistance', 4)
                # Spread economic sites into gaps while preserving a land route to the reference city.
                score = (-min(clearance, 3), -min(city_clearance, 3), abs(distance-preferred), tie)
            valid.append((score, cell))
        if not valid:
            raise ValueError(f'No suitable {kind} cell within {radius} hexes: {city["name"]}')
        _, cell = min(valid)
        used.add(cell)
        row, col = divmod(cell, width)
        style, coast = 2, {}
        if kind == 'port':
            x,y = col*64+(row%2)*32,row*54-18
            shore = [n for n in neighbors(cell,width,height) if data[n//width,n%width,0] != 1]
            shore_cell = min(shore,key=lambda n: ((n%width)*64+(n//width%2)*32-city['world_pixel'][0])**2
                             +((n//width)*54-18-city['world_pixel'][1])**2)
            dx = sum((n%width)*64+(n//width%2)*32-x for n in shore)
            dy = sum((n//width)*54-18-y for n in shore)
            if dx == 0 and dy == 0:
                dx,dy = (shore_cell%width)*64+(shore_cell//width%2)*32-x,(shore_cell//width)*54-18-y
            # Original port styles face land north/east/west/south, respectively.
            style = (2 if dx >= 0 else 3) if abs(dx) > abs(dy) else (4 if dy >= 0 else 1)
            style = plan.get('portStyles', {}).get(request['city'], style)
            coast = dict(shore_cell=shore_cell,coast_vector=[dx,dy])
        facilities.append(dict(id=request.get('id',f'{city["id"]}-{kind}'), kind=kind, building_kind=KINDS[kind],
                               level=request['level'], building_style=style, anchor_city=city['name'], anchor_city_id=city['id'],
                               anchor_cell=city['cell'], cell=cell, col=col, row=row,
                               world_pixel=[col*64+(row%2)*32, row*54-18],
                               distance_hexes=nearby[cell], placement='coastal' if kind == 'port' else 'regional',
                               land_path_hexes=None if kind == 'port' else land[cell],
                               reason=request['reason'], owner=None,**coast))
    summary = dict(schema=1, plan_sha256=hashlib.sha256(plan_bytes).hexdigest(), cities=len(placements),
                   city_levels=dict(sorted(Counter(city['building_level'] for city in placements).items())),
                   facilities=len(facilities), facility_counts=dict(Counter(f['kind'] for f in facilities)),
                   facility_levels={kind: dict(sorted(Counter(f['level'] for f in facilities if f['kind'] == kind).items()))
                                    for kind in sorted({f['kind'] for f in facilities})},
                   maximum_distance_hexes=max(f['distance_hexes'] for f in facilities),
                   land_distance_hexes=dict(sorted(Counter(f['distance_hexes'] for f in facilities if f['kind']!='port').items())),
                   port_styles=dict(sorted(Counter(f['building_style'] for f in facilities if f['kind']=='port').items())),
                   ownership='unset; assigned by each conquest scenario', errors=[])
    return facilities, summary
