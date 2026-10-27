"""Install reviewed map assets and stage one three-map inspection Mod."""

import hashlib
import argparse
import json
from pathlib import Path
import shutil
import struct
import xml.etree.ElementTree as ET

import build_japan_hre_maps as source
import dual_map_lettering
import dual_map_water
from PIL import Image, ImageDraw, ImageFilter
import numpy as np

ROOT = source.ROOT
DATA = ROOT / 'rewrite/data'
STAGE = ROOT / 'build/map-review-tests-20261027/source'
IDENTITIES = [('japan', 4, 8, '日本', '江户', '京都'), ('hre', 5, 9, '神罗', '维也纳', '科隆')]
PLAIN_NAMES = {'石狩平原':'ISHIKARI PLAIN', '仙台平原':'SENDAI PLAIN', '关东平原':'KANTO PLAIN',
    '浓尾平原':'NOBI PLAIN', '大阪平原':'OSAKA PLAIN', '筑紫平原':'TSUKUSHI PLAIN', '新潟平原':'NIIGATA PLAIN',
    '莱茵河谷':'RHINE VALLEY', '波河平原':'PO PLAIN', '维也纳盆地':'VIENNA BASIN',
    '匈牙利平原':'HUNGARIAN PLAIN', '北德平原':'NORTH GERMAN PLAIN'}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value, compact=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=None if compact else 2,
        separators=(',', ':') if compact else None)+'\n', encoding='utf-8', newline='\n')


def xml(path, root):
    ET.indent(root, space='  ')
    path.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(root).write(path, encoding='utf-8', xml_declaration=True)


def background(key, config, metadata, sites):
    view = source.MapView(key, config)
    assert (view.width, view.height) == (metadata['gridWidth'], metadata['gridHeight'])
    land, _, _, _ = source.geography(view)
    ratio = metadata['imageWidth'] / view.world_width
    mask = Image.new('L', (metadata['imageWidth'], metadata['imageHeight']))
    draw = ImageDraw.Draw(mask)
    for polygon in source.plan.parts(land, 'Polygon'):
        draw.polygon([(x*ratio, y*ratio) for x, y in polygon.exterior.coords], fill=255)
        for ring in polygon.interiors:
            draw.polygon([(x*ratio, y*ratio) for x, y in ring.coords], fill=0)
    for site in sites:
        x, y = [p*ratio for p in site['world_pixel']]
        if site['kind'] == 'port':
            if site.get('river_berth'):
                draw.ellipse((x-12*ratio, y-12*ratio, x+12*ratio, y+12*ratio), fill=0)
        elif mask.getpixel((int(x), int(y))) < 128:
            draw.ellipse((x-12*ratio, y-12*ratio, x+12*ratio, y+12*ratio), fill=255)
    image = Image.new('RGBA', mask.size, (68, 102, 136, 0))
    image.putalpha(Image.eval(mask, lambda p:255-p))
    edge = Image.fromarray((np.asarray(mask.filter(ImageFilter.MaxFilter(3)), dtype=np.int16)
                           - np.asarray(mask, dtype=np.int16)).astype(np.uint8))
    image.paste((110, 143, 153, 255), (0, 0, *image.size), edge)
    draw = ImageDraw.Draw(image)
    for river in metadata['rivers']:
        for line in river['paths']:
            draw.line([(x*ratio, y*ratio) for x, y in line], fill=(68, 102, 136, 255), width=2)
    folder = DATA / 'client/maps' / key
    folder.mkdir(parents=True, exist_ok=True)
    tiles = []
    for top in range(0, image.height, 1024):
        for left in range(0, image.width, 1024):
            tile = image.crop((left, top, min(left+1026, image.width), min(top+1026, image.height)))
            name = f'{left}_{top}.png'
            tile.save(folder/name, compress_level=6)
            tiles.append(dict(x=left/ratio, y=top/ratio, width=tile.width/ratio, height=tile.height/ratio,
                              file=f'maps/{key}/{name}'))
    with Image.open(ROOT/'maps'/key/'regional-terrain-preview.jpg') as preview:
        preview.thumbnail((568, 320))
        preview.save(folder/'selection.png')
    return tiles


