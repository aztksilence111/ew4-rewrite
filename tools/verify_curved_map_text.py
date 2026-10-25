"""Verify geographic lettering against its terrain sources and protected runtime data."""
import copy
import hashlib
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path.home()/'.codex/tools/ew4-inspect-deps'))
from shapely.geometry import LineString, Point
import build_eastasia as geography
from eastasia_lettering import regional_labels, text_path, verify_layout

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT/'rewrite/data'


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def main():
    manifest = read(DATA/'client/manifest.json')
    library = read(DATA/'client/animations/maptext.json')
    plan = read(ROOT/'maps/eastasia_rewrite/cartography_plan.json')
    labels = manifest['maps']['eastasia']['labels']
    checks = 0

    def check(condition, message):
        nonlocal checks
        assert condition, message
        checks += 1

    check(len(labels) == 72 and all(l.get('textPath') for l in labels), 'all bilingual labels have paths')
    verify_layout(labels, manifest['maps']['eastasia']['width'], manifest['maps']['eastasia']['height'])
    rebuilt = regional_labels(copy.deepcopy(library), copy.deepcopy(labels), plan)
    check(json.loads(json.dumps(rebuilt)) == labels, 'rebuild is deterministic')
    old = json.loads(subprocess.check_output(['git', 'show', 'HEAD:rewrite/data/client/animations/maptext.json'], cwd=ROOT))
    first_item = min(i['id'] for i in old['items'] if i['name'].startswith('eastasia_'))
    first_element = min(pair[0] for item in old['items'][first_item:] for layer in item['layers'] for frame in layer for pair in frame['indices'])
    check(old['atlas'] == library['atlas'], 'original glyph texture and sprite table retained')
    check(old['items'][:first_item] == library['items'][:first_item], 'original label items retained')
    check(old['elements'][:first_element] == library['elements'][:first_element], 'original label transforms retained')
    current = (DATA/'original/eastasia.bin').read_bytes()
    baseline = ROOT/'build/eastasia-water-20261025/eastasia-before.bin'
    protected = baseline.read_bytes() if baseline.exists() else current
    check(hashlib.sha256(protected).hexdigest() ==
          '6ebcf0455bccb60553d1aab624d641ec6e69c223dfd3fd5c5ccce2b6312c5883', 'known terrain baseline')
    check(len(current) == len(protected) and all(a == b for i, (a, b) in enumerate(zip(current, protected))
          if i < 16 or (i-16) % 12 != 8), 'terrain, cities, waves and bridges retained outside updated river edges')
    check(read(ROOT/'rewrite/content/eastasia/client/manifest.json')['maps']['eastasia']['labels'] == labels, 'source label configuration synchronized')
    check(read(ROOT/'rewrite/content/eastasia/client/animations/maptext.json') == library, 'source glyph layout synchronized')
    errors = []
    for entry in read(DATA/'runtime-content.json')['files']:
        raw = (DATA/entry['path']).read_bytes()
        if len(raw) != entry['bytes'] or hashlib.sha256(raw).hexdigest() != entry['sha256']:
            errors.append(entry['path'])
    check(not errors, 'runtime hash index: '+str(errors))
    ridge = read(ROOT/'maps/eastasia_rewrite/impassable_plan.json')['ridge']
    line = LineString([[float(v)*2 for v in geography.PROJ.pixels(*p)] for p in ridge])
    label = next(l for l in labels if l['text'] == 'HIMALAYAS')
    length, at = text_path(label['textPath'])
    positions = [at(label['size']/2+i*(length-label['size'])/5) for i in range(6)]
    distances = [line.distance(Point(label['tx']+x, label['ty']+y)) for x, y, _ in positions]
    check(max(distances) < 45, 'all six Chinese characters follow Himalayan crest: '+str(distances))
    check(max(distances[:2]) < 25, 'Xi/Ma do not drift into the southwest plain')
    angles = [at(length*t)[2]*180/3.141592653589793 for t in [.05, .25, .5, .75, .95]]
    check(max(angles)-min(angles) > 60, 'Himalayan path turns from northwest segment to northeast segment')
    result = dict(checks=checks, errors=0, labels=len(labels), terrainAligned=len(plan['geographicPaths']),
                  runtimeFiles=len(read(DATA/'runtime-content.json')['files']), himalayaCharacterCrestDistances=distances,
                  himalayaAngles=angles)
    folder = ROOT/'build/curved-map-text-20261025'
    folder.mkdir(exist_ok=True, parents=True)
    (folder/'structure.json').write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
