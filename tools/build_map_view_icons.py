"""Build the approved map-view icons on the original pause-button face."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
source = Image.open(root / 'rewrite/data/client/ui/image_recruit_hd/button_pause.png').convert('RGBA')
for mode in ['normal', 'camp', 'district']:
    base = source.copy()
    for y in range(12, 45):
        left, right = source.getpixel((25, y)), source.getpixel((65, y))
        for x in range(28, 61):
            t = (x - 25) / 40
            base.putpixel((x, y), tuple(round(a * (1-t) + b*t) for a, b in zip(left, right)))
    layer = Image.new('RGBA', (73*4, 74*4))
    draw = ImageDraw.Draw(layer)
    ink = (211, 211, 211, 255)

    def line(points, width=2.5):
        draw.line([(round(x*4), round(y*4)) for x, y in points], fill=ink, width=round(width*4), joint='curve')

    def polygon(points):
        draw.polygon([(round(x*4), round(y*4)) for x, y in points], fill=ink)

    if mode == 'normal':
        line([(28,17),(38,13),(49,17),(60,13),(60,39),(49,43),(38,39),(28,43),(28,17)])
        line([(38,13),(38,39)], 2)
        line([(49,17),(49,43)], 2)
    elif mode == 'camp':
        line([(31,42),(31,14)], 2.8)
        polygon([(32,14),(45,17),(42,26),(32,23)])
        line([(49,42),(49,20)], 2.8)
        polygon([(50,20),(63,23),(60,32),(50,29)])
        line([(27,43),(55,43)], 2)
    else:
        for cx, cy, filled in [(37.5,20,False),(37.5,35,False),(50.5,27.5,True)]:
            points = [(cx+dx,cy+dy) for dx,dy in [(-8,0),(-4,-6.5),(4,-6.5),(8,0),(4,6.5),(-4,6.5)]]
            if filled:
                polygon(points)
            else:
                line(points + [points[0]], 2.1)
    Image.alpha_composite(base, layer.resize(base.size, Image.Resampling.LANCZOS)).crop((18,1,73,56)).save(
        root / f'rewrite/Ew4.Web/wwwroot/map-{mode}.png')
print('Built 3 map-view button faces.')
