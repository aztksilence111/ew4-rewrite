"""Audit existing maps against independent GSI and BKG cartography, without editing maps."""

from concurrent.futures import ThreadPoolExecutor
import argparse
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import subprocess
from urllib.parse import urlencode

import build_japan_hre_maps as b
from PIL import Image, ImageDraw

OUT=b.ROOT/'maps/japan_hre_source_20261026/geography_audit'
CACHE=b.ROOT/'maps/source_cache/geography_audit_20261026'
REGIONS=[
    ('japan','kansai','近畿：京都、大阪与奈良',(134.9,34.25,136.4,35.5),'gsi',9),
    ('japan','central_japan','日本中部：三大山系、盆地与富士山',(136.5,35.0,139.4,37.0),'gsi',8),
    ('japan','kanto','关东：平原与利根川',(138.9,35.1,141.0,37.0),'gsi',8),
    ('japan','tohoku','东北：奥羽、北上山地及河流',(139.3,37.3,142.2,40.6),'gsi',8),
    ('japan','hokkaido','北海道：中央山地与石狩平原',(140.7,41.8,145.2,45.2),'gsi',7),
    ('japan','korea','朝鲜半岛：山系与主要河流',(124.8,34.0,130.6,42.5),'bkg',None),
    ('hre','alps','阿尔卑斯山与波河平原',(5.8,44.2,15.5,48.7),'bkg',None),
    ('hre','rhine','莱茵河谷、孚日与黑森林',(5.8,47.3,9.8,50.8),'bkg',None),
    ('hre','bohemia','波希米亚盆地与周围山系',(11.5,48.2,17.8,51.6),'bkg',None),
    ('hre','north_plain','北德平原与低地',(3.5,50.8,17.0,56.0),'bkg',None),
]
VALLEYS={
    'japan':[('京都盆地',135.77,35.01),('大阪平原',135.5,34.69),('奈良盆地',135.80,34.67),
             ('甲府盆地',138.57,35.66),('松本盆地',137.97,36.24),('长野盆地',138.20,36.65),
             ('东京周边平原',139.76,35.69),('浓尾平原',136.91,35.18)],
    'hre':[('斯特拉斯堡莱茵河谷',7.75,48.58),('弗赖堡莱茵河谷边缘',7.85,48.0),
           ('因斯布鲁克因河谷',11.40,47.27),('萨尔茨堡盆地',13.04,47.80),
           ('林茨多瑙河谷',14.29,48.31),('波河平原',10.04,45.06),('柏林周边平原',13.4,52.5)]}
SOURCES={
    'gsi':dict(name='日本国土地理院 地理院タイル（標準地図）',
        template='https://cyberjapandata.gsi.go.jp/xyz/std/{z}/{x}/{y}.png',
        terms='https://maps.gsi.go.jp/development/ichiran.html',
        credit='地理院タイル（標準地図）を加工して作成。Blue/red audit marks added by this project.'),
    'bkg':dict(name='德国联邦制图与大地测量局 BKG TopPlusOpen',
        template='https://sgx.geodatenzentrum.de/wms_topplus_open',
        terms='https://gdz.bkg.bund.de/index.php/default/webdienste/topplusopen.html',
        credit='(c) Bundesamt fuer Kartographie und Geodaesie, TopPlusOpen, dl-de/by-2-0; '
               'data sources: https://sgx.geodatenzentrum.de/web_public/gdz/datenquellen/Datenquellen_TopPlusOpen.html; '
               'outside Germany primarily OpenStreetMap. Audit marks added by this project.')}


