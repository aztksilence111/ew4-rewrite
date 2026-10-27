"""Antialiased East Asia style coasts and real river channels at two image densities."""

import build_japan_hre_maps as source
import hashlib
import math
import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter
from shapely.geometry import LineString, Point, box
from shapely.ops import nearest_points, unary_union
from shapely.strtree import STRtree

from eastasia_hydrology import water_path

SEA = (68,102,136,255)


def dilate(mask,radius):
    values=np.asarray(mask).copy()
    for axis in (0,1):
        remaining,step=radius,1
        while remaining:
            shift=min(step,remaining)
            before=values.copy()
            if axis==0:
                np.maximum(values[shift:],before[:-shift],out=values[shift:])
                np.maximum(values[:-shift],before[shift:],out=values[:-shift])
            else:
                np.maximum(values[:,shift:],before[:,:-shift],out=values[:,shift:])
                np.maximum(values[:,:-shift],before[:,shift:],out=values[:,:-shift])
            remaining-=shift;step*=2
    return Image.fromarray(values)


class Water:
    def __init__(self,view,metadata,sites):
        self.view,self.sites = view,sites
        self.land,self.components,_,_ = source.geography(view)
        self.polygons = STRtree(self.components)
        self.lines,self.widths,self.names = [],[],[]
        major = {'莱茵河','多瑙河','易北河','波河','维斯瓦河','罗讷河','塞纳河','利根川','信浓川','石狩川','汉江','洛东江'}
        for river in metadata['rivers']:
            for points in river['paths']:
                if len(points)<2:
                    continue
                self.lines.append(LineString(points))
                self.names.append(river['name'])
                self.widths.append(6 if river['name'] in major else 4)
        original_count = len(self.lines)
        tree = STRtree(self.lines)
        seams = []
        seen = set()
        for i,line in enumerate(self.lines[:original_count]):
            for end in (line.coords[0],line.coords[-1]):
                p=Point(end)
                choices=[int(j) for j in tree.query(p.buffer(5)) if i!=j and self.names[i]==self.names[j] and .1<p.distance(self.lines[j])<=5]
                if not choices:
                    continue
                j=min(choices,key=lambda j:p.distance(self.lines[j]))
                target=nearest_points(p,self.lines[j])[1]
                pair=tuple(sorted((tuple(p.coords[0]),tuple(target.coords[0]))))
                if pair in seen:
                    continue
                seen.add(pair)
                seams.append((LineString(pair),self.widths[i],self.names[i]))
        for line,width,name in seams:
            self.lines.append(line);self.widths.append(width);self.names.append(name)
        ratio=metadata['imageWidth']/view.world_width
        mask=self.mask((0,0,view.world_width,view.world_height),ratio)
        mouths,failures=[],[]
        seen=set()
        for line,width,name in zip(self.lines[:original_count],self.widths,self.names):
            for side in (0,-1):
                end=Point(line.coords[side])
                if not (40<end.x<view.world_width-40 and 40<end.y<view.world_height-40):
                    continue
                if end.distance(self.land.boundary)>40:
                    continue
                key=(round(end.x),round(end.y))
                if key in seen:
                    continue
                seen.add(key)
                path=water_path(mask,(end.x*ratio,end.y*ratio))
                if path is None:
                    path=water_path(mask,(end.x*ratio,end.y*ratio),radius=520)
                coast_join=None
                if path is None:
                    coast_join=nearest_points(end,self.land.boundary)[1]
                    path=water_path(mask,(coast_join.x*ratio,coast_join.y*ratio),radius=520)
                if path is None:
                    # Small lakes and narrow lagoons need a receiving-water point, not an open-sea basin.
                    cx,cy=round(end.x*ratio),round(end.y*ratio)
                    radius=math.ceil(64*ratio)
                    left,top=max(0,cx-radius),max(0,cy-radius)
                    patch=ImageChops.invert(mask.crop((left,top,min(mask.width,cx+radius+1),min(mask.height,cy+radius+1)))).filter(ImageFilter.MinFilter(5))
                    yy,xx=np.nonzero(np.asarray(patch)>=128)
                    if len(xx):
                        distances=(xx+left-cx)**2+(yy+top-cy)**2
                        i=int(np.argmin(distances))
                        if math.sqrt(float(distances[i]))/ratio<=64:
                            target=((int(xx[i])+left)/ratio,(int(yy[i])+top)/ratio)
                            path=LineString([(end.x*ratio,end.y*ratio),(target[0]*ratio,target[1]*ratio)])
                if path is None:
                    failures.append(dict(name=name,endpoint=list(end.coords[0]),coastGap=round(end.distance(self.land.boundary),3)))
                    continue
                points=[(x/ratio,y/ratio) for x,y in path.coords]
                if coast_join is not None:
                    points=[end.coords[0]]+points
                back=line.interpolate(min(12,line.length) if side==0 else max(0,line.length-12))
                extension=LineString([back.coords[0]]+points)
                mouths.append(dict(name=name,path=[list(p) for p in extension.coords],length=round(extension.length,3),endpoint=list(end.coords[0])))
                self.lines.append(extension);self.widths.append(width);self.names.append(name)
        # A sub-hex river harbour must join its actual river rather than an isolated circular hole.
        for site in sites:
            if not site.get('river_berth'):
                continue
            p=Point(site['world_pixel'])
            nearest=int(tree.nearest(p))
            target=nearest_points(p,self.lines[nearest])[1]
            assert p.distance(target)<64, site['name']+' is not on the mapped river'
            self.lines.append(LineString([p.coords[0],target.coords[0]]));self.widths.append(8);self.names.append(self.names[nearest])
        self.tree=STRtree(self.lines)
        self.report=dict(sourcePaths=original_count,sourceSeams=len(seams),mouths=mouths,unresolvedMouths=failures,
            detailWorldPixelsPerImagePixel=1,antialiasSamples=3,coastBands=['inner-bank','outer-shore'],riverBands=['bank','edge','water'])

    def mask(self,bounds,density):
        left,top,right,bottom=bounds
        mask=Image.new('L',(round((right-left)*density),round((bottom-top)*density)))
        draw=ImageDraw.Draw(mask)
        frame=box(*bounds)
        for index in self.polygons.query(frame):
            for polygon in source.plan.parts(self.components[int(index)].intersection(frame),'Polygon'):
                draw.polygon([((x-left)*density,(y-top)*density) for x,y in polygon.exterior.coords],fill=255)
                for ring in polygon.interiors:
                    draw.polygon([((x-left)*density,(y-top)*density) for x,y in ring.coords],fill=0)
        for site in self.sites:
            if site['kind']=='port':
                continue
            x,y=site['world_pixel']
            px,py=(x-left)*density,(y-top)*density
            if 0<=px<mask.width and 0<=py<mask.height and mask.getpixel((int(px),int(py)))<128:
                radius=12*density
                draw.ellipse((px-radius,py-radius,px+radius,py+radius),fill=255)
        return mask

    def tile(self,bounds,density):
        left,top,right,bottom=bounds
        guard=16
        outer=(left-guard,top-guard,right+guard,bottom+guard)
        factor=3
        scale=density*factor
        mask=self.mask(outer,scale)
        image=Image.new('RGBA',mask.size,SEA)
        image.putalpha(ImageChops.invert(mask))
        inner=ImageChops.subtract(mask,ImageChops.invert(dilate(ImageChops.invert(mask),max(1,round(2*scale)))))
        shore=ImageChops.subtract(dilate(mask,max(1,round(5*scale))),mask)
        image.paste((91,113,118,255),(0,0,*mask.size),inner)
        image.paste((119,151,159,255),(0,0,*mask.size),shore)
        candidates=self.tree.query(box(*outer))
        layer=Image.new('RGBA',mask.size)
        draw=ImageDraw.Draw(layer)
        # Whole-network bands keep confluences open, matching the East Asia treatment.
        for extra,color in ((4,(137,163,164,255)),(2,(51,78,91,255)),(0,(85,136,170,255))):
            for i in candidates:
                for line in source.plan.parts(self.lines[int(i)].intersection(box(*outer)),'LineString'):
                    points=[((x-outer[0])*scale,(y-outer[1])*scale) for x,y in line.coords]
                    width=max(1,round((self.widths[int(i)]+extra)*scale))
                    draw.line(points,fill=color,width=width,joint='curve')
                    radius=width/2
                    for x,y in (points[0],points[-1]):
                        draw.ellipse((x-radius,y-radius,x+radius,y+radius),fill=color)
        alpha=layer.getchannel('A')
        water=ImageChops.multiply(alpha,ImageChops.invert(mask))
        image.paste(SEA,(0,0,*mask.size),water)
        layer.putalpha(ImageChops.multiply(alpha,mask))
        image.alpha_composite(layer)
        image=image.resize((round((right-left+2*guard)*density),round((bottom-top+2*guard)*density)),Image.Resampling.LANCZOS)
        margin=round(guard*density)
        return image.crop((margin,margin,margin+round((right-left)*density),margin+round((bottom-top)*density)))


