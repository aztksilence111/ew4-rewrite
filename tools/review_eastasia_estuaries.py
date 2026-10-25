"""Render local before/after river-mouth evidence without rebuilding game resources."""
import json

import update_eastasia_water as u
from PIL import Image, ImageDraw

OUT = u.ROOT/'build/eastasia-water-20261025/mouths'
REGIONS = {
    'qiantang': [6400, 4650, 6770, 5030],
    'hai': [5860, 3240, 6240, 3550],
    'min': [6290, 5380, 6660, 5680],
    'dong': [5680, 5830, 5900, 6040],
    'irrawaddy': [2450, 6750, 2880, 7280],
    'ganges': [1580, 5480, 2190, 6080],
}


def main():
    manifest = u.read(u.DATA/'client/manifest.json')
    for name, bounds in REGIONS.items():
        left, top, right, bottom = bounds
        pair = Image.new('RGB', ((right-left)*2, bottom-top+25), (170, 175, 168))
        for i, stage in enumerate(['before', 'after']):
            patch = Image.new('RGBA', (right-left, bottom-top), (170, 175, 168, 255))
            for tile in manifest['maps']['eastasia']['tiles']:
                x, y = int(tile['x']), int(tile['y'])
                if x >= right or y >= bottom or x+tile['width'] <= left or y+tile['height'] <= top:
                    continue
                old = OUT/'before'/tile['file']
                source = old if stage == 'before' and old.exists() else u.DATA/'client'/tile['file']
                with Image.open(source) as part:
                    patch.alpha_composite(part.convert('RGBA'), (x-left, y-top))
            pair.paste(patch.convert('RGB'), (i*(right-left), 25))
            ImageDraw.Draw(pair).text((i*(right-left)+8, 6), name+' '+stage, fill='black')
        pair.save(OUT/(name+'-comparison.png'))
    u.snapshot_map(manifest, 'europe', [2000, 1020, 2950, 1740], OUT/'original-rhine-mouth.png')
    print(json.dumps(dict(regions=len(REGIONS))))


if __name__ == '__main__':
    main()
