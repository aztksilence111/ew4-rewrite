"""Original-style river banks, clear lakes and estuaries following coastal channels."""

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter
from shapely.geometry import LineString, box
from shapely.strtree import STRtree
import eastasia_hydrology


def dilate(mask, radius):
    values = np.asarray(mask).copy()
    # Separable expanding windows avoid a large square filter at 3x density.
    for axis in (0, 1):
        remaining, step = radius, 1
        while remaining:
            shift = min(step, remaining)
            before = values.copy()
            if axis == 0:
                np.maximum(values[shift:], before[:-shift], out=values[shift:])
                np.maximum(values[:-shift], before[shift:], out=values[:-shift])
            else:
                np.maximum(values[:, shift:], before[:, :-shift], out=values[:, shift:])
                np.maximum(values[:, :-shift], before[:, shift:], out=values[:, :-shift])
            remaining -= shift
            step *= 2
    return Image.fromarray(values)


def coast_mask(runtime, polygons, tree, placements, original, bounds, factor):
    left, top, right, bottom = bounds
    mask = Image.new('L', ((right-left)*factor, (bottom-top)*factor))
    draw = ImageDraw.Draw(mask)
    source_bounds = box((left+runtime.IMAGE_LEFT)/2, (top+runtime.IMAGE_TOP)/2,
                        (right+runtime.IMAGE_LEFT)/2, (bottom+runtime.IMAGE_TOP)/2)

    def project(points):
        return [((x*2-runtime.IMAGE_LEFT-left)*factor,
                 (y*2-runtime.IMAGE_TOP-top)*factor) for x, y in points]

    # Draw enclosing land first so its lake holes cannot erase islands returned earlier by STRtree.
    for index in sorted(tree.query(source_bounds), key=lambda i: polygons[int(i)].area, reverse=True):
        for polygon in runtime.geo.geometries(polygons[int(index)].intersection(source_bounds), 'Polygon'):
            draw.polygon(project(polygon.exterior.coords), fill=255)
            for ring in polygon.interiors:
                draw.polygon(project(ring.coords), fill=0)
    for city in placements:
        if not city.get('visual_land_patch'):
            continue
        x, y = city['world_pixel']
        x, y = (x-runtime.IMAGE_LEFT-left)*factor, (y-runtime.IMAGE_TOP-top)*factor
        draw.ellipse((x-17*factor, y-14*factor, x+17*factor, y+14*factor), fill=255)
    # Keep established island interiors and narrow-water seeds; only the two-pixel edge can be resampled.
    previous = original.crop(bounds)
    valid = Image.new('L', previous.size)
    ImageDraw.Draw(valid).rectangle((max(0, -left), max(0, -top),
                                    min(right, original.width)-left-1,
                                    min(bottom, original.height)-top-1), fill=255)
    dry = ImageChops.multiply(ImageChops.invert(dilate(ImageChops.invert(previous), 2)), valid)
    wet = ImageChops.multiply(ImageChops.invert(dilate(previous, 2)), valid)
    mask = ImageChops.lighter(mask, dry.resize(mask.size, Image.Resampling.NEAREST))
    mask = ImageChops.multiply(mask, ImageChops.invert(wet.resize(mask.size, Image.Resampling.NEAREST)))
    return mask


def background(runtime, land, features, placements, land_mask):
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
    polygons = list(runtime.geo.geometries(land, 'Polygon'))
    coast_tree = STRtree(polygons)
    surface = Image.new('RGBA', land_mask.size)
    runtime.water_visual_mask = Image.new('L', land_mask.size)
    factor, guard, block = 3, 40, 1024
    for top in range(0, surface.height, block):
        for left in range(0, surface.width, block):
            right, bottom = min(left+block, surface.width), min(top+block, surface.height)
            bounds = (left-guard, top-guard, right+guard, bottom+guard)
            mask = coast_mask(runtime, polygons, coast_tree, placements, land_mask, bounds, factor)
            patch = Image.new('RGBA', mask.size, (*runtime.geo.SEA, 255))
            patch.putalpha(ImageChops.invert(mask))
            inner = ImageChops.subtract(mask, ImageChops.invert(dilate(ImageChops.invert(mask), 2*factor)))
            shore = ImageChops.subtract(dilate(mask, 4*factor), mask)
            patch.paste((58, 76, 83, 255), (0, 0, *mask.size), inner)
            patch.paste((119, 151, 159, 255), (0, 0, *mask.size), shore)
            candidates = tree.query(box(*bounds))
            layer = Image.new('RGBA', mask.size)
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
            alpha = layer.getchannel('A')
            # The final river bands lose their blue and bank contrast on approach to receiving water.
            fade = ImageChops.subtract(mask.filter(ImageFilter.GaussianBlur(8*factor)),
                                       Image.new('L', mask.size, 128)).point(lambda v: min(255, v*2))
            receiving = Image.new('RGBA', mask.size, (*runtime.geo.SEA, 0))
            receiving.putalpha(alpha)
            layer = Image.composite(layer, receiving, fade)
            # Paths open the coast through narrow channels, but their banks stop on land.
            # Existing water supplies the outlet; drawing a river over it creates a pasted tail.
            water = ImageChops.multiply(alpha, ImageChops.invert(mask))
            patch.paste((*runtime.geo.SEA, 255), (0, 0, *mask.size), water)
            layer.putalpha(ImageChops.multiply(alpha, mask))
            patch.alpha_composite(layer)
            size = (right-left+2*guard, bottom-top+2*guard)
            crop = (guard, guard, guard+right-left, guard+bottom-top)
            patch = patch.resize(size, Image.Resampling.LANCZOS).crop(crop)
            surface.paste(patch, (left, top))
            runtime.water_visual_mask.paste(mask.resize(size, Image.Resampling.LANCZOS).crop(crop), (left, top))
    return surface
