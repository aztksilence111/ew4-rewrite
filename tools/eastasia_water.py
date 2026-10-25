"""Original-style river banks, clear lakes and estuaries following coastal channels."""

from PIL import Image, ImageChops, ImageDraw
from shapely.geometry import LineString, box
from shapely.strtree import STRtree
import eastasia_hydrology


def rivers(runtime, surface, features, land_mask):
    lines, widths, names = [], [], []
    for geometry, attrs in features:
        rank = attrs.get('scalerank')
        width = 6 if rank is not None and float(rank) < 5 else 4
        for line in runtime.geo.geometries(geometry, 'LineString'):
            if len(line.coords) < 2:
                continue
            lines.append(LineString([runtime.source_to_image(x, y) for x, y in line.coords]))
            widths.append(width)
            names.append(attrs.get('name_en') or attrs.get('name'))
    mouths = eastasia_hydrology.estuaries(runtime, lines, widths, names, land_mask)
    runtime.water_mouth_report = [dict(name=m['name'], endpoint=m['endpoint'], length=m['length'],
                                     path=list(m['path'].coords)) for m in mouths]
    for mouth in mouths:
        lines.append(mouth['path'])
        widths.append(mouth['width'])
    tree = STRtree(lines)
    factor, guard, block = 3, 12, 1024
    for top in range(0, surface.height, block):
        for left in range(0, surface.width, block):
            right, bottom = min(left+block, surface.width), min(top+block, surface.height)
            bounds = (left-guard, top-guard, right+guard, bottom+guard)
            candidates = tree.query(box(*bounds))
            if not len(candidates):
                continue
            layer = Image.new('RGBA', ((right-left+2*guard)*factor, (bottom-top+2*guard)*factor))
            draw = ImageDraw.Draw(layer)
            # Paint each band for the whole network so junctions have no dark crossbars.
            for extra, color in [(4, (137, 163, 164, 255)), (2, (51, 78, 91, 255)),
                                 (0, (85, 136, 170, 255))]:
                for index in candidates:
                    line = lines[index].intersection(box(*bounds))
                    for part in runtime.geo.geometries(line, 'LineString'):
                        points = [((x-bounds[0])*factor, (y-bounds[1])*factor) for x, y in part.coords]
                        width = (widths[index]+extra)*factor
                        draw.line(points, fill=color, width=width, joint='curve')
                        radius = width/2
                        for x, y in (points[0], points[-1]):
                            draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill=color)
            layer = layer.resize((right-left+2*guard, bottom-top+2*guard), Image.Resampling.LANCZOS)
            layer = layer.crop((guard, guard, guard+right-left, guard+bottom-top))
            land = land_mask.crop((left, top, right, bottom))
            alpha = layer.getchannel('A')
            # Paths open the coast through narrow channels, but their banks stop on land.
            # Existing water supplies the outlet; drawing a river over it creates a pasted tail.
            water = ImageChops.multiply(alpha, ImageChops.invert(land))
            patch = surface.crop((left, top, right, bottom))
            patch.paste((*runtime.geo.SEA, 255), (0, 0, right-left, bottom-top), water)
            layer.putalpha(ImageChops.multiply(alpha, land))
            patch.alpha_composite(layer)
            surface.paste(patch, (left, top))
    return surface
