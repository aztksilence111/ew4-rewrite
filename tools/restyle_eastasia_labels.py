"""Apply East Asia lettering, wave density and artwork camera limits."""

import hashlib
import argparse
import json
import struct
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path.home()/'.codex/tools/ew4-inspect-deps'))

from eastasia_lettering import regional_labels, verify_layout

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT/'rewrite/data'
REPORT = ROOT/'maps/eastasia_rewrite'


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def write(path, value, compact=False):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=None if compact else 2,
                               separators=(',', ':') if compact else None)+'\n', encoding='utf-8', newline='\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--lettering-only', action='store_true', help='Preserve terrain, waves and camera; synchronize lettering source')
    args = parser.parse_args()
    plan = read(REPORT/'cartography_plan.json')
    manifest = read(DATA/'client/manifest.json')
    library = read(DATA/'client/animations/maptext.json')
    labels = regional_labels(library, manifest['maps']['eastasia']['labels'], plan)
    verify_layout(labels, manifest['maps']['eastasia']['width'], manifest['maps']['eastasia']['height'])
    if args.lettering_only:
        save_lettering(manifest, library, labels)
        return
    eastasia = manifest['maps']['eastasia']
    tiles = eastasia['tiles']
    left, top = min(t['x'] for t in tiles)+1, min(t['y'] for t in tiles)+1
    eastasia['cameraBounds'] = dict(left=left, top=top,
                                  width=max(t['x']+t['width'] for t in tiles)-1-left,
                                  height=max(t['y']+t['height'] for t in tiles)-1-top)
    waves = [o for o in eastasia['overlays'] if o.get('seaForeground')]
    ocean = read(REPORT/'cartography.json')['oceanCells']
    target = round(ocean*plan['waveDensity'])
    waves.sort(key=lambda o: hashlib.sha256(f"{plan['waveSeed']}:{o['cell']}".encode()).digest())
    keep = {o['cell'] for o in waves[:target]}
    removed = {o['cell'] for o in waves[target:]}
    eastasia['overlays'] = [o for o in eastasia['overlays'] if not o.get('seaForeground') or o['cell'] in keep]
    terrain = bytearray((DATA/'original/eastasia.bin').read_bytes())
    magic, version, width, height = struct.unpack_from('<4s3i', terrain)
    assert magic == b'YSAE' and version == 1 and len(terrain) == 16+width*height*12
    for cell in removed:
        assert terrain[16+cell*12] == 1 and terrain[16+cell*12+4] == 1
        terrain[16+cell*12+4:16+cell*12+8] = bytes([11, 255, 0, 0])
    (DATA/'original/eastasia.bin').write_bytes(terrain)
    save_lettering(manifest, library, labels, terrain_changed=True)
    print(json.dumps(dict(waves=len(keep), removed=len(removed))))


def save_lettering(manifest, library, labels, terrain_changed=False):
    write(DATA/'client/manifest.json', manifest, True)
    write(DATA/'client/animations/maptext.json', library, True)
    write(REPORT/'geographic_labels.json', labels)
    xml = ET.Element('Elements')
    for label in labels:
        ET.SubElement(xml, 'Element', **{k: str(label[k]) for k in
                      ('index', 'libName', 'colorAlphaPercent', 'tx', 'ty', 'left', 'top', 'width', 'height')})
    ET.indent(xml, space='  ')
    (DATA/'original/maptextpos_eastasia.xml').write_bytes(ET.tostring(xml, encoding='utf-8', xml_declaration=True))
    changed = {'client/manifest.json', 'client/animations/maptext.json', 'original/maptextpos_eastasia.xml'}
    if terrain_changed:
        changed.add('original/eastasia.bin')
    source = ROOT/'rewrite/content/eastasia'
    if source.exists():
        incoming = read(source/'client/manifest.json')
        incoming['maps']['eastasia']['labels'] = labels
        write(source/'client/manifest.json', incoming, True)
        write(source/'client/animations/maptext.json', library, True)
        (source/'original/maptextpos_eastasia.xml').write_bytes((DATA/'original/maptextpos_eastasia.xml').read_bytes())
    index = read(DATA/'runtime-content.json')
    for entry in index['files']:
        if entry['path'] in changed:
            raw = (DATA/entry['path']).read_bytes()
            entry.update(bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
    write(DATA/'runtime-content.json', index)
    print(json.dumps(dict(labels=len(labels), curved=sum(bool(l.get('textPath')) for l in labels), updated=len(changed))))


if __name__ == '__main__':
    main()
