"""Render every classified terrain cell with its native game sprite."""

from collections import Counter

from eastasia_settlements import hex_distance, neighbors


def materials(data, evidence, width, height):
    assigned={}
    for sample in evidence['cells']:
        cell=sample['cell'];kind=int(data[cell,0])
        if kind not in (8,9):
            continue
        if kind==8:
            relief=sample['near_relief_m']
            pool=(0,1) if sample['river_valley'] or relief<180 else (2,3) if relief<300 else (4,5)
        else:
            pool=tuple(range(6))
        preferred=int(sample['slope_aspect_degrees']//60)%6
        nearby=Counter(assigned[n] for n in neighbors(cell,width,height) if n in assigned and int(data[n,0])==kind)
        def score(index):
            distance=min((index-preferred)%6,(preferred-index)%6)
            return nearby[index]*3+distance,index
        index=min(pool,key=score)
        data[cell,1]=index
        assigned[cell]=index


def game_tiles(rows, atlas, appearance=None, scales=None):
    result=[]
    for row in rows:
        if row.get('cellKind') in (8,9):
            sprite=atlas[row['image']]
            if appearance == 'native-range-bands':
                row=dict(row,sourceImage=row['image'],opacity=1)
                if row['cellKind']==9:
                    scale=(scales or {'low':.84,'middle':.98,'high':1.12})[row['relief']]
                else:
                    scale=.75 if row['cellIndex']<2 else .65 if row['cellIndex']<4 else .58
                row.update(scaleX=scale,scaleY=scale)
                result.append(row)
                continue
            # Fit each native sprite to its hex footprint so valleys stay visible.
            width,height=60,46
            if appearance == 'native-height-hierarchy':
                if row['cellKind'] == 8:
                    width,height=(44,18) if row['cellIndex']==0 else (52,24)
                else:
                    width,height={'low':(54,34),'middle':(64,44),'high':(74,52)}[row['relief']]
            scale=min(1,width/sprite['w'],height/sprite['h'])
            row=dict(row,scaleX=scale,scaleY=scale,opacity=1)
        result.append(row)
    return sorted(result,key=lambda row:(row['y'],row['x']))


def overview_landforms(rows, evidence, width, hill_spacing=3):
    samples={s['cell']:s for s in evidence['cells']}
    mountains=[r for r in rows if r.get('cellKind')==9]
    hills=[r for r in rows if r.get('cellKind')==8]
    # Select actual local high points, in order of relief, rather than random dots.
    for group,spacing in ((mountains,1),(hills,hill_spacing)):
        group.sort(key=lambda r:(-samples[r['cell']]['center_m'],-samples[r['cell']]['near_relief_m'],r['cell']))
        same=[]
        for row in group:
            row['overview']=False
            if any(hex_distance(row['cell'],other['cell'],width)<spacing for other in same):
                continue
            row['overview']=True
            row['overviewScale']=1
            same.append(row)


def material_catalog(atlas, source, target, font):
    from PIL import Image, ImageDraw
    target.parent.mkdir(parents=True,exist_ok=True)
    image=Image.new('RGB',(960,440),'#d8d9d0')
    draw=ImageDraw.Draw(image)
    for group,stem in enumerate(('hill','mountain')):
        for index in range(6):
            x=(index%3)*320;y=group*220+(index//3)*110
            name=f'{stem}_{index+1}.png';s=atlas[name]
            crop=source.crop((s['x'],s['y'],s['x']+s['w'],s['y']+s['h']))
            image.paste(crop,(x+12,y+10),crop)
            draw.text((x+132,y+20),name,font=font,fill='#263735')
            draw.text((x+132,y+50),f"{int(s['w'])} x {int(s['h'])}",font=font,fill='#52615d')
    image.save(target,quality=95,optimize=True)