def fetch(url,path):
    if not path.exists():
        path.parent.mkdir(parents=True,exist_ok=True)
        if 'sgx.geodatenzentrum.de' in url:
            command="Invoke-WebRequest -UseBasicParsing -Uri '"+url.replace("'","''")+"' -OutFile '"+str(path).replace("'","''")+"' -TimeoutSec 60"
            subprocess.run(['powershell.exe','-NoLogo','-NoProfile','-NonInteractive','-Command',command],
                           check=True,capture_output=True,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
        else:
            subprocess.run(['curl.exe','--fail','--silent','--show-error','--location','--max-time','60',
                            '--retry','2','--output',str(path),url],check=True,capture_output=True)
    with Image.open(path) as image:image.verify()
    return dict(url=url,path=str(path.relative_to(b.ROOT)),sha256=b.digest(path))


def merc(lon,lat):
    return (b.np.asarray(lon)+180)/360,(1-b.np.arcsinh(b.np.tan(b.np.radians(lat)))/b.np.pi)/2


def mesh(region,width=720):
    west,south,east,north=region
    left,top=merc(west,north);right,bottom=merc(east,south)
    height=round(width*float((bottom-top)/(right-left)))
    x=left+(b.np.arange(width)+.5)/width*(right-left)
    y=top+(b.np.arange(height)+.5)/height*(bottom-top)
    xx,yy=b.np.meshgrid(x,y)
    return xx,yy,xx*360-180,b.np.degrees(b.np.arctan(b.np.sinh(b.np.pi*(1-2*yy))))


def reference(region,provider,zoom,xx,yy,stem):
    if provider=='gsi':
        n=2**zoom
        x0,y0=math.floor(float(xx.min())*n),math.floor(float(yy.min())*n)
        x1,y1=math.floor(float(xx.max())*n),math.floor(float(yy.max())*n)
        jobs=[(x,y) for x in range(x0,x1+1) for y in range(y0,y1+1)]
        def load(pair):
            x,y=pair
            path=CACHE/'gsi'/str(zoom)/str(x)/f'{y}.png'
            record=fetch(SOURCES['gsi']['template'].format(z=zoom,x=x,y=y),path)
            return x,y,path,record
        mosaic=Image.new('RGB',((x1-x0+1)*256,(y1-y0+1)*256),'white')
        records=[]
        with ThreadPoolExecutor(max_workers=3) as pool:
            for x,y,path,record in pool.map(load,jobs):
                with Image.open(path) as tile:mosaic.paste(tile.convert('RGB'),((x-x0)*256,(y-y0)*256))
                records.append(record)
        return sample(mosaic,xx*n*256-x0*256,yy*n*256-y0*256),records
    left,top=merc(region[0],region[3]);right,bottom=merc(region[2],region[1])
    length=40075016.68557849
    bbox=[float(left)*length-length/2,length/2-float(bottom)*length,
          float(right)*length-length/2,length/2-float(top)*length]
    query=dict(SERVICE='WMS',VERSION='1.3.0',REQUEST='GetMap',LAYERS='web',STYLES='',
               CRS='EPSG:3857',BBOX=','.join(map(str,bbox)),WIDTH=xx.shape[1],HEIGHT=xx.shape[0],FORMAT='image/png')
    path=CACHE/'bkg'/f'{stem}.png'
    record=fetch(SOURCES['bkg']['template']+'?'+urlencode(query,safe=':/,'),path)
    with Image.open(path) as image:result=image.convert('RGB')
    assert result.size==(xx.shape[1],xx.shape[0])
    return result,[record]


def sample(image,x,y):
    pixels=b.np.asarray(image.convert('RGB'))
    xi=b.np.clip(b.np.rint(x).astype(int),0,image.width-1)
    yi=b.np.clip(b.np.rint(y).astype(int),0,image.height-1)
    return Image.fromarray(pixels[yi,xi])


def kilometres(lon1,lat1,lon2,lat2):
    a,b0,c,d=map(math.radians,(lon1,lat1,lon2,lat2))
    h=math.sin((d-b0)/2)**2+math.cos(b0)*math.cos(d)*math.sin((c-a)/2)**2
    return 6371.0088*2*math.asin(min(1,math.sqrt(h)))


def main():
    global OUT
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=OUT)
    args=parser.parse_args()
    OUT=args.output.resolve()
    OUT.mkdir(parents=True,exist_ok=True)
    protected=[b.ROOT/'maps'/key/name for key in ('japan','hre')
               for name in (key+'.bin',key+'.map.json','buildings.json','map.json')]
    protected += [b.ROOT/'maps/japan_hre_source_20261026'/name for name in ('index.html','viewer.js','viewer-data.js')]
    before={str(path.relative_to(b.ROOT)):b.digest(path) for path in protected}
    config=json.loads(b.CONFIG_PATH.read_text(encoding='utf-8'))
    maps={};views={};sites_by_map={};artwork={}
    for key in ('japan','hre'):
        view=views[key]=b.MapView(key,config[key])
        folder=b.ROOT/'maps'/key
        sites=json.loads((folder/'buildings.json').read_text(encoding='utf-8'))
        actual=[]
        for site in sites:
            lon,lat=map(float,view.lonlat(*site['world_pixel']))
            actual.append(dict(id=site['id'],name=site['name'],kind=site['kind'],source=[site['lon'],site['lat']],
                actual=[round(lon,6),round(lat,6)],geodesic_shift_km=round(kilometres(site['lon'],site['lat'],lon,lat),3),
                reference_shift_km=site['displacement_km']))
        sites_by_map[key]=actual
        evidence=json.loads((folder/'elevation.json').read_text(encoding='utf-8'))
        terrain=b.TerrainMap.from_bytes((folder/(key+'.bin')).read_bytes())
        by_cell={r['cell']:r for r in evidence['cells']}
        valleys=[]
        for name,lon,lat in VALLEYS[key]:
            cell=view.nearest(lon,lat)
            valleys.append(dict(name=name,lon=lon,lat=lat,cell=cell,
                final_terrain_kind=terrain.cells[cell].background_kind,
                final_building=[s['name'] for s in sites if s['cell']==cell],
                **{k:v for k,v in by_cell.get(cell,{}).items() if k!='cell'}))
        city_shifts=[r['geodesic_shift_km'] for r in actual if r['kind']=='city']
        metadata=json.loads((folder/'map.json').read_text(encoding='utf-8'))
        maps[key]=dict(city_count=len(city_shifts),city_shift_over_10km=sum(d>10 for d in city_shifts),
            city_shift_over_20km=sum(d>20 for d in city_shifts),maximum_city_shift_km=max(city_shifts),
            median_city_shift_km=round(float(b.np.median(city_shifts)),3),
            exported_rivers=sorted({r['name'] for r in metadata['rivers']}),valley_samples=valleys,buildings=actual)
        with Image.open(folder/'overview.jpg') as image:artwork[key]=image.convert('RGB')
    records=[]
    for key,stem,title,bounds,provider,zoom in REGIONS:
        print('Reference comparison: '+stem,flush=True)
        xx,yy,lon,lat=mesh(bounds)
        reference_image,sources=reference(bounds,provider,zoom,xx,yy,stem)
        view=views[key];source=artwork[key]
        wx,wy=view.xy(lon,lat)
        current=sample(source,wx/view.world_width*source.width,wy/view.world_height*source.height)
        width,height=current.size
        board=Image.new('RGB',(width*2+24,height+125),'white')
        board.paste(reference_image,(0,65));board.paste(current,(width+24,65))
        draw=ImageDraw.Draw(board)
        draw.text((10,4),title,font=b.plan.font(22),fill='#172f35')
        draw.text((10,34),'外部实地底图：'+('日本国土地理院' if provider=='gsi' else 'BKG TopPlusOpen'),font=b.plan.font(16),fill='#172f35')
        draw.text((width+34,34),'当前地图源（同范围，均为北向上）',font=b.plan.font(16),fill='#172f35')
        draw.text((10,height+70),'蓝圈：候选原始经纬位置   红叉：目前落格位置   连线：偏移。参考底图只作地理核查，标记为本项目添加。',font=b.plan.font(14),fill='#172f35')
        draw.text((10,height+95),'Source: '+SOURCES[provider]['credit'],font=b.plan.font(10),fill='#38464a')
        left,top=merc(bounds[0],bounds[3]);right,bottom=merc(bounds[2],bounds[1])
        selected=[s for s in sites_by_map[key] if s['kind']=='city' and bounds[0]<s['source'][0]<bounds[2] and bounds[1]<s['source'][1]<bounds[3]]
        selected=sorted(selected,key=lambda s:-s['geodesic_shift_km'])[:8]
        for s in selected:
            points=[]
            for location in (s['source'],s['actual']):
                x,y=merc(*location);points.append((float((x-left)/(right-left)*width),float((y-top)/(bottom-top)*height)+65))
            for offset in (0,width+24):
                a,c=[(x+offset,y) for x,y in points]
                if not all(0<=x-offset<width and 65<=y<height+65 for x,y in (a,c)):continue
                draw.line((a,c),fill='#b43c46',width=2)
                x,y=a;draw.ellipse((x-5,y-5,x+5,y+5),outline='#007db7',width=3)
                x,y=c;draw.line((x-5,y-5,x+5,y+5),fill='#c32b38',width=3);draw.line((x-5,y+5,x+5,y-5),fill='#c32b38',width=3)
                draw.text((x+6,y-18),s['name'],font=b.plan.font(13),fill='#a12630',stroke_width=2,stroke_fill='white')
        path=OUT/(stem+'.jpg');board.save(path,quality=92,optimize=True)
        records.append(dict(key=key,region=stem,title=title,bounds=bounds,provider=provider,
                            comparison=str(path.relative_to(b.ROOT)),sources=sources))
    unchanged=all(b.digest(b.ROOT/path)==sha for path,sha in before.items())
    assert unchanged,'Audit must not edit original maps'
    report=dict(created_utc=datetime.now(timezone.utc).isoformat(),scope='Independent cartographic audit; no map edits',
                sources=SOURCES,regions=records,maps=maps,
                integrity=dict(files=len(before),changed=0,sha256=before))
    b.write_json(OUT/'audit.json',report)
    print(json.dumps({k:{p:r[p] for p in ('city_count','city_shift_over_10km','city_shift_over_20km','maximum_city_shift_km','median_city_shift_km','exported_rivers')} for k,r in maps.items()},ensure_ascii=False))


if __name__=='__main__':main()
