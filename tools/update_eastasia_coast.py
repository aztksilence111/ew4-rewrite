"""Repaint East Asia coastal artwork without changing its game map or named sites."""
import hashlib
import json
import shutil

import build_eastasia_runtime as r
from PIL import Image

OUT = r.ROOT/'build/eastasia-coast-fix-20261027'
REPORT = r.REPORT/'coast-correction-20261027.json'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    manifest = read(r.BASE/'client/manifest.json')
    protected_paths = ['original/eastasia.bin', 'original/eastasia.map.json', 'rewrite/geography.json',
                       'client/manifest.json', 'client/animations/maptext.json']
    protected = {p:digest(r.BASE/p) for p in protected_paths}
    planning = {p:digest(r.REPORT/p) for p in ['cities.json', 'facilities.json', 'bridges.json',
                                              'land_mask.png', 'geographic_labels.json', 'impassable_plan.json']}
    for tile in manifest['maps']['eastasia']['tiles']:
        backup = OUT/'before'/tile['file']
        backup.parent.mkdir(parents=True, exist_ok=True)
        if not backup.exists():
            shutil.copyfile(r.BASE/'client'/tile['file'], backup)
    land, rivers, _ = r.geography()
    mask = Image.open(r.REPORT/'land_mask.png').convert('L')
    print('Rendering 3x coastline, banks and estuary transitions', flush=True)
    image, _ = r.background(land, rivers, read(r.REPORT/'cities.json'), read(r.REPORT/'bridges.json'), mask)
    visual = r.water_visual_mask
    visual.save(OUT/'visual-mask.png')
    old, new = r.np.asarray(mask), r.np.asarray(visual)
    changed_mask = (old >= 128) != (new >= 128)
    permitted = r.np.asarray(mask.filter(r.ImageFilter.MaxFilter(5))) != r.np.asarray(mask.filter(r.ImageFilter.MinFilter(5)))
    assert not r.np.any(changed_mask & ~permitted), 'Visual land changed away from original coastal raster boundary'
    changed_files = set()
    index = read(r.BASE/'runtime-content.json')
    for tile in manifest['maps']['eastasia']['tiles']:
        name = 'client/'+tile['file']
        x, y = int(tile['x']-r.IMAGE_LEFT), int(tile['y']-r.IMAGE_TOP)
        patch = image.crop((x, y, x+int(tile['width']), y+int(tile['height'])))
        temporary = OUT/'tile.png'
        patch.save(temporary, compress_level=6)
        if digest(temporary) != digest(r.BASE/name):
            shutil.copyfile(temporary, r.BASE/name)
            destination = r.OUT/name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(temporary, destination)
            changed_files.add(name)
        temporary.unlink()
    for entry in index['files']:
        if entry['path'] in changed_files:
            p = r.BASE/entry['path']
            entry.update(bytes=p.stat().st_size, sha256=digest(p))
    r.write_json(r.BASE/'runtime-content.json', index)
    assert all(digest(r.BASE/p) == value for p,value in protected.items()), 'Gameplay or map metadata changed'
    assert all(digest(r.REPORT/p) == value for p,value in planning.items()), 'Planning source changed'
    report = dict(date='2026-10-27', status='artwork generated; verification pending',
                  antialiasSamples=3, coastBands=['dark-inner-bank-2px', 'light-outer-shore-4px'],
                  riverBlendWorldPixels=24, changedTiles=sorted(changed_files), protected=protected, planning=planning,
                  antialiasPixels=int(((new>0)&(new<255)).sum()), coastalThresholdChanges=int(changed_mask.sum()),
                  coastalThresholdChangesBeyond2px=int((changed_mask & ~permitted).sum()),
                  mouths=r.water_mouth_report, mouthFailures=r.water_mouth_failures)
    r.write_json(REPORT, report)
    print(json.dumps({k:report[k] for k in ['status','antialiasSamples','antialiasPixels',
                      'coastalThresholdChanges','coastalThresholdChangesBeyond2px','mouthFailures']}|
                     dict(changedTiles=len(changed_files),mouths=len(r.water_mouth_report))), flush=True)


if __name__ == '__main__':
    main()