def signature(view,metadata,sites):
    return hashlib.sha256(source.json.dumps(dict(version=3,config=view.config,rivers=metadata['rivers'],
        sites=[{k:s.get(k) for k in ('kind','world_pixel','river_berth')} for s in sites]),sort_keys=True).encode()).hexdigest()


def tile_specs(view,metadata,detail=False):
    density=1 if detail else metadata['imageWidth']/view.world_width
    width,height=round(view.world_width*density),round(view.world_height*density)
    subfolder='water-detail' if detail else ''
    return [dict(x=left/density,y=top/density,width=(min(left+1026,width)-left)/density,height=(min(top+1026,height)-top)/density,
        file='/'.join(p for p in ('maps',view.key,subfolder,f'{left}_{top}.png') if p))
        for top in range(0,height,1024) for left in range(0,width,1024)]


def build(view,metadata,sites,folder):
    report_path=source.ROOT/'maps/japan_hre_source_20261026'/(view.key+'-water.json')
    stamp=signature(view,metadata,sites)
    cached=dict(tiles=tile_specs(view,metadata),detailTiles=tile_specs(view,metadata,True),waterDetailZoom=.55)
    if report_path.exists() and source.json.loads(report_path.read_text(encoding='utf-8')).get('inputSignature')==stamp and all((source.BASE/'client'/t['file']).exists() for t in cached['tiles']+cached['detailTiles']):
        print(view.key+': unchanged water tiles reused',flush=True)
        return cached
    water=Water(view,metadata,sites)
    folder.mkdir(parents=True,exist_ok=True)
    result={}
    for kind,density,step,subfolder in [('tiles',metadata['imageWidth']/view.world_width,1024,''),('detailTiles',1,1024,'water-detail')]:
        width,height=round(view.world_width*density),round(view.world_height*density)
        target=folder/subfolder
        target.mkdir(exist_ok=True)
        tiles=[]
        for top in range(0,height,step):
            for left in range(0,width,step):
                right,bottom=min(left+step+2,width),min(top+step+2,height)
                bounds=(left/density,top/density,right/density,bottom/density)
                tile=water.tile(bounds,density)
                filename=f'{left}_{top}.png'
                tile.save(target/filename,compress_level=6)
                tiles.append(dict(x=bounds[0],y=bounds[1],width=bounds[2]-bounds[0],height=bounds[3]-bounds[1],file='/'.join(p for p in ('maps',view.key,subfolder,filename) if p)))
        result[kind]=tiles
        print(view.key+': '+kind+' '+str(len(tiles)),flush=True)
    result['waterDetailZoom']=.55
    water.report['inputSignature']=stamp
    source.write_json(report_path,water.report)
    return result


