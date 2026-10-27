"""Check coastal continuity, AA artwork, small islands and preserved East Asia rules."""
import json
import math

import update_eastasia_coast as u
from PIL import Image
from shapely.geometry import LineString, Point

r = u.r


def main():
    report = u.read(u.REPORT)
    manifest = u.read(r.BASE/'client/manifest.json')
    checks = 0

    def check(value, message):
        nonlocal checks
        assert value, message
        checks += 1

    for name, sha in report['protected'].items():
        if name not in ('client/manifest.json', 'client/animations/maptext.json'):
            check(u.digest(r.BASE/name) == sha, name+' unchanged')
    for name, sha in report['planning'].items():
        check(u.digest(r.REPORT/name) == sha, name+' unchanged')
    tiles = manifest['maps']['eastasia']['tiles']
    images = {t['file']:Image.open(r.BASE/'client'/t['file']).convert('RGBA') for t in tiles}
    original = {t['file']:Image.open(u.OUT/'before'/t['file']).convert('RGBA') for t in tiles}
    overlap_pairs = 0
    for i, a in enumerate(tiles):
        for b in tiles[i+1:]:
            left, top = max(a['x'],b['x']), max(a['y'],b['y'])
            right = min(a['x']+a['width'], b['x']+b['width'])
            bottom = min(a['y']+a['height'], b['y']+b['height'])
            if left >= right or top >= bottom:
                continue
            pa = r.np.asarray(images[a['file']].crop((left-a['x'],top-a['y'],right-a['x'],bottom-a['y'])))
            pb = r.np.asarray(images[b['file']].crop((left-b['x'],top-b['y'],right-b['x'],bottom-b['y'])))
            check(r.np.array_equal(pa,pb), 'Overlapping coastal tiles have identical pixels')
            overlap_pairs += 1

    def pixel(x, y, before=False):
        wx, wy = x+r.IMAGE_LEFT, y+r.IMAGE_TOP
        t = next(t for t in tiles if t['x']<=wx<t['x']+t['width'] and t['y']<=wy<t['y']+t['height'])
        return (original if before else images)[t['file']].getpixel((round(wx-t['x']),round(wy-t['y'])))

    bridge_rects = []
    for bridge in u.read(r.REPORT/'bridges.json'):
        s = manifest['atlases']['buildings'][f"bridge_{bridge['style']}_{bridge['direction']+1}.png"]
        x,y = bridge['world_pixel']
        left, top = round(x-s['refx']-r.IMAGE_LEFT), round(y-s['refy']-r.IMAGE_TOP)
        bridge_rects.append((left,top,left+s['w'],top+s['h']))
    mask = Image.open(r.REPORT/'land_mask.png').convert('L')
    visual = Image.open(u.OUT/'visual-mask.png').convert('L')
    samples = covered = sea_samples = 0
    fade_old, fade_new = [], []
    for mouth in report['mouths']:
        path = LineString(mouth['path'])
        start = path.project(Point(mouth['endpoint']))
        for distance in range(math.ceil(start),math.floor(path.length)):
            p = path.interpolate(distance)
            x,y = round(p.x),round(p.y)
            if any(l<=x<rr and t<=y<b for l,t,rr,b in bridge_rects):
                covered += 1
                continue
            area = [pixel(x+dx,y+dy) for dx in (-1,0,1) for dy in (-1,0,1)]
            check(any(a>=245 and 55<=red<=105 and 90<=green<=148 and 125<=blue<=185
                      for red,green,blue,a in area), mouth['name']+' continuous water '+str((x,y)))
            if mask.crop((x-9,y-9,x+10,y+10)).getextrema()[1] == 0:
                check(all(c[3]==255 and all(abs(a-b)<=1 for a,b in zip(c[:3],r.geo.SEA)) for c in area),
                      mouth['name']+' no banks or appended river tails in open sea')
                sea_samples += 1
            old, new = pixel(x,y,True), pixel(x,y)
            if mask.getpixel((x,y))>=128 and visual.getpixel((x,y))>=220 and old[3]==new[3]==255:
                if 55<=old[0]<=105 and 110<=old[1]<=148 and 140<=old[2]<=185:
                    fade_old.append(sum(abs(a-b) for a,b in zip(old[:3],r.geo.SEA)))
                    fade_new.append(sum(abs(a-b) for a,b in zip(new[:3],r.geo.SEA)))
            samples += 1
        p = path.interpolate(path.length)
        x,y = round(p.x),round(p.y)
        check(mask.crop((x-14,y-14,x+15,y+15)).getextrema()[1]==0, mouth['name']+' still reaches open sea')
    check(not report['mouthFailures'] and len(report['mouths'])==46, 'All previous estuary routes retained')
    check(len(fade_new)>20 and sum(fade_new)<sum(fade_old)*.9, 'River-mouth colors approach receiving sea color')
    check(report['antialiasPixels']>100000 and report['coastalThresholdChangesBeyond2px']==0,
          'Antialiasing is confined to the original coastal raster edge')
    dark = shallow = 0
    for image in images.values():
        pixels = r.np.asarray(image)
        dark += int(r.np.all(pixels == (58,76,83,255),axis=2).sum())
        shallow += int(r.np.all(pixels == (119,151,159,255),axis=2).sum())
    check(dark>10000 and shallow>10000, 'Distinct dark inner-bank and pale outer-shore bands exist')
    land,_,_ = r.geography()
    islands = 0
    for polygon in r.geo.geometries(land,'Polygon'):
        if polygon.area*4 < 16:
            continue
        p = polygon.representative_point()
        x,y = [round(v) for v in r.source_to_image(p.x,p.y)]
        if not (3<=x<visual.width-3 and 3<=y<visual.height-3):
            continue
        # Subpixel inlets can place the mathematical representative point between raster pixels.
        if mask.crop((x-1,y-1,x+2,y+2)).getextrema()[1]<128:
            continue
        check(visual.crop((x-2,y-2,x+3,y+3)).getextrema()[1]>=128, 'Existing small land component retained')
        islands += 1
    index = {entry['path']:entry for entry in u.read(r.BASE/'runtime-content.json')['files']}
    for t in tiles:
        name = 'client/'+t['file']
        check(u.digest(r.BASE/name)==index[name]['sha256'], 'Runtime artwork index matches coastal PNG')
        check(u.digest(r.BASE/name)==u.digest(r.OUT/name), 'Regenerable source and built-in coastal PNG match')
    result = dict(checks=checks, errors=0, tiles=len(tiles), overlapPairs=overlap_pairs, overlapPixelDifferences=0,
                  mouths=46, waterSamples=samples, bridgeCoveredSamples=covered, openSeaSamples=sea_samples,
                  antialiasPixels=report['antialiasPixels'], darkBankPixels=dark, shallowShorePixels=shallow,
                  sampledLandComponents=islands, riverBlendSamples=len(fade_new),
                  mouthColorDistanceBefore=round(sum(fade_old)/len(fade_old),3),
                  mouthColorDistanceAfter=round(sum(fade_new)/len(fade_new),3),
                  coastalThresholdChangesBeyond2px=0, protectedGameplayChanged=0)
    r.write_json(r.REPORT/'coast-correction-verification-20261027.json', result)
    report['status'] = 'source artwork verified'
    r.write_json(u.REPORT, report)
    print(json.dumps(result))


if __name__ == '__main__':
    main()
