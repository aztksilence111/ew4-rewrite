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
  function terrainRows(site) {
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
  class Renderer {
    constructor(api) {this.api=api;this.surface=new Ew4MapSurface.Surface();this.cityLabels=new Map();this.mapLabels=new Map();this.unitFrames=new Map();this.unitFramePixels=0;this.unitLibraryIds=new WeakMap();this.nextLibraryId=1;this.resetMotions();}
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
      if(this.preparedState!==this.state){
        if(this.preparedMap!==this.map){this.cityLabels.clear();this.mapLabels.clear();this.unitFrames.clear();this.unitFramePixels=0;}
        this.preparedMap=this.map;this.preparedState=this.state;
        this.factions=new Map(this.state.factions.map(f=>[f.id,f]));
        this.stationed=new Map(this.state.units.map(u=>[u.cell,u]));
        this.unitIds=new Set(this.state.units.map(u=>u.id));
        this.orderedUnits=[...this.state.units].sort((a,b)=>a.cell-b.cell);
        this.objectiveCells=[];
        this.state.cells.forEach((site,cell)=>{if(site.active&&objectiveMarker(site,this.stationed.get(cell)))this.objectiveCells.push(cell);});
        this.groundSignature=JSON.stringify([this.state.width,this.state.cells,this.state.factions.map(f=>[f.id,f.color])]);
        this.visibleKey=null;
      }
      this.syncMotions();
      const r=this.visibleRect,visibleKey=[r.left,r.top,r.width,r.height].join(',');
      if(this.visibleKey!==visibleKey){
        this.visibleKey=visibleKey;this.visibleCells=this.cellsInRect(r);
        this.visibleUnits=this.orderedUnits.filter(unit=>this.visible(point(unit.cell,this.state.width)));
      }
      this.animated=!!this.selection||this.visibleUnits.some(u=>this.unitMotions.get(u.id)?.playing||u.burning>0||u.category==='fort'&&u.health>0&&u.maxHealth-u.health>=80||badgeAlpha(u,this.state,300)!==1||flagAnimated(u,this.state)&&this.factions.get(u.owner)?.flag&&!this.factions.get(u.owner)?.flagImage)||
        this.visibleCells.some(({site,cell})=>site.active&&(objectiveMarker(site,this.stationed.get(cell))||site.buildingKind==='port'&&site.owner&&!this.stationed.has(cell)));
      // Keep flags above all unit bodies, including ships on adjacent cells.
      this.staticGround();this.objectives();this.units();this.flags();this.unitMarkers();this.labels();this.highlights();
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
          this.draw.fillStyle='#688e91';this.draw.fillRect(this.visibleRect.left,this.visibleRect.top,this.visibleRect.width,this.visibleRect.height);
          this.ground();this.facilities(true);this.terrain(false);this.ownership();this.mapTiles();this.mapText();this.borders();this.terrain(true);this.facilities(false);
        }finally{this.draw=target;this.visibleRect=rect;this.visibleCells=cells;}
        this.staticKey=key;this.staticSignature=this.groundSignature;this.staticMap=this.map;
        this.staticBuilds=(this.staticBuilds||0)+1;
      }
      this.staticState=this.state;
      target.save();target.resetTransform();
      target.drawImage(this.staticCanvas,this.staticX-matrix.e,this.staticY-matrix.f,size.width,size.height,0,0,size.width,size.height);target.restore();
    }
    ground() {
      const {draw,visibleRect:r}=this, pattern=this.api.image('images/map_pt@2x.png');
      if(pattern.complete&&pattern.naturalWidth){const fill=draw.createPattern(pattern,'repeat');fill.setTransform(new DOMMatrix().scale(.5));draw.fillStyle=fill;draw.fillRect(r.left,r.top,r.width,r.height);}
    }
    ownership() {
      const {draw}=this;
      // Land in the original texture is transparent. Ownership belongs BELOW coast/river tiles.
      for(const {site:c,cell:i,p} of this.visibleCells){
        if(c.owner&&!c.sea){const color=Ew4MapSurface.rgba(this.factions.get(c.owner)?.color||0);
          hex(draw,p.x,p.y);draw.fillStyle=`rgba(${color[0]},${color[1]},${color[2]},${color[3]/255})`;draw.fill();}
        else {const neighbors=this.neighbors(i);if(neighbors.some(n=>n?.active&&!n.sea&&n.color))draw.drawImage(this.surface.coast(neighbors),p.x-32,p.y-36,64,72);}
      }
    }
    neighbors(index){return Array.from({length:6},(_,d)=>{const cell=this.state.cells[neighbor(index,d,this.state.width,this.state.height)];
      return cell?{...cell,color:this.factions.get(cell.owner)?.color||0}:null;});}
    mapTiles() {
      for(const tile of this.map.tiles){if(!this.visible({x:tile.x+tile.width/2,y:tile.y+tile.height/2},Math.max(tile.width,tile.height)))continue;
        const image=this.api.image(tile.file);if(image.complete&&image.naturalWidth)this.draw.drawImage(image,tile.x,tile.y,tile.width,tile.height);
      }
    }
    borders() {
      const {draw,state}=this,rules=this.api.manifest.mapDrawing;if(!rules)return;
      const texture=this.api.image(rules.borderTexture);
      for(const {site:c,cell:i,p} of this.visibleCells){if(!c.active||c.sea||!c.owner)continue;
        const mask=Ew4MapSurface.borderMask(c,this.neighbors(i));if(!mask)continue;
        const bitmap=this.surface.border(mask,rules,texture);if(bitmap)draw.drawImage(bitmap,p.x-32,p.y-36,64,72);
      }
    }
    terrain(seaForeground) {
      for(const overlay of this.map.overlays)if(!!overlay.seaForeground===seaForeground&&this.state.cells[overlay.cell]?.active&&this.visible(overlay))this.api.sprite('tiles',overlay.image,overlay.x,overlay.y,1,this.draw);
    }
    mapText() {
      const lib=this.api.libraries.get('maptext');if(!lib)return;
      if(this.mapLabelSource!==lib.texture||this.mapFontRevision!==Ew4OriginalUI.revision){this.mapLabelSource=lib.texture;this.mapFontRevision=Ew4OriginalUI.revision;this.mapLabels.clear();}
      const density=1;
      for(const label of this.map.labels||[]){const p={x:Number(label.tx),y:Number(label.ty)};if(!this.visible(p,Math.max(Number(label.width),Number(label.height))))continue;
        const chinese=this.api.geographicLanguage==='zh'&&label.textZh;
        const opacity=Number.isFinite(label.opacity)?Math.max(0,Math.min(1,label.opacity)):60/255;
        const key=label.libName+':'+density+':'+opacity+':'+(chinese?'zh':'en');
        if(!this.mapLabels.has(key)){
          const item=lib.byName.get(label.libName);let bounds=animationBounds(lib,item);if(!bounds)continue;
          if(chinese){
            const angle=Number(label.angle||0)*Math.PI/180,w=Number(label.letteringLength)/2+Number(label.size)*.2+5,h=Number(label.size)*.7+5;
            const x=Math.abs(Math.cos(angle))*w+Math.abs(Math.sin(angle))*h,y=Math.abs(Math.sin(angle))*w+Math.abs(Math.cos(angle))*h;
            bounds={left:-x,top:-y,right:x,bottom:y};
          }
          const ratio=lib.pixelRatio||1,left=Math.floor(bounds.left/ratio)-3,top=Math.floor(bounds.top/ratio)-3;
          const width=Math.ceil(bounds.right/ratio)-left+3,height=Math.ceil(bounds.bottom/ratio)-top+3;
          const canvas=document.createElement('canvas');canvas.width=width*density;canvas.height=height*density;
          const draw=canvas.getContext('2d');draw.scale(density,density);draw.translate(-left,-top);draw.globalAlpha=opacity;
          if(chinese){
            void Ew4OriginalUI.prepareText(label.textZh,label.sea?undefined:['EW4 Serif','EW4 Kai','EW4 Han','EW4 Han Plane 2','EW4 Han Plane 3','EW4 Unicode','EW4 Unicode Supplement']);
            const height=Number(label.size),text=[...Ew4OriginalUI.availableText(label.textZh)],length=Number(label.letteringLength);
            draw.rotate(Number(label.angle||0)*Math.PI/180);
            draw.font=(label.sea?'italic ':'')+height+'px '+(label.sea?'':'"EW4 Serif",')+Ew4OriginalUI.fontFamily;
            draw.fontKerning='none';draw.textBaseline='alphabetic';
            const widths=text.map(char=>draw.measureText(char).width),natural=widths.reduce((a,b)=>a+b,0);
            const scale=Math.min(1,length/natural),spacing=text.length>1?Math.max(0,(length-natural*scale)/(text.length-1)):0;
            draw.scale(scale,1);draw.fillStyle=label.sea?'#ffffff':'#303333';
            let x=-Math.max(length,natural*scale)/2/scale;
            const metrics=draw.measureText(text.join('')),baseline=(metrics.actualBoundingBoxAscent-metrics.actualBoundingBoxDescent)/2;
            for(let i=0;i<text.length;i++){draw.fillText(text[i],x,baseline);x+=widths[i]+spacing/scale;}
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
      for(const {site,cell,p} of this.visibleCells){
        if(!site.active||!site.owner||!site.buildingKind||site.buildingKind==='farmland'||this.stationed.has(cell))continue;
        const faction=this.factions.get(site.owner);this.flag(faction?.flag,p,site.buildingKind==='port',faction?.flagImage);
      }
    }
    units() {
      const {draw,state,time}=this;
      this.visibleUnitMarkers=[];
      // Native movement emitters render behind the unit bodies, using HD-to-map scale.
      const activeTrails=new Set();
      for(const [id,motion] of this.motions){const unit=state.units.find(u=>u.id===id);if(!unit)continue;
        const p=pose(motion,state.width,time),profile=this.api.effects?.profile(unit.profile,this.factions.get(unit.owner))||this.api.manifest.profiles[unit.profile];
        if(p.moving){activeTrails.add(id);this.api.effects?.trail(unit.id,p.x,p.y,Number(profile?.grade||0),state.cells[p.cell??unit.cell]?.sea,time,this.visible(p),this.arrivalSpeed||1);}
      }
      this.api.effects?.finishTrails?.(activeTrails,time);
      this.api.effects?.frame(draw,time,'ground',this.visibleRect);
      const units=this.motions.size?this.orderedUnits.filter(u=>this.motions.has(u.id)||this.visible(point(u.cell,state.width))):this.visibleUnits;
      for(const unit of units){
        const motion=this.motions.get(unit.id),p=motion?pose(motion,state.width,time):point(unit.cell,state.width);
        if(!this.visible(p))continue;
        const profile=this.api.effects?.profile(unit.profile,this.factions.get(unit.owner))||this.api.manifest.profiles[unit.profile],lib=this.api.libraries.get(profile?.library);if(!profile)continue;
        draw.save();
        const death=this.deaths?.get(unit.id),deathProgress=death?Math.max(0,Math.min(1,(time-death.started)/death.duration)):0;
        if(deathProgress>=1){draw.restore();continue;}
        const facing=motion?p.facing:this.facings.get(unit.id)??(unit.facing===0?-1:1),sea=state.cells[p.cell??unit.cell]?.sea;
        draw.save();if(death)draw.globalAlpha*=1-deathProgress;
        const arrival=this.arrivals.get(unit.id);if(arrival!=null)draw.translate(0,recruitOffset(time-arrival,this.arrivalSpeed||1));
        if(sea&&unit.category!=='naval')this.api.sprite('buildings',facing>0?'transportship1.png':'transportship2.png',p.x,p.y);
        else if(lib){
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
        const color=badgeColor(state,unit.owner);
        draw.save();draw.globalAlpha*=death?1:badgeAlpha(unit,state,time);
        this.api.sprite('buildings','hpbar_'+color+'.png',p.x-13,p.y+15);
        draw.beginPath();draw.arc(p.x-13,p.y+15,9,Math.PI,Math.PI+Math.PI*Math.max(0,death?0:unit.maxHealth>0?Math.min(1,unit.health/unit.maxHealth):0));draw.strokeStyle=healthColor(death?0:unit.health,unit.maxHealth);draw.lineWidth=2.5;draw.stroke();
        this.api.sprite('buildings','mark_unit_'+profile.unitIndex+'.png',p.x-13,p.y+15);
        draw.restore();
        if(unit.transport&&unit.category!=='naval'&&unit.category!=='fort')this.api.sprite('buildings','ship_mark.png',p.x+19,p.y+17);
        if(unit.burning>0){const fire=this.api.libraries.get('anim_fire'),item=fire?.byName.get(['1','3','2'][unit.burning-1]);if(item){draw.save();draw.globalAlpha=.5;draw.translate(p.x,p.y);this.api.animation(fire,item,time/1000*fire.fps);draw.restore();}}
        if(!death&&unit.category==='fort'&&unit.health>0){const damaged=Math.max(0,Math.min(3,Math.floor((unit.maxHealth-unit.health)/80)));if(damaged)this.api.effects?.element?.('damaged '+damaged,p.x,p.y,time,draw);}
        const rank=Number(unit.veterancy);if(rank>0)this.api.sprite('buildings','rank_'+rank+'.png',p.x+8,p.y-8);
        if(!death)this.visibleUnitMarkers.push({unit,p});
        draw.restore();
      }
    }
    unitMarkers(){
      // Draw every flag after all bodies so nearby ships cannot cover them.
      const {state}=this;
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
    labels() {
      if(this.zoom<=.5)return;
      const {draw,state}=this;
      const density=Math.max(1,Math.min(4,Math.ceil(draw.getTransform().a/2)));
      for(const {site:c,cell:i,p} of this.visibleCells){if(!c.active||!c.name)continue;
        const key=i+':'+c.name+':'+density;
        if(!this.cityLabels.has(key)){
          if(this.cityLabels.size>=1024)this.cityLabels.delete(this.cityLabels.keys().next().value);
          this.cityLabels.set(key,this.api.cityLabel(c.name,i,density).firstElementChild);
        }
        const label=this.cityLabels.get(key),width=label.width/density/2,height=label.height/density/2;
        draw.drawImage(label,p.x-width/2,p.y+29,width,height);}
    }
    highlights() {
      const range=new Set(this.selection?.range),targets=new Set(this.selection?.targets);
      for(const {site:c,cell:i,p} of this.visibleCells){if(!c.active)continue;
        if(this.showGrid){hex(this.draw,p.x,p.y);this.draw.strokeStyle='#33333360';this.draw.lineWidth=.7/this.zoom;this.draw.stroke();}
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
    visible(p,padding=120) {const r=this.visibleRect;return p.x>=r.left-padding&&p.x<=r.left+r.width+padding&&p.y>=r.top-padding&&p.y<=r.top+r.height+padding;}
  }
  root.Ew4Battlefield=Object.freeze({point,neighbor,cameraBounds,hex,pose,badgeAlpha,badgeColor,selectionHostile,recruitDuration,recruitOffset,flagFrame,flagAnimated,healthColor,flagPosition,moraleSprite,objectiveMarker,terrainRows,animationBounds,Renderer});
})(globalThis);