def sync_properties(key):
    """Extend actual river edge flags only where a repaired outlet crosses dry cells."""
    folder=source.ROOT/'maps'/key
    report=source.json.loads((source.ROOT/'maps/japan_hre_source_20261026'/(key+'-water.json')).read_text(encoding='utf-8'))
    metadata=source.json.loads((folder/'map.json').read_text(encoding='utf-8'))
    config=source.json.loads((source.ROOT/'maps/dual_map_config.json').read_text(encoding='utf-8'))[key]
    view=source.MapView(key,config)
    blob=bytearray((folder/(key+'.bin')).read_bytes())
    data=np.frombuffer(blob,dtype=np.uint8,offset=16).reshape((-1,12))
    before=data[:,8].copy()
    barrier=unary_union([LineString(m['path']) for m in report['mouths']])
    selected=np.flatnonzero((data[:,0]!=1)&source.contains_xy(barrier.buffer(70),view.x,view.y))
    for cell in selected:
        for direction,other in source.enumerate_neighbor(int(cell),view):
            if direction<3 and data[other,0]!=1 and LineString([view.points[cell],view.points[other]]).intersects(barrier):
                data[cell,8]|=1<<direction
                data[other,8]|=1<<((direction+3)%6)
    document=source.json.loads((folder/(key+'.map.json')).read_text(encoding='utf-8'))
    for cell in np.flatnonzero(data[:,8]!=before):
        document['cells'][int(cell)]['terrain']['riverEdges']=int(data[cell,8])
    metadata.update(source.terrain_metadata(data,metadata['crossings']))
    (folder/(key+'.bin')).write_bytes(blob)
    (source.BASE/'original'/(key+'.bin')).write_bytes(blob)
    source.write_json(folder/(key+'.map.json'),document)
    source.write_json(folder/'map.json',metadata)
    report['riverPropertyCellsUpdated']=int(np.count_nonzero(before!=data[:,8]))
    source.write_json(source.ROOT/'maps/japan_hre_source_20261026'/(key+'-water.json'),report)
    return report['riverPropertyCellsUpdated']


