"""Check continuous plain cores, actual terrain colours and readable overview LOD."""

from collections import Counter
import json

import build_japan_hre_maps as b


PLAINS = [
    ('关东平原',139.45,35.55,140.15,36.15,.95),
    ('浓尾平原',136.65,35.07,136.9,35.35,.95),
    ('大阪平原',135.4,34.5,135.65,34.8,.95),
    ('石狩平原',141.45,43.0,141.85,43.35,.95),
    ('仙台平原',140.88,38.1,141.08,38.4,.9),
    ('新潟平原',138.85,37.65,139.2,37.95,.9),
    ('甲府盆地',138.48,35.6,138.68,35.72,.7),
    ('松本谷地',137.88,36.17,138.03,36.42,.4),
]


def main():
    config=json.loads(b.CONFIG_PATH.read_text(encoding='utf-8'))
    view=b.MapView('japan',config['japan'])
    folder=b.ROOT/'maps/japan'
    terrain=b.TerrainMap.from_bytes((folder/'japan.bin').read_bytes())
    kinds=b.np.array([c.background_kind for c in terrain.cells])
    lon,lat=view.lonlat(view.x,view.y)
    artwork=json.loads((folder/'map.json').read_text(encoding='utf-8'))
    checks=0
    def check(value,message):
        nonlocal checks
        assert value,message
        checks+=1
    results=[]
    for name,left,bottom,right,top,minimum in PLAINS:
        cells=set(b.np.flatnonzero((lon>=left)&(lon<=right)&(lat>=bottom)&(lat<=top)&(kinds!=1)).tolist())
        plain={c for c in cells if kinds[c]==0}
        check(len(cells)>=4,name+' multiple grid cells')
        check(len(plain)/len(cells)>=minimum,name+' open plain core')
        if minimum>=.9:
            check(all(kinds[c] in (0,8) for c in cells),name+' no mountain barrier inside plain core')
        components=[];remaining=set(plain)
        while remaining:
            seed=remaining.pop();frontier=[seed];component={seed}
            while frontier:
                cell=frontier.pop()
                for other in b.neighbors(cell,view.width,view.height):
                    if other in remaining:
                        remaining.remove(other);component.add(other);frontier.append(other)
            components.append(len(component))
        check(max(components)>=len(plain)*.65,name+' connected open ground')
        results.append(dict(name=name,coreCells=len(cells),plainCells=len(plain),mountainEdgeCells=int(sum(kinds[c]==9 for c in cells)),largestPlainComponent=max(components)))
    detail=[r for r in artwork['overlays'] if r.get('cellKind') in (8,9)]
    overview=[r for r in detail if r.get('overview')]
    glyph_area=sum(b.ART['atlases']['tiles'][r['image']]['w']*r['scaleX']*b.ART['atlases']['tiles'][r['image']]['h']*r['scaleY'] for r in overview)
    coverage=glyph_area/(int(b.np.count_nonzero(kinds!=1))*64*54)
    atlas=b.Image.open(b.BASE/'client/images/tiles.png').convert('RGBA')
    opaque_pixels={}
    for name in {r['image'] for r in overview}:
        s=b.ART['atlases']['tiles'][name]
        alpha=atlas.crop((s['x'],s['y'],s['x']+s['w'],s['y']+s['h'])).getchannel('A')
        opaque_pixels[name]=int(b.np.count_nonzero(b.np.asarray(alpha)>=32))
    opaque_coverage=sum(opaque_pixels[r['image']]*r['scaleX']*r['scaleY'] for r in overview)/(int(b.np.count_nonzero(kinds!=1))*64*54)
    check(opaque_coverage<.16,'actual opaque terrain pixels leave readable ground')
    check(all(r.get('overview') for r in detail if r['cellKind']==9),'all primary mountain glyphs remain in overview')
    check({r['cellIndex'] for r in detail if r['cellKind']==8}==set(range(6)),'six native hill forms cover different real relief')
    check(not b.np.any(kinds==6),'random unsourced forests removed from plains')
    band_heights={}
    for level in ('hill','low','middle','high'):
        group=[r for r in detail if r['relief']==level]
        heights=[b.ART['atlases']['tiles'][r['image']]['h']*r['scaleY'] for r in group]
        band_heights[level]=dict(minimum=min(heights),maximum=max(heights),median=float(b.np.median(heights)))
    for lower,upper in zip(('hill','low','middle'),('low','middle','high')):
        check(band_heights[lower]['median']<band_heights[upper]['median'],lower+' and '+upper+' have increasing native size')
    check(len({r['image'] for r in detail if r['cellKind']==9})==6,'six unmodified original mountain shapes')
    check(all(r['scaleX']==r['scaleY'] for r in detail),'native aspect ratios retained')
    check(not artwork.get('ridgeOverlays'),'no repeated shoulder stamps')
    check(all(r['image']==artwork['terrainRules'][r['cellKind']]['sprites'][str(r['cellIndex'])] for r in detail),'visible artwork exactly matches original cell definition')
    check(all(r.get('overviewScale',1)==1 for r in detail),'overview preserves the same high-low geometry')
    check(artwork['terrainPalette'] is None,'same original textured ground as East Asia, no tinted terrain polygons')
    for group,spacing in (([r for r in overview if r['cellKind']==8],3),):
        for i,row in enumerate(group):
            check(all(b.hex_distance(row['cell'],other['cell'],view.width)>=spacing for other in group[i+1:]),'overview glyph spacing')
    image=b.Image.open(folder/'base.jpg').convert('RGB')
    scale=image.width/view.world_width
    brightness={}
    for kind in (0,8,9):
        cells=[i for i,c in enumerate(terrain.cells) if c.background_kind==kind and not c.river_flags]
        cells=cells[::max(1,len(cells)//400)]
        values=[]
        for cell in cells:
            x,y=int(view.x[cell]*scale),int(view.y[cell]*scale)
            if 2<=x<image.width-2 and 2<=y<image.height-2:
                values.append(sum(image.getpixel((x,y)))/3)
        brightness[kind]=float(b.np.median(values))
    check(max(brightness.values())-min(brightness.values())<5,'surface texture remains consistent; relief shown by installed sprites')
    report=dict(checks=checks,errors=0,plainCoreWindows=results,terrainCounts=dict(Counter(map(int,kinds))),
        detailSprites=len(detail),overviewSprites=len(overview),medianSurfaceBrightness=brightness,renderedBandHeights=band_heights,glyphFootprintFraction=coverage,opaqueGlyphFraction=opaque_coverage,
        caveat='Core windows validate continuous plains, not exact boundary polygons; unmodified original sprite per actual cell, uniform scaling only')
    b.write_json(folder/'landforms_verification.json',report)
    print(json.dumps(report,ensure_ascii=False))


if __name__=='__main__':
    main()
