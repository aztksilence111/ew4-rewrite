'use strict';
(function(root) {
  const corners = [[0,-36],[32,-18],[32,18],[0,36],[-32,18],[-32,-18]];
  function point(cell, width) {
    const row = Math.floor(cell / width);
    // Native cell rectangles start one row above the grid; their centre is at -18.
    return {x:(cell % width)*64+(row % 2)*32, y:row*54-18};
  }
  function neighbor(cell, direction, width, height) {
    const row=Math.floor(cell/width), col=cell%width, odd=row%2;
    const [x,y]=[[col+1,row],[col+odd,row+1],[col+odd-1,row+1],[col-1,row],[col+odd-1,row-1],[col+odd,row-1]][direction];
    return x>=0&&x<width&&y>=0&&y<height?y*width+x:-1;
  }
  function cameraBounds(state, texture) {
    const scenario=state.cameraBounds||{left:0,top:0,width:texture.width,height:texture.height};
    if(!texture.cameraBounds)return {...scenario};
    const limit=texture.cameraBounds,left=Math.max(scenario.left,limit.left),top=Math.max(scenario.top,limit.top);
    return {left,top,width:Math.min(scenario.left+scenario.width,limit.left+limit.width)-left,
      height:Math.min(scenario.top+scenario.height,limit.top+limit.height)-top};
  }
  function hex(draw,x,y) {
    draw.beginPath();corners.forEach(([dx,dy],i)=>i?draw.lineTo(x+dx,y+dy):draw.moveTo(x+dx,y+dy));draw.closePath();
  }
  function textPath(points) {
    if(!Array.isArray(points)||points.length<3||points.length>65||points.length%2!==1||
      points.some(p=>!Array.isArray(p)||p.length!==2||!p.every(Number.isFinite)))return null;
    const samples=[{distance:0,x:points[0][0],y:points[0][1]}];
    for(let i=0;i<points.length-2;i+=2){const [a,b,c]=points.slice(i,i+3);
      for(let step=1;step<=64;step++){const t=step/64,x=(1-t)**2*a[0]+2*(1-t)*t*b[0]+t*t*c[0],y=(1-t)**2*a[1]+2*(1-t)*t*b[1]+t*t*c[1],last=samples[samples.length-1];
        samples.push({distance:last.distance+Math.hypot(x-last.x,y-last.y),x,y});
      }
    }
    const length=samples[samples.length-1].distance;if(!(length>0))return null;
    return {length,samples,at(distance){
      let low=1,high=samples.length-1;while(low<high){const mid=(low+high)>>1;if(samples[mid].distance<distance)low=mid+1;else high=mid;}
      const a=samples[low-1],b=samples[low],t=b.distance>a.distance?Math.max(0,Math.min(1,(distance-a.distance)/(b.distance-a.distance))):0;
      return {x:a.x+(b.x-a.x)*t,y:a.y+(b.y-a.y)*t,angle:Math.atan2(b.y-a.y,b.x-a.x)};
    }};
  }
  function pathLettering(lib,label,path) {
    const sea=!!label.sea,regional=label.geographicStyle==='regional',family=sea?'s_s_c':regional?'l_b':'l_s',height=Number(label.size);
    const base=height/Math.max(...[...'abcdefghijklmnopqrstuvwxyz'].map(c=>Number(lib.sprites.get(family+'_'+c+'.png').h)));
    const text=sea&&regional?label.text.toLowerCase().replace(/\b\w/g,c=>c.toUpperCase()).replace(/ Of /g,' of '):label.text;
    const letters=[];let cursor=0;
    for(const char of text){if(char===' '){cursor+=height*.48;continue;}
      let name=(sea&&regional&&char===char.toLowerCase()?'s_s_l':family)+'_'+char.toLowerCase(),scale=base;
      if(sea&&regional&&char==='o'){name='s_s_c_o';scale*=Number(lib.sprites.get('s_s_l_o.png').h)/Number(lib.sprites.get('s_s_c_o.png').h);}
      else if(sea&&regional&&char==='f')scale*=.5;
      const item=lib.byName.get(name),sprite=lib.sprites.get(name+'.png');if(!item||!sprite)return [];
      letters.push({item,cursor,scale,width:Number(sprite.w)*scale,height:Number(sprite.h)*scale});cursor+=Number(sprite.w)*scale;
    }
    const target=Number(label.pathChordLength)||path.length,fit=regional&&cursor>target?target/cursor:1,spacing=letters.length>1?Math.max(0,(target-cursor)/(letters.length-1)):0;
    return letters.map((letter,i)=>{const w=letter.width*fit,p=path.at(((letter.cursor+i*spacing)*fit+w/2)/Math.max(target,cursor*fit)*path.length);
      return {...letter,scale:letter.scale*fit,width:w,height:letter.height*fit,...p};});
  }
  function mapLabelBounds(label) {
    const path=textPath(label.textPath);
    if(path){const pad=Number(label.size)*.85+8,x=path.samples.map(p=>p.x+Number(label.tx)),y=path.samples.map(p=>p.y+Number(label.ty));
      const bounds={left:Math.min(...x)-pad,top:Math.min(...y)-pad,right:Math.max(...x)+pad,bottom:Math.max(...y)+pad};
      const radius=Number(label.characterSizeZh||label.size)*.85+8;
      for(const p of label.characterPositionsZh||[]){if(!Number.isFinite(p.x)||!Number.isFinite(p.y))continue;
        const px=Number(label.tx)+p.x,py=Number(label.ty)+p.y;
        bounds.left=Math.min(bounds.left,px-radius);bounds.top=Math.min(bounds.top,py-radius);
        bounds.right=Math.max(bounds.right,px+radius);bounds.bottom=Math.max(bounds.bottom,py+radius);}
      return bounds;}
    return paddedBounds(Number(label.tx),Number(label.ty),Math.max(Number(label.width),Number(label.height)));
  }
  function pose(motion,width,time) {
    const progress=Math.max(0,Math.min(motion.path.length-1,(time-motion.started)/motion.stepMs));
    const step=Math.min(motion.path.length-2,Math.floor(progress));
    const from=point(motion.path[Math.max(0,step)],width),to=point(motion.path[Math.max(0,step)+1],width),fraction=progress-step;
    return {x:from.x+(to.x-from.x)*fraction,y:from.y+(to.y-from.y)*fraction,
      facing:to.x<from.x?-1:1,cell:motion.path[Math.round(progress)],moving:progress<motion.path.length-1};
  }
  function badgeAlpha(unit,state,time) {
    if(unit.owner!==state.playerFaction || state.activeFaction!==state.playerFaction || state.phase!=='Active'
      || !(unit.canMove || unit.canAttack))return 1;
    // Shared phase, independent of selection: only available friendly unit badges pulse.
    return .25+.75*(1+Math.cos(time*Math.PI*2/1200))/2;
  }
  function selectionHostile(state,cell){
    const owner=state.units.find(u=>u.cell===cell)?.owner||state.cells[cell]?.owner;
    const player=state.factions.find(f=>f.id===state.playerFaction),target=state.factions.find(f=>f.id===owner);
    return !!(player&&target&&(target.relation?target.relation!=='allied':player.camp!==target.camp));
  }
  function badgeColor(state,owner){
    const player=state.factions.find(f=>f.id===state.playerFaction),target=state.factions.find(f=>f.id===owner);
    return target?.relation?({allied:'blue',neutral:'black',hostile:'red'}[target.relation]):target?.neutral?'black':target&&player&&target.camp===player.camp?'blue':'red';
  }
  function recruitDuration(speed=1){return 480/speed;}
  function flagFrame(time){return Math.floor(time/100)%4+1;}
  function flagAnimated(unit,state){
    // Original 50900 -> 4fb38: movement first, then the unspent attack/target flag.
    return unit.owner===state.playerFaction&&((unit.remainingMovement>0&&unit.morale!==-3)||
      !(unit.constructionTurns>0)&&!unit.acted&&!!unit.canAttack);
  }
  function facilityFlagAnimated(site,state){return site.buildingKind==='port'&&!!site.owner&&site.owner===state.playerFaction;}
  function healthColor(health,maximum){
    const hp=Math.max(0,Math.min(maximum,health));
    if(!(maximum>0))return '#ff0000';
    // Original 60958/60b68 use packed ABGR and truncate each integer division.
    const upper=hp*2>maximum;
    const red=upper?255-Math.trunc((hp*2-maximum)*255/maximum):255;
    const green=upper?255:255-Math.trunc((maximum-hp*2)*255/maximum);
    const blue=upper?128-(red>>1):0;
    return '#'+[red,green,blue].map(v=>v.toString(16).padStart(2,'0')).join('');
  }
  function flagPosition(p){return {x:p.x-27,y:p.y-45};}
  function moraleSprite(morale){return ({'-3':'morale_down3.png','-2':'morale_down2.png','-1':'morale_down1.png','1':'morale_up.png'})[morale]||null;}
  function objectiveMarker(site,unit){return site?.objectiveMarker||(site?.objective?1:0)||unit?.objectiveMarker||(unit?.objective?1:0);}
  function recruitOffset(elapsed,speed=1){const t=Math.max(0,Math.min(1,elapsed/recruitDuration(speed)));return t>=1?0:-60*(1-t);}
  function advanceTutorialArrow(motion,seconds){
    // Preserve native float32 rounding at the turning points.
    let offset=Math.fround(motion.offset+motion.speed*Math.fround(seconds)),speed=motion.speed;
    if(offset < -20){offset=-20;speed=-speed;}
    else if(offset > 0){offset=0;speed=-speed;}
    return {offset,speed};
  }
  function terrainRows(site) {
    if(site.geographyName)return [{icon:null,text:site.geographyName,geography:true}];
    const info=site.tileInfo;if(!info)return [];
    const rows=[];
    for(const [kind,value] of Object.entries(info.income||{}))if(value>0)rows.push({icon:'marker_'+kind+'.png',text:'+'+value});
    if(info.defense>0)rows.push({icon:'marker_dfs_all.png',text:'-'+info.defense+'%',defense:true});
    else for(const category of ['infantry','cavalry','artillery']){
      const value=info.penalties?.[category];if(value>0)rows.push({icon:'marker_dfs_'+category+'.png',text:'-'+value+'%',defense:true});
    }
    return rows;
  }
  function animationBounds(lib,item,tick=0,depth=0) {
    if(!item||depth>20)return null;
    if(item.kind){const s=lib.sprites.get(item.name+'.png');return s?{left:item.pivot[0],top:item.pivot[1],right:item.pivot[0]+Number(s.w),bottom:item.pivot[1]+Number(s.h)}:null;}
    const frame=Math.floor(tick)%item.duration,points=[];
    for(const layer of item.layers){let entry=layer[0];for(const candidate of layer){if(candidate.key<=frame)entry=candidate;else break;}
      for(const [elementId,childId] of entry.indices){const e=lib.elements[elementId],child=lib.items[childId];
        const local=e.playback_mode===2?e.start_frame:e.playback_mode===1?Math.min(e.start_frame+frame-entry.key,(child?.duration||1)-1):e.start_frame+frame-entry.key;
        const b=animationBounds(lib,child,local,depth+1);if(!b)continue;const [a,c,d,f,x,y]=e.transform;
        for(const [px,py] of [[b.left,b.top],[b.right,b.top],[b.right,b.bottom],[b.left,b.bottom]])points.push([a*px+d*py+x,c*px+f*py+y]);
      }
    }
    return points.length?{left:Math.min(...points.map(p=>p[0])),top:Math.min(...points.map(p=>p[1])),right:Math.max(...points.map(p=>p[0])),bottom:Math.max(...points.map(p=>p[1]))}:null;
  }
  class SpatialIndex {
    constructor(items,bounds){
      this.bins=new Map();this.large=[];
      items.forEach((item,order)=>{
        const b=bounds(item),entry={item,order};
        if((Math.floor(b.bottom/512)-Math.floor(b.top/512)+1)*(Math.floor(b.right/512)-Math.floor(b.left/512)+1)>256){this.large.push(entry);return;}
        for(let y=Math.floor(b.top/512);y<=Math.floor(b.bottom/512);y++)for(let x=Math.floor(b.left/512);x<=Math.floor(b.right/512);x++){
          const key=x+':'+y;if(!this.bins.has(key))this.bins.set(key,[]);this.bins.get(key).push(entry);
        }
      });
    }
    query(rect){
      const found=new Set(this.large);
      for(let y=Math.floor(rect.top/512);y<=Math.floor((rect.top+rect.height)/512);y++)for(let x=Math.floor(rect.left/512);x<=Math.floor((rect.left+rect.width)/512);x++)
        for(const entry of this.bins.get(x+':'+y)||[])found.add(entry);
      return [...found].sort((a,b)=>a.order-b.order).map(entry=>entry.item);
    }
  }
  function paddedBounds(x,y,padding){return {left:x-padding,top:y-padding,right:x+padding,bottom:y+padding};}
  class Renderer {
    constructor(api) {this.api=api;this.surface=new Ew4MapSurface.Surface();this.chunks=new Map();this.chunkPixels=0;this.cityLabels=new Map();this.mapLabels=new Map();this.healthArcs=new Map();this.unitFrames=new Map();this.unitFramePixels=0;this.unitLibraryIds=new WeakMap();this.nextLibraryId=1;this.resetMotions();}
    invalidateGround(){for(const c of this.chunks.values())this.gpuDraw?.release?.(c.canvas);this.chunks.clear();this.chunkPixels=0;this.staticKey=null;this.groundSignature=(this.groundSignature||0)+1;}
    indexMap(map){
      if(this.indexedMap===map)return;
      this.indexedMap=map;
      this.mapLabels.clear();
      this.overlayIndex=new SpatialIndex(map.overlays,o=>paddedBounds(o.x,o.y,120));
      this.tileIndex=new SpatialIndex(map.tiles,t=>paddedBounds(t.x+t.width/2,t.y+t.height/2,Math.max(t.width,t.height)));
      this.waterTileIndex=new SpatialIndex(map.detailTiles||[],t=>({left:t.x,top:t.y,right:t.x+t.width,bottom:t.y+t.height}));
      this.labelIndex=new SpatialIndex(map.labels||[],mapLabelBounds);
    }
    resetMotions(){this.unitMotions=new Map();this.motionSelection=null;}
    syncMotions(){
      for(const [id,combat] of this.combatMotions||[])if(!this.stationedHasUnit(id)||this.time>=combat.started+(combat.delay||0)+(combat.segments.at(-1)?.end||0))this.combatMotions.delete(id);
      for(const id of this.unitMotions.keys())if(!this.stationedHasUnit(id))this.unitMotions.delete(id);
      const unit=this.selection&&this.selected,eligible=unit&&unit.owner===this.state.playerFaction&&this.state.activeFaction===this.state.playerFaction&&!unit.constructionTurns&&!this.combatMotions?.has(unit.id);
      const token=eligible?unit.id+':'+this.selectedAt:null;
      if(token===this.motionSelection?.token)return;
      const previous=this.motionSelection?.id;
      if(previous&&!this.combatMotions?.has(previous))this.unitMotions.set(previous,{action:'undoready',started:this.time,playing:true});
      this.motionSelection=eligible?{id:unit.id,token}:null;
      if(eligible)this.unitMotions.set(unit.id,{action:'ready',started:this.selectedAt??this.time,playing:true});
    }
    stationedHasUnit(id){return this.unitIds.has(id);}
    motionPose(unit,profile,lib,facing,time,moving=false){
      let pose=this.unitMotions.get(unit.id)||{action:'ready',playing:false};
      if(moving||unit.constructionTurns>0){pose={action:unit.constructionTurns>0?'build':'ready',playing:false};this.unitMotions.set(unit.id,pose);}
      let native=this.api.effects?.motion(profile,pose.action,facing),item=lib.byName.get(native?.name||profile.motions[pose.action]||profile.motions.ready);
      if(pose.action==='undoready'&&!native&&!profile.motions.undoready){pose={action:'ready',playing:false};this.unitMotions.set(unit.id,pose);native=this.api.effects?.motion(profile,'ready',facing);item=lib.byName.get(native?.name||profile.motions.ready);}
      let tick=pose.playing?(time-pose.started)/1000*lib.fps*Number(native?.speed||1):0;
      if(pose.playing&&(!item||tick>=item.duration)){
        pose={action:pose.action==='ready'?'attack':'ready',playing:false};this.unitMotions.set(unit.id,pose);
        native=this.api.effects?.motion(profile,pose.action,facing);item=lib.byName.get(native?.name||profile.motions[pose.action]||profile.motions.ready);tick=0;
      }
      return {native,item,tick};
    }
    frame(scene,time) {
      Object.assign(this,scene);this.time=time;
      const arrowActive=Number.isInteger(this.tutorialCell)&&!!this.state.cells[this.tutorialCell]?.active;
      const arrowElapsed=this.tutorialArrowTime===undefined?0:Math.max(0,(time-this.tutorialArrowTime)/1000);
      this.tutorialArrowMotion=advanceTutorialArrow(this.tutorialArrowMotion||{offset:0,speed:-100},arrowElapsed);
      this.tutorialArrowTime=time;
      const mapMode=this.api.mapMode||'normal';
      if(this.mapMode!==mapMode){this.mapMode=mapMode;this.invalidateGround();}
      if(this.preparedState!==this.state){
        if(this.preparedMap!==this.map||this.preparedWidth!==this.state.width||this.preparedHeight!==this.state.height){this.invalidateGround();this.gpuGroundRect=null;if(this.surfaceMap!==this.map)this.surface=new Ew4MapSurface.Surface();this.cityLabels.clear();this.pendingCityLabels?.clear();this.mapLabels.clear();this.unitFrames.clear();this.unitFramePixels=0;}
        this.preparedWidth=this.state.width;this.preparedHeight=this.state.height;
        this.preparedMap=this.map;this.preparedState=this.state;
        this.namedSites=[];this.state.cells.forEach((site,cell)=>{if(site.active&&site.name)this.namedSites.push({cell,p:point(cell,this.state.width)});});
        this.cityLayout=null;
        this.factions=new Map(this.state.factions.map(f=>[f.id,f]));
        if(root.Ew4MapViews){this.projection??=new root.Ew4MapViews.Projection();if(this.projection.sync(this.state,this.map))this.invalidateGround();}
        this.stationed=new Map(this.state.units.map(u=>[u.cell,u]));
        this.unitById=new Map(this.state.units.map(u=>[u.id,u]));
        this.unitIds=new Set(this.unitById.keys());
        this.orderedUnits=[...this.state.units].sort((a,b)=>a.cell-b.cell);
        this.unitOrder=new Map(this.orderedUnits.map((u,i)=>[u.id,i]));
        this.unitIndex=new SpatialIndex(this.orderedUnits,u=>{const p=point(u.cell,this.state.width);return paddedBounds(p.x,p.y,120);});
        const prior=this.groundState;
        if(!prior||prior.width!==this.state.width||prior.height!==this.state.height){
          this.siteObjectives=new Set();this.state.cells.forEach((site,cell)=>{if(site.objectiveMarker||site.objective)this.siteObjectives.add(cell);});
        }else if(prior.cells!==this.state.cells){
          const changed=this.state.baseRevision===prior.revision&&this.state.changedCells?this.state.changedCells:this.state.cells.map((_,i)=>i);
          for(const cell of changed){const site=this.state.cells[cell];if(site?.objectiveMarker||site?.objective)this.siteObjectives.add(cell);else this.siteObjectives.delete(cell);}
        }
        this.objectiveCells=[...new Set([...this.siteObjectives,...this.state.units.filter(u=>u.objectiveMarker||u.objective).map(u=>u.cell)])].sort((a,b)=>a-b);
        this.indexMap(this.map);
        if(prior&&prior.cells!==this.state.cells){
          const fields=['owner','active','sea','geographyType','geographySprite','buildingKind','buildingLevel','buildingImage','buildingStyle','installationKind','facility','jurisdictionCenter','bridgeEdges','bridgeStyle0','bridgeStyle1','bridgeStyle2'];
          const candidates=this.state.baseRevision===prior.revision&&this.state.changedCells?this.state.changedCells:this.state.cells.map((_,i)=>i);
          const changed=candidates.filter(i=>fields.some(key=>this.state.cells[i]?.[key]!==prior.cells[i]?.[key]));
          if(changed.length){this.staticKey=null;this.groundSignature=(this.groundSignature||0)+1;}
          for(const cell of changed){const p=point(cell,this.state.width);for(const [key,c] of this.chunks)if(p.x>=c.x-160&&p.x<=c.x+672&&p.y>=c.y-160&&p.y<=c.y+672){this.draw.release?.(c.canvas);this.chunkPixels-=c.pixels;this.chunks.delete(key);}}
        }
        if(!this.projection&&prior&&this.state.factions.some(f=>{const p=prior.factions.find(p=>p.id===f.id);return p?.color!==f.color||p?.camp!==f.camp;}))this.invalidateGround();
        this.groundState=this.state;
        this.visibleKey=null;
      }
      this.detailScale=this.detailScale||1;
      this.badgeOnly=this.badgeOnly?this.zoom<this.detailScale*.62:this.zoom<this.detailScale*.52;
      this.markerScale=this.detailScale/this.zoom;
      if(!this.badgeOnly)this.syncMotions();
      const r=this.visibleRect,visibleKey=[r.left,r.top,r.width,r.height].join(',');
      if(this.visibleKey!==visibleKey){
        this.visibleKey=visibleKey;this.visibleCells=this.cellsInRect(r);
        this.visibleUnits=this.unitIndex.query(r).filter(unit=>this.visible(point(unit.cell,this.state.width)));
      }
      this.animated=arrowActive||!!this.selection||this.visibleUnits.some(u=>badgeAlpha(u,this.state,300)!==1||!this.badgeOnly&&(this.unitMotions.get(u.id)?.playing||u.burning>0||u.category==='fort'&&u.health>0&&u.maxHealth-u.health>=80||flagAnimated(u,this.state)&&this.factions.get(u.owner)?.flag&&!this.factions.get(u.owner)?.flagImage))||
        this.visibleCells.some(({site,cell})=>site.active&&(objectiveMarker(site,this.stationed.get(cell))||!this.badgeOnly&&facilityFlagAnimated(site,this.state)&&this.factions.get(site.owner)?.flag&&!this.factions.get(site.owner)?.flagImage&&!this.stationed.has(cell)));
      // Grid covers the ground/buildings, while units, flags and portraits stay above it.
      this.staticGround();this.grid();this.objectives();this.units();this.flags();this.unitMarkers();this.labels();this.highlights();this.playerOrders();this.tutorialArrow();this.scheduleCityLabels();
    }
    async prewarm(state,map,rect){
      const surface=new Ew4MapSurface.Surface(),factions=new Map(state.factions.map(f=>[f.id,f]));
      const rules=this.api.manifest.mapDrawing,texture=rules&&this.api.image(rules.borderTexture);
      let started=performance.now();
      const first=0,last=state.height-1;
      for(let row=first;row<=last;row++)for(let col=0;col<state.width;col++){
        const i=row*state.width+col;
        const site=state.cells[i],neighbors=Array.from({length:6},(_,d)=>{const c=state.cells[neighbor(i,d,state.width,state.height)];return c?{...c,color:factions.get(c.owner)?.color||0}:null;});
        if(!site.owner||site.sea){if(neighbors.some(n=>n?.active&&!n.sea&&n.color))surface.coast(neighbors);}
        if(rules&&site.active&&!site.sea&&site.owner){const mask=Ew4MapSurface.borderMask(site,neighbors);if(mask)surface.border(mask,rules,texture);}
        if(performance.now()-started>=4){await new Promise(resolve=>setTimeout(resolve,0));started=performance.now();}
      }
      this.surface=surface;this.surfaceMap=map;
    }
    cellsInRect(r){
      const cells=[],width=this.state.width,height=this.state.height;
      const firstRow=Math.max(0,Math.floor((r.top-120+18)/54)),lastRow=Math.min(height-1,Math.ceil((r.top+r.height+120+18)/54));
      for(let row=firstRow;row<=lastRow;row++){
        const offset=row%2*32,firstCol=Math.max(0,Math.floor((r.left-120-offset)/64)),lastCol=Math.min(width-1,Math.ceil((r.left+r.width+120-offset)/64));
        for(let col=firstCol;col<=lastCol;col++){const cell=row*width+col;cells.push({cell,p:point(cell,width),site:this.state.cells[cell]});}
      }
      return cells;
    }
    staticGround(){
      if(this.draw.gpu){
        const waterDetail=!!this.map.waterDetailZoom&&this.zoom>=this.map.waterDetailZoom;
        const key=[this.groundSignature,this.api.imageRevision||0,this.api.geographicLanguage||'en',Ew4OriginalUI.revision,this.map.terrainDetailZoom?this.zoom>=this.map.terrainDetailZoom:'',waterDetail].join(':');
        const r=this.visibleRect,rangeMode=waterDetail?'water':'standard';
        if(this.gpuGroundRangeMode!==rangeMode){this.gpuGroundRect=null;this.gpuGroundRangeMode=rangeMode;}
        const prior=this.gpuGroundRect;
        const far=Ew4Camera.constrain({x:r.left+r.width/2,y:r.top+r.height/2,zoom:waterDetail?this.zoom*.85:Math.min(this.zoom,this.detailScale*.28)},
          {w:r.width*this.zoom,h:r.height*this.zoom},cameraBounds(this.state,this.map)),ratio=this.zoom/far.zoom;
        const left=Math.min(r.left,far.x-r.width*ratio/2),top=Math.min(r.top,far.y-r.height*ratio/2);
        const right=Math.max(r.left+r.width,far.x+r.width*ratio/2),bottom=Math.max(r.top+r.height,far.y+r.height*ratio/2);
        if(!prior||left<prior.left||top<prior.top||right>prior.left+prior.width||bottom>prior.top+prior.height){
          this.gpuGroundRect={left:Math.floor(left/512)*512-512,top:Math.floor(top/512)*512-512,width:Math.ceil((right-left)/512)*512+1536,height:Math.ceil((bottom-top)/512)*512+1536};
        }
        this.draw.cachedLayer(key,this.gpuGroundRect,()=>{
          const cells=this.visibleCells;this.visibleRect=this.gpuGroundRect;this.visibleCells=this.cellsInRect(this.visibleRect);
          try{this.groundLayers();}finally{this.visibleRect=r;this.visibleCells=cells;}
        });return;
      }
      const target=this.draw,matrix=target.getTransform(),size=target.canvas;
      const key=[matrix.a,matrix.b,matrix.c,matrix.d,size.width,size.height,this.api.imageRevision||0,this.api.geographicLanguage||'en',Ew4OriginalUI.revision].join(',');
      const dx=matrix.e-this.staticX,dy=matrix.f-this.staticY;
      if(this.staticKey!==key||this.staticSignature!==this.groundSignature||this.staticMap!==this.map||
        !(dx<=0&&dy<=0&&dx+this.staticCanvas.width>=size.width&&dy+this.staticCanvas.height>=size.height)){
        const canvas=this.staticCanvas||(this.staticCanvas=document.createElement('canvas'));
        // Retain terrain beyond the viewport so camera motion can crop the existing bitmap.
        const margin=Math.max(0,Math.min(Math.ceil(Math.min(size.width,size.height)*.35),
          Math.floor((Math.sqrt((size.width-size.height)**2+4*10*1024*1024)-size.width-size.height)/4)));
        canvas.width=size.width+margin*2;canvas.height=size.height+margin*2;
        this.staticX=Math.round(matrix.e)+margin;this.staticY=Math.round(matrix.f)+margin;
        const rect=this.visibleRect,cells=this.visibleCells;
        this.visibleRect={left:-this.staticX/matrix.a,top:-this.staticY/matrix.d,width:canvas.width/matrix.a,height:canvas.height/matrix.d};
        this.visibleCells=this.cellsInRect(this.visibleRect);
        this.draw=canvas.getContext('2d');this.draw.setTransform(matrix.a,matrix.b,matrix.c,matrix.d,this.staticX,this.staticY);
        try{
          this.groundLayers();
        }finally{this.draw=target;this.visibleRect=rect;this.visibleCells=cells;}
        this.staticKey=key;this.staticSignature=this.groundSignature;this.staticMap=this.map;
        this.staticBuilds=(this.staticBuilds||0)+1;
      }
      this.staticState=this.state;
      target.save();target.resetTransform();
      target.drawImage(this.staticCanvas,this.staticX-matrix.e,this.staticY-matrix.f,size.width,size.height,0,0,size.width,size.height);target.restore();
    }
    groundLayers(){
      const r=this.visibleRect;this.draw.fillStyle='#688e91';this.draw.fillRect(r.left,r.top,r.width,r.height);
      this.ground();this.facilities(true);this.terrain(false);this.ownership();this.mapTiles();this.terrain(false,true);this.geography();this.mapText();this.borders();this.districtBorders();this.terrain(true);this.facilities(false);this.bridges();
    }
    scheduleCityLabels(){
      if(this.warmTimer||!this.pendingCityLabels?.size||!this.draw||this.api.warmingPaused||this.draw.lost||document.hidden||this.api.visible===false)return;
      const run=()=>{
        this.warmTimer=0;
        if(!this.draw||this.api.warmingPaused||this.draw.lost||document.hidden||this.api.visible===false)return;
        if(this.api.cameraActive){this.scheduleCityLabels();return;}
        if(!this.pendingCityLabels?.size)return;
        const [key,label]=this.pendingCityLabels.entries().next().value;this.pendingCityLabels.delete(key);
        if(!this.cityLabels.has(key))this.cacheCityLabel(key,label.name,label.cell,label.density);
        const canvas=this.cityLabels.get(key);
        if(canvas&&this.draw.gpu&&this.draw.renderer&&!this.draw.lost){const texture=this.draw.texture(canvas,0,0,canvas.width,canvas.height);this.draw.renderer.texture.bind(texture.baseTexture);}
        this.api.requestFrame?.();this.scheduleCityLabels();
      };
      this.warmTimer=setTimeout(run,32);
    }
    bridges() {
      for(const {site,p} of this.visibleCells){
        if(!site.active)continue;
        // Native 506c4 draws only the first three edges at the cell's atlas anchor.
        for(let style=0;style<3;style++){
          const edges=site['bridgeStyle'+style]&site.bridgeEdges&7;
          for(let direction=0;direction<3;direction++)if(edges&(1<<direction))
            this.api.sprite('buildings',`bridge_${style+1}_${direction+1}.png`,p.x,p.y,1,this.draw);
        }
      }
    }
    ground() {
      const {draw,visibleRect:r}=this, pattern=this.api.image('images/map_pt@2x.png');
      if(draw.drawPattern&&pattern.complete&&pattern.naturalWidth){draw.drawPattern(pattern,.5,r);return;}
      if(pattern.complete&&pattern.naturalWidth){const fill=draw.createPattern(pattern,'repeat');fill.setTransform(new DOMMatrix().scale(.5));draw.fillStyle=fill;draw.fillRect(r.left,r.top,r.width,r.height);}
    }
    ownership(preview=false) {
      const {draw}=this;
      // Land in the original texture is transparent. Ownership belongs BELOW coast/river tiles.
      for(const {site:c,cell:i,p} of this.visibleCells){
        if(c.owner&&!c.sea){const color=Ew4MapSurface.rgba(this.projection?.color(c,this.mapMode)??this.factions.get(c.owner)?.color??0);
          hex(draw,p.x,p.y);draw.fillStyle=`rgba(${color[0]},${color[1]},${color[2]},${color[3]/255})`;draw.fill();}
        else if(!preview){const neighbors=this.neighbors(i);if(neighbors.some(n=>n?.active&&!n.sea&&n.color))draw.drawImage(this.surface.coast(neighbors),p.x-32,p.y-36,64,72);}
      }
    }
    neighbors(index){return Array.from({length:6},(_,d)=>{const cell=this.state.cells[neighbor(index,d,this.state.width,this.state.height)];
      return cell?{...cell,owner:this.projection?.borderOwner(cell,this.mapMode)??cell.owner,color:this.projection?.color(cell,this.mapMode)??this.factions.get(cell.owner)?.color??0}:null;});}
    mapTiles() {
      if(this.map.waterDetailZoom&&this.zoom>=this.map.waterDetailZoom){
        for(const tile of this.waterTileIndex.query(this.visibleRect)){
          const r=this.visibleRect;if(tile.x>r.left+r.width||tile.y>r.top+r.height||tile.x+tile.width<r.left||tile.y+tile.height<r.top)continue;
          const image=this.api.image(tile.file);
          if(image.complete&&image.naturalWidth){this.draw.drawImage(image,tile.x,tile.y,tile.width,tile.height);continue;}
          // Replace each loaded patch, including its transparent land, to avoid coarse river ghosts.
          for(const low of this.tileIndex.query({left:tile.x,top:tile.y,width:tile.width,height:tile.height})){
            const x=Math.max(tile.x,low.x),y=Math.max(tile.y,low.y),right=Math.min(tile.x+tile.width,low.x+low.width),bottom=Math.min(tile.y+tile.height,low.y+low.height);
            if(x>=right||y>=bottom)continue;
            const fallback=this.api.image(low.file);if(!fallback.complete||!fallback.naturalWidth)continue;
            const sx=fallback.naturalWidth/low.width,sy=fallback.naturalHeight/low.height;
            const sourceX=(x-low.x)*sx,sourceY=(y-low.y)*sy;
            // Pixi rejects even a subpixel rounding excess at the source texture edge.
            const sourceWidth=Math.min((right-x)*sx,fallback.naturalWidth-sourceX-1e-6),sourceHeight=Math.min((bottom-y)*sy,fallback.naturalHeight-sourceY-1e-6);
            if(sourceWidth>0&&sourceHeight>0)this.draw.drawImage(fallback,sourceX,sourceY,sourceWidth,sourceHeight,x,y,right-x,bottom-y);
          }
        }
        return;
      }
      for(const tile of this.tileIndex.query(this.visibleRect)){if(!this.visible({x:tile.x+tile.width/2,y:tile.y+tile.height/2},Math.max(tile.width,tile.height)))continue;
        const image=this.api.image(tile.file);if(image.complete&&image.naturalWidth)this.draw.drawImage(image,tile.x,tile.y,tile.width,tile.height);
      }
    }
    borders() {
      const {draw,state}=this,rules=this.api.manifest.mapDrawing;if(!rules)return;
      const texture=this.api.image(rules.borderTexture);
      for(const {site:c,cell:i,p} of this.visibleCells){if(!c.active||c.sea||!c.owner)continue;
        const display={...c,owner:this.projection?.borderOwner(c,this.mapMode)??c.owner};
        const mask=Ew4MapSurface.borderMask(display,this.neighbors(i));if(!mask)continue;
        const bitmap=this.surface.border(mask,rules,texture);if(bitmap)draw.drawImage(bitmap,p.x-32,p.y-36,64,72);
      }
    }
    districtBorders(){
      if(this.mapMode!=='district')return;
      this.districtCanvases??=new Map();
      const edges=[[1,2],[2,3],[3,4],[4,5],[5,0],[0,1]];
      for(const {site,cell,p} of this.visibleCells){
        if(!site.active||site.sea||!Number.isInteger(site.jurisdictionCenter))continue;
        let mask=0;
        for(let d=0;d<6;d++){
          const other=neighbor(cell,d,this.state.width,this.state.height),next=this.state.cells[other];
          if(next?.active&&!next.sea&&next.owner===site.owner&&next.jurisdictionCenter!==site.jurisdictionCenter&&(!Number.isInteger(next.jurisdictionCenter)||cell<other))mask|=1<<d;
        }
        if(!mask)continue;
        if(!this.districtCanvases.has(mask)){
          const canvas=document.createElement('canvas');canvas.width=144;canvas.height=160;
          const draw=canvas.getContext('2d');draw.scale(2,2);draw.translate(36,40);draw.beginPath();
          edges.forEach(([a,b],d)=>{if(mask&(1<<d)){draw.moveTo(...corners[a]);draw.lineTo(...corners[b]);}});
          draw.strokeStyle='rgba(60,72,70,.65)';draw.lineWidth=2.6;draw.lineCap='round';draw.lineJoin='round';draw.setLineDash([5,5]);draw.stroke();
          this.districtCanvases.set(mask,canvas);
        }
        this.draw.drawImage(this.districtCanvases.get(mask),p.x-36,p.y-40,72,80);
      }
    }
    terrain(seaForeground,reliefForeground=false) {
      // Relief mountains share the snow ridge's natural sprite alpha above rivers.
      for(const overlay of this.overlayIndex.query(this.visibleRect)){
        if(!!overlay.seaForeground!==seaForeground||!!(overlay.relief&&overlay.relief!=='hill'&&!overlay.seaForeground)!==reliefForeground||!this.state.cells[overlay.cell]?.active||this.state.cells[overlay.cell]?.geographySprite||!this.visible(overlay))continue;
        if(overlay.overview===false&&this.zoom<(this.map.terrainDetailZoom??0))continue;
        const scale=this.zoom<(this.map.terrainDetailZoom??0)?(overlay.overviewScale??1):1;
        this.draw.save();this.draw.translate(overlay.x,overlay.y);
        this.draw.scale((overlay.scaleX??1)*scale,(overlay.scaleY??1)*scale);
        this.draw.globalAlpha=overlay.opacity??1;
        this.api.sprite('tiles',overlay.image,0,0,1,this.draw);this.draw.restore();
      }
    }
    geography(){
      for(const {site,p,cell} of this.visibleCells)if(site.active&&site.geographySprite){
        let sx=1,sy=1,dx=0,dy=0;
        if(site.geographyType==='mountain_pass'&&/^mountain_[1-6]\.png$/.test(site.geographySprite))sy=.85;
        else if(site.geographyType==='impassable_mountain'&&site.geographySprite.startsWith('impassable_mountain_')){
          for(let d=0;d<6;d++){
            const other=neighbor(cell,d,this.state.width,this.state.height);
            if(this.state.cells[other]?.geographyType!=='mountain_pass')continue;
            const q=point(other,this.state.width);dx+=p.x-q.x;dy+=p.y-q.y;sx=.9;
          }
          const length=Math.hypot(dx,dy);if(length){dx=dx/length*6;dy=dy/length*6;}
        }
        this.draw.save();this.draw.translate(p.x+dx,p.y+dy);this.draw.scale(sx,sy);
        this.api.sprite('tiles',site.geographySprite,0,0,1,this.draw);this.draw.restore();
      }
    }
    mapText() {
      const lib=this.api.libraries.get('maptext');if(!lib)return;
      if(this.mapLabelSource!==lib.texture||this.mapFontRevision!==Ew4OriginalUI.revision){this.mapLabelSource=lib.texture;this.mapFontRevision=Ew4OriginalUI.revision;this.mapLabels.clear();}
      const density=1;
      for(const label of this.labelIndex.query(this.visibleRect)){const p={x:Number(label.tx),y:Number(label.ty)};if(!label.textPath&&!this.visible(p,Math.max(Number(label.width),Number(label.height))))continue;
        const chinese=this.api.geographicLanguage==='zh'&&label.textZh;
        const opacity=Number.isFinite(label.opacity)?Math.max(0,Math.min(1,label.opacity)):60/255;
        const key=label.libName+':'+density+':'+opacity+':'+(chinese?'zh':'en');
        if(!this.mapLabels.has(key)){
          const item=lib.byName.get(label.libName),path=textPath(label.textPath);let bounds=animationBounds(lib,item);if(!bounds&&!path)continue;
          if(path){const b=mapLabelBounds(label);bounds={left:b.left-p.x,top:b.top-p.y,right:b.right-p.x,bottom:b.bottom-p.y};}
          else if(chinese){
            const angle=Number(label.angle||0)*Math.PI/180,w=Number(label.letteringLength)/2+Number(label.size)*.2+5,h=Number(label.size)*.7+5;
            const x=Math.abs(Math.cos(angle))*w+Math.abs(Math.sin(angle))*h,y=Math.abs(Math.sin(angle))*w+Math.abs(Math.cos(angle))*h;
            bounds={left:-x,top:-y,right:x,bottom:y};
          }
          const ratio=path?1:lib.pixelRatio||1,left=Math.floor(bounds.left/ratio)-3,top=Math.floor(bounds.top/ratio)-3;
          const width=Math.ceil(bounds.right/ratio)-left+3,height=Math.ceil(bounds.bottom/ratio)-top+3;
          const canvas=document.createElement('canvas');canvas.width=width*density;canvas.height=height*density;
          const draw=canvas.getContext('2d');draw.scale(density,density);draw.translate(-left,-top);draw.globalAlpha=opacity;
          if(chinese){
            void Ew4OriginalUI.prepareMapText(label.textZh,!label.sea);
            const text=[...Ew4OriginalUI.availableText(label.textZh)],positions=label.characterPositionsZh;
            const positioned=Array.isArray(positions)&&positions.length===text.length&&positions.every(p=>[p.x,p.y,p.angle].every(Number.isFinite));
            const height=Number(positioned?label.characterSizeZh||label.size:label.size),length=path?path.length:Number(label.letteringLength);
            if(!path)draw.rotate(Number(label.angle||0)*Math.PI/180);
            draw.font=(label.sea?'italic ':'')+height+'px '+(label.sea?'':'"EW4 Serif",')+Ew4OriginalUI.mapFontFamily;
            draw.fontKerning='none';draw.textBaseline='alphabetic';
            const widths=text.map(char=>draw.measureText(char).width),natural=widths.reduce((a,b)=>a+b,0);
            const scale=Math.min(1,length/natural),spacing=text.length>1?Math.max(0,(length-natural*scale)/(text.length-1)):0;
            if(!path)draw.scale(scale,1);draw.fillStyle=label.sea?'#ffffff':'#303333';
            let x=path?0:-Math.max(length,natural*scale)/2/scale;
            const metrics=draw.measureText(text.join('')),baseline=(metrics.actualBoundingBoxAscent-metrics.actualBoundingBoxDescent)/2;
            for(let i=0;i<text.length;i++){
              if(positioned){const p=positions[i];draw.save();draw.translate(p.x,p.y);draw.rotate(p.angle*Math.PI/180);draw.fillText(text[i],-widths[i]/2,baseline);draw.restore();}
              else if(path){const p=path.at(x+widths[i]*scale/2);draw.save();draw.translate(p.x,p.y);draw.rotate(p.angle);draw.scale(scale,1);draw.fillText(text[i],-widths[i]/2,baseline);draw.restore();x+=widths[i]*scale+spacing;}
              else{draw.fillText(text[i],x,baseline);x+=widths[i]+spacing/scale;}
            }
          }else if(path){
            for(const glyph of pathLettering(lib,label,path)){
              draw.save();draw.translate(glyph.x,glyph.y);draw.rotate(glyph.angle);
              draw.translate(-glyph.width/2,Number(label.size)/2-glyph.height);draw.scale(glyph.scale,glyph.scale);
              draw.translate(-glyph.item.pivot[0],-glyph.item.pivot[1]);
              draw.scale(lib.pixelRatio||1,lib.pixelRatio||1);this.api.animation(lib,glyph.item,0,0,draw);draw.restore();
            }
          }else this.api.animation(lib,item,0,0,draw);
          // Bound zoom-level caches while retaining labels at their actual render density.
          if(this.mapLabels.size>=96)this.mapLabels.delete(this.mapLabels.keys().next().value);
          this.mapLabels.set(key,{canvas,left,top,width,height});
        }
        const cached=this.mapLabels.get(key);this.draw.drawImage(cached.canvas,p.x+cached.left,p.y+cached.top,cached.width,cached.height);
      }
    }
    facilities(farms) {
      for(const {site:c,p} of this.visibleCells){
        if(!c.active||(c.buildingKind==='farmland')!==farms)continue;
        if(c.buildingKind){
          const level=Math.max(1,c.buildingLevel),style=c.buildingStyle??1,region=style===1?'west':'east';
          const art=({city:`city_${region}_lv${level}.png`,industry:`factory_${region}_lv${level}.png`,stable:`stable_${region}_lv${level}.png`,port:`port_${style}.png`,farmland:`farm_${style}_lv${level}.png`})[c.buildingKind];
          this.draw.save();this.draw.globalAlpha=.85;this.api.sprite('buildings',art,p.x,p.y,1,this.draw);
          if(c.buildingImage?.startsWith('capital_')){this.api.sprite('buildings',c.buildingImage,p.x,p.y,1,this.draw);this.api.sprite('buildings',`city_${region}_lv${level}_up.png`,p.x,p.y,1,this.draw);}
          this.draw.restore();
          if(!farms){const levels=this.api.manifest.buildings[c.buildingKind];
            if(levels&&level>=levels.length)this.api.sprite('buildings','buildinglevel_max.png',p.x,p.y+25,1,this.draw);
            this.api.sprite('buildings','buildinglevel_'+level+'.png',p.x,p.y+25,1,this.draw);
          }
          if(!farms&&c.facility)this.api.sprite('buildings','marker_'+c.facility+'.png',p.x+17,p.y+25,1,this.draw);
        }
        if(!farms&&c.installationKind)this.api.sprite('buildings',({trench:'defense_moat.png',fence:'defense_fences.png',bunker:'defense_bunker.png'})[c.installationKind],p.x,p.y,1,this.draw);
      }
    }
    objectives(){
      const lib=this.api.libraries.get('anim_smoke');if(!lib)return;
      if(this.objectiveSource!==lib.texture){this.objectiveSource=lib.texture;this.objectiveLibraries=new Map();}
      for(const cell of this.objectiveCells){
        const site=this.state.cells[cell],unit=this.stationed.get(cell),marker=objectiveMarker(site,unit);
        if(!site.active||!(marker===1||marker===2))continue;
        const motion=!site.objectiveMarker&&!site.objective&&unit?this.motions.get(unit.id):null;
        const p=motion?pose(motion,this.state.width,this.time):point(cell,this.state.width);
        if(!this.visible(p))continue;
        if(!this.objectiveLibraries.has(marker)){
          const texture=document.createElement('canvas');texture.width=lib.texture.naturalWidth;texture.height=lib.texture.naturalHeight;
          const draw=texture.getContext('2d');draw.drawImage(lib.texture,0,0);draw.globalCompositeOperation='multiply';draw.fillStyle=marker===1?'#ff0000':'#ffff00';draw.fillRect(0,0,texture.width,texture.height);draw.globalCompositeOperation='destination-in';draw.drawImage(lib.texture,0,0);
          this.objectiveLibraries.set(marker,{...lib,texture});
        }
        const tinted=this.objectiveLibraries.get(marker);this.draw.save();this.draw.translate(p.x,p.y);this.draw.scale(.5,.5);
        this.api.animation(tinted,tinted.byName.get('circle'),this.time/1000*tinted.fps,0,this.draw);this.draw.restore();
      }
    }
    flag(flag,p,animated=true,imagePath=null){
      if(!flag&&!imagePath)return;
      const anchor=flagPosition(p);this.draw.save();this.draw.globalAlpha*=.5;this.api.sprite('buildings','flagpole.png',anchor.x,anchor.y,1,this.draw);this.draw.restore();
      if(imagePath){
        const image=this.api.image('extensions/'+imagePath.slice('rewrite/'.length));
        if(image.complete&&image.naturalWidth)this.draw.drawImage(image,anchor.x,anchor.y,26,15);
        return;
      }
      this.api.sprite('buildings',flag+(animated?flagFrame(this.time):1)+'.png',anchor.x,anchor.y,1,this.draw);
    }
    flags(){
      if(this.badgeOnly)return;
      for(const {site,cell,p} of this.visibleCells){
        if(!site.active||!site.owner||!site.buildingKind||site.buildingKind==='farmland'||this.stationed.has(cell))continue;
        const faction=this.factions.get(site.owner);this.flag(faction?.flag,p,facilityFlagAnimated(site,this.state),faction?.flagImage);
      }
    }
    units() {
      const {draw,state,time}=this;
      this.visibleUnitMarkers=[];
      // Native movement emitters render behind the unit bodies, using HD-to-map scale.
      const activeTrails=new Set();
      if(!this.badgeOnly)for(const [id,motion] of this.motions){const unit=this.unitById.get(id);if(!unit)continue;
        const p=pose(motion,state.width,time),profile=this.api.effects?.profile(unit.profile,this.factions.get(unit.owner))||this.api.manifest.profiles[unit.profile];
        if(p.moving){activeTrails.add(id);this.api.effects?.trail(unit.id,p.x,p.y,Number(profile?.grade||0),state.cells[p.cell??unit.cell]?.sea,time,this.visible(p),this.arrivalSpeed||1);}
      }
      this.api.effects?.finishTrails?.(activeTrails,time,this.badgeOnly);
      this.api.effects?.frame(draw,time,'ground',this.visibleRect);
      const activeUnits=new Set(this.visibleUnits);
      for(const id of this.motions.keys()){const unit=this.unitById.get(id);if(unit)activeUnits.add(unit);}
      const units=this.motions.size?[...activeUnits].sort((a,b)=>this.unitOrder.get(a.id)-this.unitOrder.get(b.id)):this.visibleUnits;
      for(const unit of units){
        const motion=this.motions.get(unit.id),p=motion?pose(motion,state.width,time):point(unit.cell,state.width);
        if(!this.visible(p))continue;
        const profile=(!this.badgeOnly&&this.api.effects?.profile(unit.profile,this.factions.get(unit.owner)))||this.api.manifest.profiles[unit.profile];if(!profile)continue;
        draw.save();
        const death=this.deaths?.get(unit.id),deathProgress=death?Math.max(0,Math.min(1,(time-death.started)/death.duration)):0;
        if(deathProgress>=1){draw.restore();continue;}
        // Far view exits before body libraries, poses, flames, damage effects or flags are evaluated.
        if(this.badgeOnly){
          draw.translate(p.x,p.y);draw.scale(this.markerScale,this.markerScale);
          this.unitBadge(unit,{x:0,y:0},profile,!!death);
          if(!death)this.visibleUnitMarkers.push({unit,p});
          draw.restore();continue;
        }
        const lib=this.api.libraries.get(profile.library);
        if(!lib)this.requestLibrary(profile.library);
        const facing=motion?p.facing:this.facings.get(unit.id)??(unit.facing===0?-1:1),sea=state.cells[p.cell??unit.cell]?.sea;
        draw.save();if(death)draw.globalAlpha*=1-deathProgress;
        const arrival=this.arrivals.get(unit.id);if(arrival!=null)draw.translate(0,recruitOffset(time-arrival,this.arrivalSpeed||1));
        if(!this.badgeOnly&&sea&&unit.category!=='naval')this.api.sprite('buildings',facing>0?'transportship1.png':'transportship2.png',p.x,p.y);
        else if(!this.badgeOnly&&lib){
          const combat=this.combatMotions?.get(unit.id),elapsed=combat?time-combat.started-(combat.delay||0):0;
          const segment=elapsed>=0?(combat?.segments.find(s=>elapsed<s.end)||combat?.segments.at(-1)):null;
          if(combat)this.unitMotions.set(unit.id,{action:'ready',playing:false});
          const idle=this.motionPose(unit,profile,lib,facing,time,motion&&p.moving),native=idle.native,item=segment?lib.byName.get(segment.name):idle.item;
          const tick=segment?Math.min((elapsed-segment.start)*segment.speed/1000*lib.fps,Math.max(0,(item?.duration||1)-1)):idle.tick;
          const directional=segment?segment.directional:native?.dir==='left'||native?.dir==='right';
          draw.save();draw.translate(p.x+facing*profile.x,p.y+profile.y);draw.scale((directional?1:facing)*profile.scale*.5,.5);
          this.unitAnimation(lib,item,tick);draw.restore();
        }
        draw.restore();
        this.unitBadge(unit,{x:p.x-13,y:p.y+15},profile,!!death);
        if(unit.transport&&unit.category!=='naval'&&unit.category!=='fort')this.api.sprite('buildings','ship_mark.png',p.x+19,p.y+17);
        if(unit.burning>0){const fire=this.api.libraries.get('anim_fire'),item=fire?.byName.get(['1','3','2'][unit.burning-1]);if(item){draw.save();draw.globalAlpha=.5;draw.translate(p.x,p.y);this.api.animation(fire,item,time/1000*fire.fps);draw.restore();}}
        if(!death&&unit.category==='fort'&&unit.health>0){const damaged=Math.max(0,Math.min(3,Math.floor((unit.maxHealth-unit.health)/80)));if(damaged)this.api.effects?.element?.('damaged '+damaged,p.x,p.y,time,draw);}
        const rank=Number(unit.veterancy);if(rank>0)this.api.sprite('buildings','rank_'+rank+'.png',p.x+8,p.y-8);
        if(!death)this.visibleUnitMarkers.push({unit,p});
        draw.restore();
      }
    }
    requestLibrary(name){
      if(!this.api.loadLibrary)return;
      this.libraryRequests??=new Set();this.failedLibraries??=new Set();
      if(this.libraryRequests.has(name)||this.failedLibraries.has(name))return;
      this.libraryRequests.add(name);
      this.api.loadLibrary(name).then(()=>{this.libraryRequests.delete(name);this.api.requestFrame?.();},()=>{this.libraryRequests.delete(name);this.failedLibraries.add(name);});
    }
    unitBadge(unit,p,profile,death){
      const {draw,state,time}=this,color=badgeColor(state,unit.owner);
      draw.save();draw.globalAlpha*=death?1:badgeAlpha(unit,state,time);
      this.api.sprite('buildings','hpbar_'+color+'.png',p.x,p.y);
      const arc=!death&&this.healthArc(unit.health,unit.maxHealth);
      if(arc)draw.drawImage(arc,p.x-12,p.y-12,24,24);
      this.api.sprite('buildings','mark_unit_'+profile.unitIndex+'.png',p.x,p.y);
      const camp=!this.badgeOnly&&this.campMark(unit.owner);
      if(camp){const height=10,width=Math.min(12,camp.width/camp.height*height);draw.drawImage(camp,p.x+9-width,p.y,width,height);}
      if(unit.aiControlled&&!death){
        const config=this.state.aiPresentation||{},badge=config.badgeImage?this.api.image(config.badgeImage.replace(/^client\//,'')):this.aiBadge();
        if(badge instanceof HTMLCanvasElement||badge.complete&&badge.naturalWidth)draw.drawImage(badge,p.x+(config.badgeX??-10),p.y+(config.badgeY??-27),config.badgeWidth||20,config.badgeHeight||12);
      }
      draw.restore();
    }
    aiBadge(){
      const p=this.state.aiPresentation||{},key=JSON.stringify([p.badgeText,p.badgeColor,p.badgeTextColor,p.badgeBorderColor]);
      if(!this.aiBadgeCanvas||this.aiBadgeKey!==key){this.draw.release?.(this.aiBadgeCanvas);const canvas=document.createElement('canvas');canvas.width=80;canvas.height=48;const draw=canvas.getContext('2d');
        draw.fillStyle=p.badgeColor||'#343e38';draw.fillRect(0,0,80,48);draw.strokeStyle=p.badgeBorderColor||'#d8d0a2';draw.lineWidth=4;draw.strokeRect(2,2,76,44);
        const text=p.badgeText||'AI';draw.font='bold 36px sans-serif';const size=Math.min(36,36*72/Math.max(1,draw.measureText(text).width));draw.font='bold '+size+'px sans-serif';
        draw.textAlign='center';draw.textBaseline='middle';draw.fillStyle=p.badgeTextColor||'#fff4c5';draw.fillText(text,40,25);this.aiBadgeCanvas=canvas;this.aiBadgeKey=key;}
      return this.aiBadgeCanvas;
    }
    playerOrders(){
      const overlay=this.aiOverlay,config=this.state.aiPresentation||{};
      if(overlay?.tasks){for(const task of overlay.tasks)this.playerTask(task,config);return;}
      this.playerTask({units:overlay?.units||[],goals:overlay?(overlay.goals||[overlay.goal]):this.selected?.aiControlled?(this.selected.aiGoals||[this.selected.aiGoal]):[],current:true},config);
    }
    playerTask(task,config){
      const draw=this.draw,selected=new Set(task.units||[]),color=task.color||config.selectionColor||'#ffe995';
      let targets=task.goals||[];
      targets=targets.filter(target=>Number.isInteger(target)&&this.state.cells[target]?.active);
      draw.save();
      draw.globalAlpha=task.current===false?.32:1;
      const distance=(a,b)=>{const r=Math.floor(a/this.state.width),s=Math.floor(b/this.state.width),q=a%this.state.width-(r-r%2)/2,t=b%this.state.width-(s-s%2)/2;return Math.max(Math.abs(q-t),Math.abs(r-s),Math.abs(q-t+r-s));};
      const line=[];for(const end of task.line||[]){if(!line.length)line.push(end);while(line[line.length-1]!==end&&line.length<513){const origin=line[line.length-1];const next=Array.from({length:6},(_,d)=>neighbor(origin,d,this.state.width,this.state.height)).filter(c=>c>=0).sort((a,b)=>distance(a,end)-distance(b,end)||a-b)[0];line.push(next);}}
      const centers=task.kind==='defend'?targets.slice(0,1):line;
      for(const {cell,p,site} of this.visibleCells){if(!site.active||!centers.length)continue;const d=Math.min(...centers.map(c=>distance(cell,c)));if(d>(task.radius??1))continue;hex(draw,p.x,p.y);draw.fillStyle=color+(d===0?'44':'18');draw.fill();if(d===0){draw.strokeStyle=color;draw.lineWidth=2/this.zoom;draw.stroke();}}
      for(const unit of this.visibleUnits)if(selected.has(unit.id)){const p=point(unit.cell,this.state.width);hex(draw,p.x,p.y);draw.fillStyle=color+'33';draw.fill();draw.strokeStyle=color;draw.lineWidth=2/this.zoom;draw.stroke();if(task.taskId)draw.drawImage(this.aiGoalNumber(task.taskId,{badgeColor:color,badgeTextColor:'#18231f'}),p.x-29,p.y-32,15/this.zoom,15/this.zoom);}
      for(const cell of Object.values(task.stations||{})){const p=point(cell,this.state.width);if(!this.visible(p))continue;draw.strokeStyle=color;draw.lineWidth=2/this.zoom;draw.beginPath();draw.moveTo(p.x-9,p.y-9);draw.lineTo(p.x+9,p.y-9);draw.lineTo(p.x+9,p.y+9);draw.lineTo(p.x-9,p.y+9);draw.closePath();draw.stroke();}
      for(let index=0;index<targets.length;index++){
        const target=targets[index];
        const end=point(target,this.state.width),units=task.units?.length?this.state.units.filter(u=>selected.has(u.id)):this.selected?[this.selected]:[];
        draw.strokeStyle=task.color||config.lineColor||'#ffdb65';draw.lineWidth=(config.lineWidth||2)/this.zoom;
        hex(draw,end.x,end.y);draw.stroke();
        // Draw separate segments: the Pixi adapter does not implement Canvas line dashes.
        const dash=(config.dashLength||8)/this.zoom;
        const starts=index?[point(targets[index-1],this.state.width)]:units.map(unit=>point(unit.cell,this.state.width));
        for(const start of starts){const dx=end.x-start.x,dy=end.y-start.y,length=Math.hypot(dx,dy);if(length<1)continue;
          draw.beginPath();for(let d=0;d<length;d+=dash*2){const until=Math.min(length,d+dash);draw.moveTo(start.x+dx*d/length,start.y+dy*d/length);draw.lineTo(start.x+dx*until/length,start.y+dy*until/length);}draw.stroke();}
        const offset=Math.sin(this.time/220)*5;
        this.api.sprite('buildings','arrow_shadow.png',end.x,end.y+5,.7,draw);
        this.api.sprite('buildings',config.arrowImage||'arrow_red.png',end.x,end.y+offset,1,draw);
        if(targets.length>1||task.taskId)draw.drawImage(this.aiGoalNumber(targets.length>1?index+1:task.taskId,{...config,badgeColor:task.color||config.badgeColor}),end.x+10,end.y-28,18,18);
        this.animated=true;
      }
      draw.restore();
    }
    aiGoalNumber(number,config){
      this.aiGoalNumbers??=new Map();
      const key=JSON.stringify([number,config.badgeColor,config.badgeTextColor]);
      if(!this.aiGoalNumbers.has(key)){
        const canvas=document.createElement('canvas');canvas.width=48;canvas.height=48;const draw=canvas.getContext('2d');
        draw.fillStyle=config.badgeColor||'#343e38';draw.fillRect(0,0,48,48);draw.fillStyle=config.badgeTextColor||'#fff4c5';draw.font='bold 32px sans-serif';draw.textAlign='center';draw.textBaseline='middle';draw.fillText(String(number),24,25);
        this.aiGoalNumbers.set(key,canvas);
      }
      return this.aiGoalNumbers.get(key);
    }
    campMark(owner){
      const text=this.projection?.abbreviation(owner);if(!text)return null;
      this.campMarks??=new Map();
      if(this.campMarkRevision!==Ew4OriginalUI.revision){for(const marker of this.campMarks.values())this.draw.release?.(marker);this.campMarks.clear();this.campMarkRevision=Ew4OriginalUI.revision;}
      if(!this.campMarks.has(text)){
        const canvas=document.createElement('canvas'),draw=canvas.getContext('2d'),font='bold 11px '+Ew4OriginalUI.fontFamily;
        draw.font=font;canvas.width=Math.ceil(draw.measureText(text).width+5)*3;canvas.height=45;
        draw.scale(3,3);draw.font=font;draw.textBaseline='alphabetic';draw.lineJoin='round';
        draw.strokeStyle='#273239';draw.lineWidth=1.6;draw.strokeText(text,2,12);
        const silver=draw.createLinearGradient(0,2,0,14);
        for(const [offset,color] of [[0,'#fcfcf4'],[.24,'#d9dfdf'],[.45,'#89989f'],[.52,'#eff4f4'],[1,'#9ca6aa']])silver.addColorStop(offset,color);
        draw.fillStyle=silver;draw.fillText(text,2,12);
        this.campMarks.set(text,canvas);
      }
      return this.campMarks.get(text);
    }
    healthArc(health,maximum){
      if(!(health>0&&maximum>0))return null;
      const hp=Math.min(health,maximum),key=hp+':'+maximum;
      if(!this.healthArcs.has(key)){
        // Supersampled transparent edges keep GPU line aliasing out of the small health ring.
        const canvas=document.createElement('canvas');canvas.width=canvas.height=96;
        const draw=canvas.getContext('2d');draw.scale(4,4);draw.beginPath();
        draw.arc(12,12,9,Math.PI,Math.PI+Math.PI*hp/maximum);draw.strokeStyle=healthColor(hp,maximum);draw.lineWidth=2.5;draw.stroke();
        if(this.healthArcs.size>=256){const oldest=this.healthArcs.keys().next().value;this.draw.release?.(this.healthArcs.get(oldest));this.healthArcs.delete(oldest);}
        this.healthArcs.set(key,canvas);
      }
      return this.healthArcs.get(key);
    }
    unitMarkers(){
      // Draw every flag after all bodies so nearby ships cannot cover them.
      const {state}=this;
      if(this.badgeOnly){
        for(const {unit,p} of this.visibleUnitMarkers||[]){
          this.draw.save();this.draw.translate(p.x,p.y);this.draw.scale(this.markerScale,this.markerScale);
          if(unit.commanderImage){
            this.api.sprite('commandermark','board_smallgenerals.png',0,-14);
            this.api.sprite('commandermark',unit.commanderImage+'.png',0,-38);
          }
          this.draw.restore();
        }
        return;
      }
      for(const {unit,p} of this.visibleUnitMarkers||[]){
        // Native 5335c -> 60b50 draws the effective state, including after reload.
        const morale=moraleSprite(unit.morale);
        if(morale)this.api.sprite('buildings',morale,p.x+15,p.y-25);
      }
      for(const {unit,p} of this.visibleUnitMarkers||[]){
        const faction=this.factions.get(unit.owner);this.flag(faction?.flag,p,flagAnimated(unit,state),faction?.flagImage);
      }
      for(const {unit,p} of this.visibleUnitMarkers||[]){
        if(unit.commanderImage){
          // Portrait and pointer use different atlas anchors; the portrait sits inside
          // the white board, with the pointer tip on the unit (native board refy=74).
          this.api.sprite('commandermark','board_smallgenerals.png',p.x,p.y-8);
          this.api.sprite('commandermark',unit.commanderImage+'.png',p.x,p.y-32);
        }
      }
    }
    unitAnimation(lib,item,tick){
      if(!item)return;
      if(!this.unitLibraryIds.has(lib))this.unitLibraryIds.set(lib,this.nextLibraryId++);
      const frame=Math.floor(tick)%item.duration,key=this.unitLibraryIds.get(lib)+':'+item.name+':'+frame;
      let cached=this.unitFrames.get(key);
      if(this.draw.gpu){
        if(!cached?.pose){const pose=new Ew4Pixi.Pose();this.api.animation(lib,item,tick,0,pose);cached={lib,pose,pixels:0};this.unitFrames.set(key,cached);while(this.unitFrames.size>1024)this.unitFrames.delete(this.unitFrames.keys().next().value);}
        cached.pose.replay(this.draw);return;
      }
      if(cached?.lib!==lib){
        const bounds=animationBounds(lib,item,tick);if(!bounds)return;
        const ratio=lib.pixelRatio||1,density=2,left=Math.floor(bounds.left/ratio)-1,top=Math.floor(bounds.top/ratio)-1;
        const width=Math.ceil(bounds.right/ratio)-left+1,height=Math.ceil(bounds.bottom/ratio)-top+1;
        const canvas=document.createElement('canvas');canvas.width=width*density;canvas.height=height*density;
        if(canvas.width>2048||canvas.height>2048){this.api.animation(lib,item,tick);return;}
        const draw=canvas.getContext('2d');draw.scale(density,density);draw.translate(-left,-top);this.api.animation(lib,item,tick,0,draw);
        const pixels=canvas.width*canvas.height;
        while(this.unitFramePixels+pixels>8*1024*1024&&this.unitFrames.size){const oldest=this.unitFrames.keys().next().value;this.unitFramePixels-=this.unitFrames.get(oldest).pixels;this.unitFrames.delete(oldest);}
        cached={lib,canvas,left,top,width,height,pixels};this.unitFrames.set(key,cached);this.unitFramePixels+=pixels;
      }
      this.draw.drawImage(cached.canvas,cached.left,cached.top,cached.width,cached.height);
    }
    cityLabelLayout() {
      const zoom=Math.pow(1.1,Math.floor(Math.log(this.zoom)/Math.log(1.1))),scale=Math.max(1,this.detailScale/zoom);
      const metricsKey=[this.api.cityLanguage||'en',Ew4OriginalUI.revision].join(':');
      if(this.cityMetricsKey!==metricsKey||this.cityMetricsMap!==this.map){this.cityMetrics=new Map();this.cityMetricsKey=metricsKey;this.cityMetricsMap=this.map;this.cityLayout=null;}
      const key=[zoom,this.detailScale,this.selection?.cell??''].join(':');
      if(this.cityLayout?.key===key)return this.cityLayout;
      const bounds=cameraBounds(this.state,this.map),bins=new Map(),rows=[],step=128*scale;
      const priority=cell=>{const c=this.state.cells[cell];return (this.selection?.cell===cell?10000:0)+(c.buildingKind==='city'?100+Number(c.buildingLevel||1)*10:0);};
      const ordered=[...this.namedSites].sort((a,b)=>priority(b.cell)-priority(a.cell)||a.cell-b.cell);
      for(const {cell,p} of ordered){
        const site=this.state.cells[cell],metricKey=cell+':'+site.name;
        if(!this.cityMetrics.has(metricKey))this.cityMetrics.set(metricKey,this.api.cityLabelSize(site.name,cell));
        const size=this.cityMetrics.get(metricKey),width=size.width*scale,height=size.height*scale;
        const x=Math.max(bounds.left,Math.min(bounds.left+bounds.width-width,p.x-width/2));
        const y=p.y+29+height<=bounds.top+bounds.height?p.y+29:Math.max(bounds.top,p.y-29-height);
        const box={left:x-4*scale,top:y-3*scale,right:x+width+4*scale,bottom:y+height+3*scale};
        const keys=[];let blocked=false;
        for(let by=Math.floor(box.top/step);by<=Math.floor(box.bottom/step);by++)for(let bx=Math.floor(box.left/step);bx<=Math.floor(box.right/step);bx++){
          const k=bx+':'+by;keys.push(k);
          if((bins.get(k)||[]).some(b=>box.left<b.right&&box.right>b.left&&box.top<b.bottom&&box.bottom>b.top))blocked=true;
        }
        if(blocked)continue;
        rows.push({cell,p,box});
        for(const k of keys){if(!bins.has(k))bins.set(k,[]);bins.get(k).push(box);}
      }
      this.cityLayout={key,rows,index:new SpatialIndex(rows,row=>row.box)};
      return this.cityLayout;
    }
    labels() {
      const {draw,state}=this;
      const scale=Math.max(1,this.markerScale),density=Math.max(1,Math.min(4,Math.ceil(draw.getTransform().a*scale/2)));
      const bounds=cameraBounds(state,this.map);
      const wanted=new Set();this.pendingCityLabels??=new Map();
      for(const {cell:i,p} of this.cityLabelLayout().index.query(this.visibleRect)){
        const c=state.cells[i];
        const key=i+':'+c.name+':'+density;
        wanted.add(key);
        if(!this.cityLabels.has(key)){
          if(this.api.cameraActive||this.pendingCityLabels.has(key)){this.pendingCityLabels.set(key,{name:c.name,cell:i,density});continue;}
          this.cacheCityLabel(key,c.name,i,density);
        }
        const label=this.cityLabels.get(key);
        if(draw.gpu&&this.api.cameraActive&&draw.sources&&!draw.sources.has(label)){this.pendingCityLabels.set(key,{name:c.name,cell:i,density});continue;}
        const width=label.width/density/2*scale,height=label.height/density/2*scale;
        // Text stays legible at distance, but its anchor stays on the building.
        const x=Math.max(bounds.left,Math.min(bounds.left+bounds.width-width,p.x-width/2));
        const y=p.y+29+height<=bounds.top+bounds.height?p.y+29:Math.max(bounds.top,p.y-29-height);
        draw.drawImage(label,x,y,width,height);}
      for(const key of this.pendingCityLabels.keys())if(!wanted.has(key))this.pendingCityLabels.delete(key);
    }
    cacheCityLabel(key,name,cell,density){
      if(this.cityLabels.size>=1024){const oldest=this.cityLabels.keys().next().value;this.draw.release?.(this.cityLabels.get(oldest));this.cityLabels.delete(oldest);}
      this.cityLabels.set(key,this.api.cityLabel(name,cell,density).firstElementChild);
    }
    grid() {
      if(!this.showGrid)return;
      this.draw.strokeStyle='#33333360';this.draw.lineWidth=.7/this.zoom;
      for(const {site,p} of this.visibleCells){if(!site.active)continue;
        hex(this.draw,p.x,p.y);this.draw.stroke();
      }
    }
    highlights() {
      const range=new Set(this.selection?.range),targets=new Set(this.selection?.targets);
      for(const {site:c,cell:i,p} of this.visibleCells){if(!c.active)continue;
        if(range.has(i)&&i!==this.selection.cell&&!this.stationed.has(i))this.api.sprite('buildings','move.png',p.x,p.y);
        if(targets.has(i))this.api.sprite('buildings','attack.png',p.x,p.y);
      }
      if(this.selection){const p=point(this.selection.cell,this.state.width);let lib=this.api.libraries.get('choose');
        if(lib&&selectionHostile(this.state,this.selection.cell)){
          if(this.redChoose?.source!==lib.texture){
            const texture=document.createElement('canvas');texture.width=lib.texture.naturalWidth;texture.height=lib.texture.naturalHeight;
            const d=texture.getContext('2d');d.drawImage(lib.texture,0,0);d.globalCompositeOperation='multiply';d.fillStyle='#ff0000';d.fillRect(0,0,texture.width,texture.height);d.globalCompositeOperation='destination-in';d.drawImage(lib.texture,0,0);
            this.redChoose={...lib,texture,source:lib.texture};
          }lib=this.redChoose;
        }
        const item=lib?.byName.get('choose'),tick=this.time/1000*(lib?.fps||24);
        const b=lib?animationBounds(lib,item,tick):null,ratio=lib?.pixelRatio||1;
        this.draw.save();this.draw.translate(p.x-(b?(b.left+b.right)/(2*ratio):0),p.y-(b?(b.top+b.bottom)/(2*ratio):0));this.api.animation(lib,item,tick);this.draw.restore();}
    }
    tutorialArrow() {
      const cell=this.tutorialCell;
      if(!Number.isInteger(cell)||!this.state.cells[cell]?.active)return;
      const p=point(cell,this.state.width);if(!this.visible(p))return;
      // Native 184e30 draws a separate map arrow for the pending wait-area target.
      const owner=this.state.cells[cell].owner;
      const color=owner&&owner!==this.state.playerFaction?'red':'blue';
      const offset=this.tutorialArrowMotion?.offset||0;
      this.api.sprite('buildings','arrow_shadow.png',p.x-offset*.5,p.y+offset*.5,.7,this.draw);
      this.api.sprite('buildings','arrow_'+color+'.png',p.x,p.y+offset,1,this.draw);
    }
    visible(p,padding=120) {const r=this.visibleRect;return p.x>=r.left-padding&&p.x<=r.left+r.width+padding&&p.y>=r.top-padding&&p.y<=r.top+r.height+padding;}
  }
  root.Ew4Battlefield=Object.freeze({point,neighbor,cameraBounds,hex,textPath,pathLettering,mapLabelBounds,pose,badgeAlpha,badgeColor,selectionHostile,recruitDuration,recruitOffset,advanceTutorialArrow,flagFrame,flagAnimated,facilityFlagAnimated,healthColor,flagPosition,moraleSprite,objectiveMarker,terrainRows,animationBounds,SpatialIndex,Renderer});
})(globalThis);