def finish(key):
    folder=source.ROOT/'maps'/key
    metadata=source.json.loads((folder/'map.json').read_text(encoding='utf-8'))
    config=source.json.loads((source.ROOT/'maps/dual_map_config.json').read_text(encoding='utf-8'))[key]
    sites=source.json.loads((folder/'buildings.json').read_text(encoding='utf-8'))
    report_path=source.ROOT/'maps/japan_hre_source_20261026'/(key+'-water.json')
    old=source.json.loads(report_path.read_text(encoding='utf-8'))
    view=source.MapView(key,config)
    water=Water(view,metadata,sites)
    keys={tuple(round(v,3) for v in m['endpoint']) for m in old['mouths']}
    added=[m for m in water.report['mouths'] if tuple(round(v,3) for v in m['endpoint']) not in keys]
    dirty=unary_union([LineString(m['path']).buffer(16) for m in added])
    changed=[]
    for detail in (False,True):
        density=1 if detail else metadata['imageWidth']/view.world_width
        for tile in tile_specs(view,metadata,detail):
            bounds=(tile['x'],tile['y'],tile['x']+tile['width'],tile['y']+tile['height'])
            if not dirty.intersects(box(*bounds)):
                continue
            image=water.tile(bounds,density)
            image.save(source.BASE/'client'/tile['file'],compress_level=6)
            changed.append('client/'+tile['file'])
    water.report['inputSignature']=signature(view,metadata,sites)
    source.write_json(report_path,water.report)
    updated=sync_properties(key)
    print(source.json.dumps(dict(map=key,repairedAdditionalOutlets=len(added),tiles=len(changed),riverPropertyCellsUpdated=updated,unresolved=water.report['unresolvedMouths']),ensure_ascii=True),flush=True)
    return changed+['original/'+key+'.bin']


if __name__=='__main__':
    import argparse
    import integrate_japan_hre_runtime as integration
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--map',choices=['japan','hre','both'],default='both')
    args=parser.parse_args()
    changed=[]
    for key in (('japan','hre') if args.map=='both' else (args.map,)):
        changed.extend(finish(key))
    integration.refresh_index(set(changed))
