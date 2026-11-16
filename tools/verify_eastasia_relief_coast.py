"""Check that relief coast clipping preserves map content and original atlas pixels."""
import argparse
import copy
import io
import json
from pathlib import Path
import zipfile
from PIL import Image, ImageChops
from eastasia_relief_coast import apply, ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input-package', type=Path, required=True)
    parser.add_argument('--content-root', type=Path, required=True)
    parser.add_argument('--patch', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    checks = 0

    def check(value):
        nonlocal checks
        assert value
        checks += 1

    with zipfile.ZipFile(args.input_package) as original:
        before = json.loads(original.read('client/manifest.json'))
        after = json.loads((args.patch / 'client/manifest.json').read_bytes())
        normalized = copy.deepcopy(after)
        changed = []
        for index, overlay in enumerate(normalized['maps']['eastasia']['overlays']):
            if 'coastSourceImage' in overlay:
                changed.append(index)
                overlay['image'] = overlay.pop('coastSourceImage')
        check(len(changed) == 133)
        for atlas_name in ['tiles', 'tiles_hd']:
            baseline = before['atlases'][atlas_name]
            atlas = normalized['atlases'][atlas_name]
            for name, entry in baseline.items():
                check(atlas[name] == entry)
            extra = set(atlas) - set(baseline)
            check(extra == {'eastasia_coast_relief_' + str(i) + '.png' for i in changed})
            for name in extra:
                del atlas[name]
            file = 'client/images/' + atlas_name + '.png'
            old_bytes = original.read(file) if file in original.namelist() else (args.content_root / file).read_bytes()
            old = Image.open(io.BytesIO(old_bytes)).convert('RGBA')
            new = Image.open(args.patch / file).convert('RGBA')
            check(new.width == old.width and new.height >= old.height)
            check(not ImageChops.difference(old, new.crop((0, 0, old.width, old.height))).getbbox(alpha_only=False))
        # Every other map, original atlas entry, label and overlay coordinate must be identical.
        check(normalized == before)

    generated = {}
    report = apply(copy.deepcopy(after), lambda name: (args.patch / name).read_bytes(),
                   lambda name, data: generated.__setitem__(name, data),
                   Image.open(ROOT / 'maps/eastasia_rewrite/land_mask.png'))
    for name, data in generated.items():
        check(data == (args.patch / name).read_bytes())
    result = dict(report, preservationChecks=checks, idempotent=True,
                  originalAtlasPixelsUnchanged=True, otherManifestContentUnchanged=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
