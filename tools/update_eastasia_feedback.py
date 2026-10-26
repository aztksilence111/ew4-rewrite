"""Repair the reported lake/river topology and synchronize current map resources."""

import hashlib
import json
import shutil
import sys
from collections import Counter
from pathlib import Path

import build_eastasia_runtime as r
from shapely.geometry import Point, Polygon
import eastasia_provinces as provinces

REPORT = r.ROOT/'build/eastasia-feedback-20261026'


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def write(path, value, compact=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    newline = '\r\n' if path == r.BASE/'rewrite/geography.json' else '\n'
    path.write_text(json.dumps(value, ensure_ascii=False, indent=None if compact else 2,
                               separators=(',', ':') if compact else None)+'\n', encoding='utf-8', newline=newline)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def remember(relative):
    target = REPORT/'before'/relative
    if not target.exists():
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(r.BASE/relative, target)


def synchronize(changed_files):
    config_path = r.BASE/'rewrite/geography.json'
    config = read(config_path)
    next(m for m in config['maps'] if m['map'] == 'eastasia')['baseTerrainSha256'] = digest(r.BASE/'original/eastasia.bin')
    write(config_path, config)
    changed_files.add('rewrite/geography.json')
    barrier_path = r.REPORT/'impassable/himalaya-cells.json'
    barrier = read(barrier_path)
    barrier['baseTerrainSha256'] = digest(r.BASE/'original/eastasia.bin')
    write(barrier_path, barrier)
    index = read(r.BASE/'runtime-content.json')
    for entry in index['files']:
        if entry['path'] in changed_files:
            path = r.BASE/entry['path']
            entry.update(bytes=path.stat().st_size, sha256=digest(path))
    write(r.BASE/'runtime-content.json', index)


def water():
    REPORT.mkdir(parents=True, exist_ok=True)
    for path in ('original/eastasia.bin', 'original/eastasia.map.json', 'client/manifest.json', 'rewrite/geography.json'):
        remember(path)
    original = (r.BASE/'original/eastasia.bin').read_bytes()
    data = r.np.frombuffer(original[16:], dtype=r.np.uint8).reshape(r.GRID_H, r.GRID_W, 12).copy()
    old = data.copy()
    placements = read(r.REPORT/'cities.json')
    facilities = read(r.REPORT/'facilities.json')
    bridges = read(r.REPORT/'bridges.json')
    protected = {s['cell'] for s in placements+facilities}
    land, rivers, _ = r.geography()
    mask = r.make_land_mask(land, placements)
    previous_mask = r.Image.open(r.REPORT/'land_mask.png')
    changed_mask = r.np.asarray(mask) != r.np.asarray(previous_mask)
    ys, xs = r.np.nonzero(changed_mask)
    assert not len(xs) or (min(xs)>2000 and max(xs)<2400 and min(ys)>1400 and max(ys)<1650), 'Water surface changed outside Zaysan'
    fractions = r.np.load(r.REPORT/'land_fraction.npy')
    lake_cells = []
    for row in range(r.GRID_H):
        for col in range(r.GRID_W):
            x,y = r.center(col,row)
            if not (2000<x<2400 and 1350<y<1650):
                continue
            sx,sy = r.world_to_source(x,y)
            polygon = Polygon([(sx+dx/r.SCALE,sy+dy/r.SCALE) for dx,dy in ((0,-36),(32,-18),(32,18),(0,36),(-32,18),(-32,-18))])
            fractions[row,col] = polygon.intersection(land).area/polygon.area
            if fractions[row,col] <= .5 and data[row,col,0] != 1:
                cell = row*r.GRID_W+col
                assert cell not in protected, 'Lake would displace a site'
                data[row,col,:8] = [1,(col*13+row*7)%7,0,0,11,255,0,0]
                lake_cells.append(cell)
    data[:,:,8] = 0
    centres = {(col,row):r.world_to_source(*r.center(col,row)) for row in range(r.GRID_H) for col in range(r.GRID_W)}
    r.add_rivers(data,centres,rivers)
    for b in bridges:
        row, col = divmod(b['cell'], r.GRID_W)
        assert data[row,col,8] & 1<<b['direction'], b['name']+' crossing lost'
    image, _ = r.background(land,rivers,placements,bridges,mask)
    manifest = read(r.BASE/'client/manifest.json')
    game_map = manifest['maps']['eastasia']
    game_map['overlays'] = [o for o in game_map['overlays'] if o['cell'] not in set(lake_cells)]
    changed_files = {'client/manifest.json','original/eastasia.bin','original/eastasia.map.json'}
    tiles_changed = 0
    for tile in game_map['tiles']:
        path = 'client/'+tile['file']
        left,top = int(tile['x']-r.IMAGE_LEFT),int(tile['y']-r.IMAGE_TOP)
        target = REPORT/'tile.png'
        image.crop((left,top,left+tile['width'],top+tile['height'])).save(target,compress_level=6)
        if digest(target) != digest(r.BASE/path):
            remember(path)
            for folder in (r.BASE,r.OUT):
                shutil.copyfile(target,folder/path)
            changed_files.add(path)
            tiles_changed += 1
    (REPORT/'tile.png').unlink()
    binary = original[:16]+data.tobytes()
    for folder in (r.BASE,r.OUT):
        (folder/'original/eastasia.bin').write_bytes(binary)
        incoming = manifest if folder == r.BASE else read(folder/'client/manifest.json')
        incoming['maps']['eastasia'] = game_map
        write(folder/'client/manifest.json',incoming,True)
    template = read(r.BASE/'original/eastasia.map.json')
    flat = data.reshape(-1,12)
    for i,c in enumerate(template['cells']):
        c['terrain']['riverEdges'] = int(flat[i,8])
        if i in lake_cells:
            c['terrain'].update(sea=True,terrainCost=1)
            c.update(defenseTerrain='sea',fireTerrain='open')
    write(r.BASE/'original/eastasia.map.json',template,True)
    write(r.OUT/'eastasia.map.json',template,True)
    mask.save(r.REPORT/'land_mask.png')
    r.np.save(r.REPORT/'land_fraction.npy',fractions)
    r.eastasia_relief.classify(r,data)
    synchronize(changed_files)
    native = next(g for g,a in rivers if a.get('name_en')=='Nen')
    outlet = Point(native.coords[-1])
    assert any(g.distance(outlet)<.001 for g,a in rivers if a.get('name_en')=='Songhua'), 'Nen outlet disconnected'
    assert not any(a.get('name')=='Nen lower course' for g,a in rivers), 'Duplicate Nen supplement retained'
    result = dict(lakeCells=lake_cells,tilesChanged=tiles_changed,riverCellsChanged=r.np.flatnonzero(old[:,:,8].ravel()!=data[:,:,8].ravel()).tolist(),
                  directedRiverEdges=sum(int(v).bit_count() for v in data[:,:,8].flat),bridges=len(bridges),errors=0)
    write(REPORT/'water.json',result)
    print(json.dumps(result))


def settlements():
    for name in ('cities.json', 'facilities.json', 'settlement_plan.json'):
        target = REPORT/'before'/name
        if not target.exists():
            write(target, read(r.REPORT/name))
    original = (r.BASE/'original/eastasia.bin').read_bytes()
    data = r.np.frombuffer(original[16:], dtype=r.np.uint8).reshape(r.GRID_H,r.GRID_W,12).copy()
    cities = read(r.REPORT/'cities.json')
    old_city_cells = {c['cell'] for c in cities}
    land, _, _ = r.geography()
    cities = r.eastasia_additions.apply(r, data, cities, land)
    old_facilities = read(REPORT/'before/facilities.json')
    plan = read(r.REPORT/'settlement_plan.json')
    entries = provinces.provinces()
    by_name = {c.get('source_name', c['name']):c for c in cities}
    by_display = {c['name']:c for c in cities}
    by_display.update(by_name)
    factories = {'唐山':2,'包头':2,'青岛':2,'柳州':2,'蚌埠':1,'遵义':1,'衡阳':1,'宜宾':2}
    stables = '张北 大同 五原 朝阳 白城 德州 徐州 衢州 南平 吉安 阜阳 南阳 襄阳 芷江 高州 百色 儋县 镇远 大理 西昌 玉树 平凉 伊宁 那曲 台中 榆林 中卫'.split()
    plan['namedBuildingOverrides'] = {
        name:dict(kind='industry',level=level,reason='区域工业、矿冶或兵工节点；替代普通城市并回收重复匿名工厂')
        for name,level in factories.items()}
    plan['namedBuildingOverrides'].update({
        name:dict(kind='stable',level=2 if name in ('五原','伊宁') else 1,
                  reason='军马、地方骑兵及驮运补给节点；一级南方节点表示游戏补给能力，不表示大型历史牧场')
        for name in stables})
    if plan.get('provinceLayoutRevision') != 1:
        keep_stables = {'齐齐哈尔','海拉尔','赤峰','兰州','西宁','迪化','喀什'}
        remove_industry = {'唐山','包头','青岛','柳州','黑河','牡丹江','佳木斯','石家庄'}
        repairs = {'海口','兰州','西宁','银川','迪化','拉萨'}
        removed = []
        retained = []
        for f in old_facilities:
            anchor = by_display[f['anchor_city']]
            chinese = provinces.province_at(entries,anchor['lon'],anchor['lat']) is not None
            remove = f['kind']=='industry' and f['anchor_city'] in remove_industry
            remove |= f['kind']=='stable' and chinese and f['anchor_city'] not in keep_stables
            if remove:
                removed.append(f)
            else:
                retained.append(f)
        removed_pairs = {(f['kind'],f['anchor_city']) for f in removed}
        for group in plan['facilityGroups']:
            group['cities'] = [n for n in group['cities'] if (group['kind'],by_display[n]['name']) not in removed_pairs
                               and not (group['kind']=='farmland' and by_display[n]['name'] in repairs)]
        plan['facilityGroups'] = [g for g in plan['facilityGroups'] if g['cities']]
        plan['facilityGroups'].append(dict(kind='industry',level=1,cities=sorted(repairs),
                                          reason='由现有农业节点改作地方修械、加工及维修能力，保证省内基本工业招募；不代表重工业规模'))
        for f in retained:
            name = by_display[f['anchor_city']].get('source_name', f['anchor_city'])
            kind = 'industry' if f['kind']=='farmland' and f['anchor_city'] in repairs else f['kind']
            plan.setdefault('facilityPlacements',{}).setdefault(kind,{})[name] = [f['col'],f['row']]
        protected = {c['cell'] for c in cities} | {c['cell'] for c in read(r.REPORT/'mountain_landmark_cells.json')}
        protected |= {b[k] for b in read(r.REPORT/'bridges.json') for k in ('cell','other')}
        config = next(m for m in read(r.BASE/'rewrite/geography.json')['maps'] if m['map']=='eastasia')
        protected |= {o['row']*r.GRID_W+o['column'] for o in config['terrainOverrides']}
        used = protected | {f['cell'] for f in retained}
        cell_provinces = {i:provinces.cell_province(r,entries,i) for i in range(r.GRID_W*r.GRID_H) if data[i//r.GRID_W,i%r.GRID_W,0]!=1}
        def reserve(name, kind, code):
            city = by_display[name]
            candidates = [i for i,p in cell_provinces.items() if p==code and i not in used and data[i//r.GRID_W,i%r.GRID_W,0]==0
                          and r.eastasia_settlements.hex_distance(city['cell'],i,r.GRID_W)<=6]
            def score(i):
                clearance = min(r.eastasia_settlements.hex_distance(i,j,r.GRID_W) for j in used)
                distance = r.eastasia_settlements.hex_distance(city['cell'],i,r.GRID_W)
                return -min(clearance,2),abs(distance-3),distance,i
            assert candidates, f'No in-province land for {name} {kind}'
            cell = min(candidates,key=score)
            used.add(cell)
            plan['facilityPlacements'].setdefault(kind,{})[city.get('source_name',city['name'])] = [cell%r.GRID_W,cell//r.GRID_W]
            return [cell%r.GRID_W,cell//r.GRID_W]
        # These old facilities were outside their anchor's province, including three municipalities.
        for name,code in (('北京','110000'),('天津','120000'),('上海','310000'),('长春','220000'),('西宁','630000'),('银川','640000')):
            reserve(name,'industry',code)
        for name,code in (('北京','110000'),('天津','120000'),('上海','310000'),('重庆','500000')):
            position = reserve(name,'stable',code)
            plan['regionalSites'].append(dict(id=f'province-{code}-stable',kind='stable',level=1,city=by_display[name].get('source_name',name),position=position,
                                             reason='由回收的北方过密马厩配额补入市域基本骑兵与驮运补给'))
        plan['provinceLayoutRevision'] = 1
        write(r.REPORT/'settlement_plan.json',plan)
    translations = read(r.REPORT/'city_names.json')
    translations['names']['马格达加奇'] = 'Magdagachi'
    town = next(c for c in cities if c['name']=='马格达加奇')
    translations['byCityId'][str(town['city_id'])] = 'Magdagachi'
    write(r.REPORT/'city_names.json',translations)
    mask = r.Image.open(r.REPORT/'land_mask.png')
    facilities, summary = r.eastasia_settlements.build(r.REPORT/'settlement_plan.json',cities,data,mask)
    if not any(f['kind']=='industry' and provinces.cell_province(r,entries,f['cell'])=='320000' for f in facilities):
        used = {s['cell'] for s in cities+facilities+read(r.REPORT/'mountain_landmark_cells.json')}
        used |= {b[k] for b in read(r.REPORT/'bridges.json') for k in ('cell','other')}
        city = by_display['南京']
        candidates = [i for i in r.eastasia_settlements.land_distances(city['cell'],data,6) if i not in used
                      and data[i//r.GRID_W,i%r.GRID_W,0]==0 and provinces.cell_province(r,entries,i)=='320000']
        cell = min(candidates,key=lambda i:(-min(2,min(r.eastasia_settlements.hex_distance(i,j,r.GRID_W) for j in used)),
                                           abs(r.eastasia_settlements.hex_distance(i,city['cell'],r.GRID_W)-3),i))
        plan['facilityPlacements']['industry']['南京'] = [cell%r.GRID_W,cell//r.GRID_W]
        write(r.REPORT/'settlement_plan.json',plan)
        facilities, summary = r.eastasia_settlements.build(r.REPORT/'settlement_plan.json',cities,data,mask)
    assert len({s['cell'] for s in cities+facilities}) == len(cities)+len(facilities)
    old_cities = read(REPORT/'before/cities.json')
    assert all(next(n for n in cities if n['city_id']==c['city_id'])['cell']==c['cell'] for c in old_cities)
    coverage = []
    for code,name in provinces.coverage_units(entries).items():
        local = [(c.get('kind','city'),c['name'],c['cell']) for c in cities if provinces.cell_province(r,entries,c['cell'])==code]
        local += [(f['kind'],f['anchor_city'],f['cell']) for f in facilities if provinces.cell_province(r,entries,f['cell'])==code]
        assert all(any(s[0]==kind for s in local) for kind in ('industry','stable')), name+' missing recruitment'
        assert any(s[0]=='city' for s in local), name+' missing ordinary city'
        coverage.append(dict(code=code,name=name,industry=[s[2] for s in local if s[0]=='industry'],stable=[s[2] for s in local if s[0]=='stable'],cities=sum(s[0]=='city' for s in local)))
    template = read(r.BASE/'original/eastasia.map.json')
    old_sites = {c['cell'] for c in old_cities+old_facilities}
    for i in old_sites:
        template['cells'][i].update(name=None,buildingKind=None,buildingLevel=0)
    for c in cities:
        cell = template['cells'][c['cell']]
        cell.update(name=c['name'],buildingKind=c['kind'],buildingLevel=c['building_level'])
        if c['cell'] not in old_city_cells:
            cell.update(defenseTerrain='land',fireTerrain='open')
            cell['terrain'].update(sea=False,terrainCost=3)
    for f in facilities:
        template['cells'][f['cell']].update(name=None,buildingKind=f['kind'],buildingLevel=f['level'])
    manifest = read(r.BASE/'client/manifest.json')
    game_map = manifest['maps']['eastasia']
    game_map['cityNames'] = {str(c['cell']):dict(name=c['name'],zh=c['name'],en=translations['byCityId'].get(str(c['city_id']),translations['names'][c['name']])) for c in cities}
    added = {c['cell'] for c in cities}-old_city_cells
    assert not added & {f['cell'] for f in facilities}
    game_map['overlays'] = [o for o in game_map['overlays'] if o['cell'] not in added]
    changed_files = {'client/manifest.json','original/eastasia.bin','original/eastasia.map.json','original/def_areaname.xml'}
    for folder in (r.BASE,r.OUT):
        (folder/'original/eastasia.bin').write_bytes(original[:16]+data.tobytes())
        incoming = manifest if folder==r.BASE else read(folder/'client/manifest.json')
        incoming['maps']['eastasia'] = game_map
        write(folder/'client/manifest.json',incoming,True)
    write(r.BASE/'original/eastasia.map.json',template,True)
    write(r.OUT/'eastasia.map.json',template,True)
    for folder in (r.REPORT,r.OUT/'client/maps/eastasia'):
        write(folder/'cities.json',cities)
        write(folder/'facilities.json',facilities)
    summary['named_building_counts'] = dict(Counter(c['kind'] for c in cities))
    summary['total_building_counts'] = dict(Counter([c['kind'] for c in cities]+[f['kind'] for f in facilities]))
    write(r.REPORT/'settlements.json',summary)
    r.make_preview(data,cities,r.Image.open(r.OUT/'client/maps/eastasia/selection.png').copy(),facilities,read(r.REPORT/'bridges.json'))
    shutil.copyfile(r.OUT/'original/def_areaname.xml',r.BASE/'original/def_areaname.xml')
    synchronize(changed_files)
    result = dict(provinces=coverage,sourceUrl=provinces.SOURCE_URL,sourceSha256=digest(provinces.SOURCE),counts=summary['total_building_counts'],named=summary['named_building_counts'],
                  sitesBefore=len(old_cities)+len(old_facilities),sitesAfter=len(cities)+len(facilities),addedTown=town,
                  note='港澳没有陆地格心，以珠三角区域补给；本表按格心而非锚点城市归省。',errors=0)
    write(r.REPORT/'province_coverage.json',result)
    write(REPORT/'settlements.json',result)
    print(json.dumps({k:result[k] for k in ('counts','named','sitesBefore','sitesAfter','errors')},ensure_ascii=False))


if __name__ == '__main__':
    if '--settlements' in sys.argv:
        settlements()
    else:
        water()
