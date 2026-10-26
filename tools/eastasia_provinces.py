"""Modern province coverage for map authoring; not scenario ownership or borders."""

import json
import math
from pathlib import Path

from shapely.geometry import Point, shape

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT/'maps/source_cache/china_admin1_datav_20261026.geojson'
SOURCE_URL = 'https://geo.datav.aliyun.com/areas_v3/bound/100000_full.json'


def provinces():
    return [(str(f['properties']['adcode']), f['properties']['name'], shape(f['geometry']))
            for f in json.loads(SOURCE.read_text(encoding='utf-8'))['features'] if f['properties']['name']]


def lonlat(runtime, x, y):
    p = runtime.geo.PROJ
    x = p.xmin+x/runtime.SCALE*(p.xmax-p.xmin)/runtime.geo.WIDTH
    y = p.ymax-y/runtime.SCALE*(p.ymax-p.ymin)/runtime.geo.HEIGHT
    rho = math.hypot(x, p.rho0-y)
    theta = math.atan2(x, p.rho0-y)
    return math.degrees(p.lon0+theta/p.n), math.degrees(2*math.atan((p.radius*p.f/rho)**(1/p.n))-math.pi/2)


def province_at(entries, lon, lat):
    point = Point(lon, lat)
    return next((code for code, _, polygon in entries if polygon.covers(point)), None)


def cell_province(runtime, entries, cell):
    return province_at(entries, *lonlat(runtime, *runtime.center(cell % runtime.GRID_W, cell // runtime.GRID_W)))


def coverage_units(entries):
    # Hong Kong and Macau have no land-cell centers at this map's resolution.
    return {code: name for code, name, _ in entries if code not in {'810000','820000'}}