def lettering(key, labels, library, config, layout):
    view = source.MapView(key, config)
    land, _, _, _ = source.geography(view)
    labels = list(labels)
    for zh,en,axis in layout.get('regionalAxes',[]):
        if not any(label['textZh']==zh for label in labels):
            labels.append(dict(textZh=zh,text=en,sea=False,category='mountain'))
    return dual_map_lettering.compose(key, labels, library, view, config, layout, land, PLAIN_NAMES)


def battle(key, map_id, metadata, sites, city_ids, first, second):
    raw = (DATA/'original/conquest1.btl').read_bytes()
    header = list(struct.unpack_from('<18i', raw))
    factions = [raw[72+i*180:72+(i+1)*180] for i in range(header[6])]
    factions = [bytearray(next(f for f in factions if f[24:40].split(b'\0')[0].decode() == name))
                for name in ('fra', 'gbr')]
    for i, faction in enumerate(factions):
        struct.pack_into('<6i', faction, 0, 9999, 9999, 9999, 1, i+1, 0)
        struct.pack_into('<f', faction, 56, 1)
    binary = (ROOT/'maps'/key/(key+'.bin')).read_bytes()
    width, height = metadata['gridWidth'], metadata['gridHeight']
    terrain = np.frombuffer(binary, dtype=np.uint8, offset=16).reshape((-1, 12))
    occupied = {s['cell'] for s in sites}
    owners = bytearray([255]) * (width*height)
    army_cells = []
    for owner, anchor in enumerate((first, second)):
        center = next(s['cell'] for s in sites if s['name'] == anchor)
        choices = source.neighbors(center, width, height)
        cell = next(c for c in choices if terrain[c, 0] != 1 and c not in occupied and c not in army_cells)
        owners[cell] = owner
        army_cells.append(cell)
    records = {}
    for site in sites:
        cell = site['cell']
        record = bytearray(16)
        struct.pack_into('<hh', record, 0, cell if cell <= 32767 else -1, city_ids.get(site['id'],-1))
        record[4], record[5] = {'city':0, 'industry':1, 'stable':2, 'port':3, 'farmland':4}[site['kind']], site['level']
        record[7] = site.get('building_style',1) if site['kind']=='port' else 2 if key == 'japan' else 1
        record[8] = record[9] = record[11] = 255
        records[cell] = record
    for crossing in metadata['crossings']:
        for cell, direction in ((crossing['cell'], crossing['direction']), (crossing['other'], (crossing['direction']+3)%6)):
            if cell not in records:
                record = bytearray(16)
                struct.pack_into('<hh', record, 0, cell if cell <= 32767 else -1, -1)
                record[4] = record[8] = record[9] = record[11] = 255
                records[cell] = record
            records[cell][13] |= 1 << direction
    army_offset = 72+header[6]*180+header[4]*header[5]+header[7]*16
    template = next(raw[army_offset+i*32:army_offset+(i+1)*32] for i in range(header[8]) if raw[army_offset+i*32+2] == 0)
    armies = []
    for cell in army_cells:
        army = bytearray(template)
        struct.pack_into('<h', army, 0, cell if cell <= 32767 else -1)
        struct.pack_into('<h', army, 16, -1)
        army[18], army[19], army[23], army[26], army[27] = 0, 0, 0, 255, 0
        armies.append(army)
    header[1:11] = [map_id, 0, 0, width, height, 2, len(records), 2, 0, 0]
    header[11:18] = [1, 999, 0, 0, 1, 0, 0]
    body = struct.pack('<18i', *header)+b''.join(factions)+owners+b''.join(records.values())+b''.join(armies)
    payload = json.dumps(dict(schema=1, baseSha256=hashlib.sha256(body).hexdigest(), mapWidth=width,
        mapHeight=height, sites=list(records), armies=army_cells), separators=(',', ':')).encode()
    return body+b'EW4XBTL\0'+struct.pack('<II4sIII', 1, 1, b'CELL', 1, 1, len(payload))+payload


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--lettering-only', action='store_true')
    parser.add_argument('--scenarios-only', action='store_true')
    args = parser.parse_args()
    STAGE.mkdir(parents=True, exist_ok=True)
    (STAGE/'original').mkdir(exist_ok=True)
    config = read(ROOT/'maps/dual_map_config.json')
    manifest = read(DATA/'client/manifest.json')
    library = read(DATA/'client/animations/maptext.json')
    dual_map_lettering.reset_library(library)
    layout = read(ROOT/'maps/dual_map_lettering.json')
    if args.lettering_only:
        for key, *_ in IDENTITIES:
            metadata = read(ROOT/'maps'/key/'map.json')
            manifest['maps'][key]['labels'] = lettering(key, metadata['labels'], library, config[key], layout[key])
        write(DATA/'client/manifest.json', manifest, True)
        write(DATA/'client/animations/maptext.json', library, True)
        refresh_index({'client/manifest.json', 'client/animations/maptext.json'})
        print('Geographic lettering updated; staged scenarios retained', flush=True)
        return
    maps = ET.parse(DATA/'original/def_map.xml').getroot()
    names = ET.parse(DATA/'original/def_areaname.xml').getroot()
    battles = ET.parse(DATA/'original/def_battlelist.xml').getroot()
    ids = {int(n.get('id')):n.get('name') for n in names}
    next_id = 12000
    pages = read(ROOT/'rewrite/Ew4.Web/wwwroot/conquest-pages.json')
    slots = [dict(scenario='conquest7', art='maps/eastasia/selection.png', labels=['东亚', '地图检阅'])]
    report = []
    for key, map_id, scenario_id, title, first, second in IDENTITIES:
        metadata = read(ROOT/'maps'/key/'map.json')
        sites = read(ROOT/'maps'/key/'buildings.json')
        farms = read(ROOT/'maps'/key/'farmland.json')['records']
        source_map = next((m for m in maps if m.get('id') == str(map_id) or m.get('name') == key), None)
        if source_map is None:
            source_map = ET.SubElement(maps, 'map', id=str(map_id), name=key, continent=key, file=key+'.bin')
        assert source_map.get('id') == str(map_id) and source_map.get('name') == key
        source_map.set('w', str(round(metadata['worldWidth'])))
        source_map.set('h', str(round(metadata['worldHeight'])))
        if not args.scenarios_only:
            shutil.copyfile(ROOT/'maps'/key/(key+'.bin'), DATA/'original'/(key+'.bin'))
        city_ids = {}
        for site in sites:
            while next_id in ids and ids[next_id] != site['name']:
                next_id += 1
            if next_id not in ids:
                ET.SubElement(names, 'areaname', id=str(next_id), name=site['name'])
                ids[next_id] = site['name']
            city_ids[site['id']] = next_id
            next_id += 1
        if not args.scenarios_only:
            labels = lettering(key, metadata['labels'], library, config[key], layout[key])
            water=dual_map_water.build(source.MapView(key,config[key]),metadata,sites,DATA/'client/maps'/key)
            dual_map_water.sync_properties(key)
            with Image.open(ROOT/'maps'/key/'regional-terrain-preview.jpg') as preview:
                preview.thumbnail((568,320))
                preview.save(DATA/'client/maps'/key/'selection.png')
            manifest['maps'][key] = dict(width=metadata['worldWidth'], height=metadata['worldHeight'], pixelRatio=1,
                **water, overlays=[dict(o, layer=0 if not o.get('seaForeground') else 1) for o in metadata['overlays']],
                labels=labels, cityNames={str(s['cell']):dict(name=s['name'], zh=s['name'], en=s['name']) for s in sites},
                terrainDetailZoom=.30, cameraBounds=dict(left=0, top=0, width=metadata['worldWidth'], height=metadata['worldHeight']))
        scenario = 'conquest'+str(scenario_id)
        manifest['strategic'][scenario] = dict(name='conquest '+str(scenario_id), preview=f'maps/{key}/selection.png')
        (STAGE/'original'/(scenario+'.btl')).write_bytes(battle(key, map_id, metadata, sites+farms, city_ids, first, second))
        ET.SubElement(battles, 'battle', name='conquest '+str(scenario_id), file=scenario+'.btl', map=str(map_id),
            hide='0', centerx=str(metadata['worldWidth']/2), centery=str(metadata['worldHeight']/2), scale='0.1', age='', agex='0', agey='0')
        slots.append(dict(scenario=scenario, art=f'maps/{key}/selection.png', labels=[title, '地图检阅']))
        report.append(dict(map=key, cells=metadata['totalCells'], buildings=len(sites)+len(farms),farms=len(farms), highCellBuildings=sum(s['cell']>32767 for s in sites+farms)))
        print(key+': runtime map staged', flush=True)
    # East Asia reuses the reviewed inspection battle, including its existing extensions.
    for name in ('conquest7.btl', 'conquest7.names.xml'):
        shutil.copyfile(ROOT/'rewrite/content/eastasia/original'/name, STAGE/'original'/name)
    incoming = ET.parse(ROOT/'rewrite/content/eastasia/original/def_battlelist.xml').getroot()
    battles.append(next(e for e in incoming if e.get('file') == 'conquest7.btl'))
    manifest['strategic']['conquest7'] = dict(name='conquest 7', preview='maps/eastasia/selection.png')
    xml(STAGE/'original/def_battlelist.xml', battles)
    if not args.scenarios_only:
        xml(DATA/'original/def_map.xml', maps)
        xml(DATA/'original/def_areaname.xml', names)
        write(DATA/'client/manifest.json', manifest, True)
        write(DATA/'client/animations/maptext.json', library, True)
    pages['pages'] = [p for p in pages['pages'] if p['id'] != 'map-review' and any(p['slots'])]
    pages['pages'].append(dict(id='map-review', slots=slots+[None]*3))
    write(STAGE/'frontend/conquest-pages.json', pages)
    text = (DATA/'original/stringtable_cn.ini').read_text(encoding='utf-8-sig')
    for n, title in ((7, '东亚'), (8, '日本'), (9, '神罗')):
        text += f'\ntext_conquest_age_{n}={title}地图检阅\ndesc_conquest {n}={title}地图与建筑检阅，非历史剧本。\n'
    (STAGE/'original/stringtable_cn.ini').write_text(text, encoding='utf-8')
    catalog = read(DATA/'legacy/catalog.json')
    catalog['source_sha256']['stringtable_cn.ini'] = source.digest(STAGE/'original/stringtable_cn.ini')
    write(STAGE/'legacy/catalog.json', catalog)
    # Refresh only changed entries; unchanged large runtime files retain their indexed hash.
    changed = {'original/def_map.xml', 'original/def_areaname.xml', 'client/manifest.json', 'client/animations/maptext.json'}
    changed.update('original/'+key+'.bin' for key, *_ in IDENTITIES)
    changed.update(p.relative_to(DATA).as_posix() for key, *_ in IDENTITIES for p in (DATA/'client/maps'/key).rglob('*.png'))
    if not args.scenarios_only:
        refresh_index(changed)
    write(ROOT/'maps/japan_hre_source_20261026/runtime-integration.json', dict(maps=report, scenarios=['conquest7', 'conquest8', 'conquest9'], errors=0))


def refresh_index(changed):
    index = read(DATA/'runtime-content.json')
    old = {f['path']:f for f in index['files']}
    for name in sorted(changed):
        p = DATA/name
        old[name] = dict(path=name, bytes=p.stat().st_size, sha256=source.digest(p))
    sections = {'original':0, 'legacy':1, 'client':2, 'rewrite':3}
    index['files'] = [old[k] for k in sorted(old, key=lambda p:(sections[p.split('/')[0]], p.casefold()))]
    write(DATA/'runtime-content.json', index)


if __name__ == '__main__':
    main()
