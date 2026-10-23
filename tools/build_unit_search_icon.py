"""Reuse the right-side button and shadow, with three white hexagonal tiles."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
source = Image.open(root / "rewrite/data/client/ui/image_recruit_hd/button_round.png").convert("RGBA")
factor = 4
# Remove the hourglass, preserving the original frame and directional shadow.
button = source.copy()
for y in range(23, 71):
    left, right = source.getpixel((21, y)), source.getpixel((70, y))
    for x in range(22, 70):
        t = (x - 21) / 49
        button.putpixel((x, y), tuple(round(a + (b - a) * t) for a, b in zip(left, right)))
symbol = Image.new("RGBA", (source.width * factor, source.height * factor))
draw = ImageDraw.Draw(symbol)
ink = (230, 230, 230, 255)
for cx, cy in ((35, 36), (35, 51), (48, 43.5)):
    draw.polygon([(round((cx + dx) * factor), round((cy + dy) * factor))
                  for dx, dy in ((-8, 0), (-4, -6.5), (4, -6.5), (8, 0), (4, 6.5), (-4, 6.5))], fill=ink)
button = Image.alpha_composite(button, symbol.resize(source.size, Image.Resampling.LANCZOS))
button.save(root / "rewrite/Ew4.Web/wwwroot/unit-search.png")
print(f"Original-style unit search icon: {source.width}x{source.height}")
