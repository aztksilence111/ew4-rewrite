'use strict';

// Reuse the live game's Canvas renderer and font loader; export without camera cropping or name culling.
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const path=require('node:path');
const crypto=require('node:crypto');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root=path.resolve(__dirname,'..');
const base=process.argv[2]||'http://127.0.0.1:5585';

(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={date:new Date().toISOString(),source:base,method:'Current game Canvas renderer, complete map extent, all named sites, Chinese curved geographic lettering',maps:[],errors:[],missing:[]};
  try{
    const mods=await (await fetch(base+'/api/mods')).json();
    report.activeMod=mods.active;
    for(const [key,scenario,expected,farms,names,geography] of [['japan','conquest8',410,153,257,27],['hre','conquest9',647,262,385,28]]){
      const page=await browser.newPage({viewport:{width:1440,height:900},deviceScaleFactor:1});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      await page.goto(base+'/?renderer=canvas');
      await page.waitForFunction(()=>startupReady,null,{timeout:180000});
      await page.evaluate(async scenario=>{
        settings.cityLanguage='zh';settings.geographicLanguage='zh';
        await start(scenario,'fra');
      },scenario);
      await page.waitForFunction(()=>state&&!busy&&!sessionTransition,null,{timeout:180000});
      const result=await page.evaluate(async()=>{
        await prepareMapFonts(state);
        const definition=manifest.maps[state.map];
        await Promise.all([...definition.tiles.map(t=>t.file),'images/map_pt@2x.png','images/tiles.png','images/buildings.png'].map(loadImage));
        await loadLibrary('maptext');
        suspendRendering();
        const width=6000,scale=width/definition.width,height=Math.round(definition.height*scale);
        const canvas=document.createElement('canvas');canvas.width=width;canvas.height=height;
        const draw=canvas.getContext('2d');draw.imageSmoothingEnabled=true;draw.imageSmoothingQuality='high';draw.scale(scale,scale);
        const sprites=[],geoCalls=[];
        const api={...battlefield.api,cityLanguage:'zh',geographicLanguage:'zh',cameraActive:false,
          sprite:(atlas,name,x,y,factor,context)=>{sprites.push({atlas,name,x,y});return battlefield.api.sprite(atlas,name,x,y,factor,context);}};
        const exporter=new Ew4Battlefield.Renderer(api);
        exporter.staticGround=function(){this.groundLayers();};
        // A planning atlas has no temporary placeholder armies, selection, grids, or gameplay HUD.
        for(const name of ['grid','objectives','units','flags','unitMarkers','labels','highlights','playerOrders','tutorialArrow','scheduleCityLabels'])exporter[name]=()=>{};
        const originalMapText=exporter.mapText;
        exporter.mapText=function(){geoCalls.push(...this.labelIndex.query(this.visibleRect).map(l=>l.textZh));return originalMapText.call(this);};
        exporter.frame({draw,state,map:definition,visibleRect:{left:0,top:0,width:definition.width,height:definition.height},zoom:scale,detailScale:1,
          showGrid:false,selection:null,selected:null,selectedAt:0,motions:new Map(),facings:new Map(),arrivals:new Map(),deaths:new Map(),combatMotions:new Map(),arrivalSpeed:1},performance.now());
        draw.resetTransform();
        const occupancy=new Map(),boxes=[],step=128;
        const keys=box=>{const result=[];for(let y=Math.floor(box.top/step);y<=Math.floor(box.bottom/step);y++)for(let x=Math.floor(box.left/step);x<=Math.floor(box.right/step);x++)result.push(x+':'+y);return result;};
        const overlaps=(a,b)=>a.left<b.right&&a.right>b.left&&a.top<b.bottom&&a.bottom>b.top;
        const add=(box,owner)=>{const item={box,owner};boxes.push(item);for(const key of keys(box)){if(!occupancy.has(key))occupancy.set(key,[]);occupancy.get(key).push(item);}};
        const clear=(box,owner)=>keys(box).every(key=>(occupancy.get(key)||[]).every(item=>item.owner===owner||!overlaps(box,item.box)));
        const named=[];const farmland=[];const counts={},levels={farmland:{},city:{}};
        for(let cell=0;cell<state.cells.length;cell++){
          const site=state.cells[cell];if(!site.buildingKind)continue;
          const p=Ew4Battlefield.point(cell,state.width),x=p.x*scale,y=p.y*scale;
          counts[site.buildingKind]=(counts[site.buildingKind]||0)+1;
          if(levels[site.buildingKind])levels[site.buildingKind][site.buildingLevel]=(levels[site.buildingKind][site.buildingLevel]||0)+1;
          if(site.name)named.push({cell,site,x,y});
          if(site.buildingKind==='farmland')farmland.push({cell,x,y,level:site.buildingLevel});
          add({left:x-10,top:y-11,right:x+10,bottom:y+10},cell);
        }
        // Reserve the actual geography glyph pixels, not the large empty area around a curved word.
        for(const cached of exporter.mapLabels.values()){
          const context=cached.canvas.getContext('2d'),data=context.getImageData(0,0,cached.canvas.width,cached.canvas.height).data;
          const label=definition.labels.find(l=>l.libName&&exporter.mapLabels.get(l.libName+':1:'+l.opacity+':zh')===cached);
          if(!label)throw new Error('Geographic label cache mismatch');
          for(let y=0;y<cached.canvas.height;y+=24)for(let x=0;x<cached.canvas.width;x+=24){
            let ink=false;
            for(let py=y;py<Math.min(y+24,cached.canvas.height)&&!ink;py+=3)for(let px=x;px<Math.min(x+24,cached.canvas.width);px+=3)if(data[(py*cached.canvas.width+px)*4+3]>32){ink=true;break;}
            if(ink)add({left:(Number(label.tx)+cached.left+x)*scale-2,top:(Number(label.ty)+cached.top+y)*scale-2,
              right:(Number(label.tx)+cached.left+x+24)*scale+2,bottom:(Number(label.ty)+cached.top+y+24)*scale+2},'geographic');
          }
        }
        const placements=[];
        for(const {cell,site,x,y} of named.sort((a,b)=>(b.site.buildingKind==='city')-(a.site.buildingKind==='city')||b.site.buildingLevel-a.site.buildingLevel||a.cell-b.cell)){
          const label=battlefield.api.cityLabel(site.name,cell,1).firstElementChild;
          const factor=(site.buildingKind==='city'?20:18)/label.height,w=label.width*factor,h=label.height*factor;
          let best=null;
          for(const radius of [16,24,36,50,68,90,120,160,210,280,360]){
            for(const angle of [90,-90,0,180,45,-45,135,-135]){
              const radians=angle*Math.PI/180,tx=x+radius*Math.cos(radians)-w/2,ty=y+radius*Math.sin(radians)-h/2;
              const box={left:tx-3,top:ty-3,right:tx+w+3,bottom:ty+h+3};
              if(box.left<3||box.top<3||box.right>width-3||box.bottom>height-3||!clear(box,cell))continue;
              best={cell,name:site.name,kind:site.buildingKind,level:site.buildingLevel,x:tx,y:ty,width:w,height:h,box,leader:radius>24};break;
            }
            if(best)break;
          }
          if(!best)throw new Error('Cannot place full name without overlap: '+site.name);
          add(best.box,cell);
          if(best.leader){draw.strokeStyle='#506564';draw.lineWidth=.8;draw.beginPath();draw.moveTo(x,y);draw.lineTo(best.x+w/2,best.y+h/2);draw.stroke();}
          draw.drawImage(label,best.x,best.y,w,h);placements.push(best);
        }
        const nativeFarms=sprites.filter(s=>s.atlas==='buildings'&&/^farm_[1-4]_lv[1-3]\.png$/.test(s.name));
        const nativePorts=sprites.filter(s=>s.atlas==='buildings'&&/^port_[1-4]\.png$/.test(s.name));
        const nativeCities=sprites.filter(s=>s.atlas==='buildings'&&/^city_(east|west)_lv[1-7]\.png$/.test(s.name));
        const nativeFactories=sprites.filter(s=>s.atlas==='buildings'&&/^factory_(east|west)_lv[1-7]\.png$/.test(s.name));
        const nativeStables=sprites.filter(s=>s.atlas==='buildings'&&/^stable_(east|west)_lv[1-7]\.png$/.test(s.name));
        const colors=new Set(),pixels=draw.getImageData(0,0,width,height).data;
        let alpha=0;for(let i=0;i<pixels.length;i+=256){colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);if(pixels[i+3]===255)alpha++;}
        const stats={key:state.map,width,height,worldWidth:definition.width,worldHeight:definition.height,
          extent:{left:0,top:0,width:definition.width,height:definition.height},crop:false,counts,levels,names:placements.length,geography:geoCalls,
          nativeDraws:{farmland:nativeFarms.length,city:nativeCities.length,industry:nativeFactories.length,stable:nativeStables.length,port:nativePorts.length},
          portDirections:[...new Set(nativePorts.map(p=>p.name))].sort(),geographicCanvases:exporter.mapLabels.size,
          placements,farmland,colors:colors.size,opaqueSamples:alpha,allNamedCells:named.map(n=>n.cell),
          overlapCount:placements.filter((a,i)=>placements.slice(i+1).some(b=>overlaps(a.box,b.box))).length,
          clippedNames:placements.filter(p=>p.box.left<0||p.box.top<0||p.box.right>width||p.box.bottom>height).length,
          pendingFonts:Ew4OriginalUI.fontStatus.failures,waterTiles:definition.tiles.length,waterDetailTiles:definition.detailTiles.length};
        return {stats,jpeg:canvas.toDataURL('image/jpeg',.96).split(',')[1]};
      });
      const stats=result.stats;
      assert.equal(Object.values(stats.counts).reduce((a,b)=>a+b,0),expected,'all current buildings');
      assert.equal(stats.nativeDraws.farmland,farms,'all tiered farms drawn using native sprites');
      assert.equal(stats.names,names,'no zoom-based name culling');
      assert.equal(stats.geography.length,geography,'all current geographic labels');
      assert.equal(new Set(stats.geography).size,geography);
      assert.equal(stats.geographicCanvases,geography);
      assert.equal(stats.clippedNames,0);assert.equal(stats.overlapCount,0);assert.deepEqual(stats.pendingFonts,[]);
      assert.equal(stats.portDirections.length,4);assert.ok(stats.colors>1000&&stats.opaqueSamples>100000,'Rendered opaque map pixels '+JSON.stringify({colors:stats.colors,opaque:stats.opaqueSamples}));
      const buffer=Buffer.from(result.jpeg,'base64');
      const relative='maps/'+key+'/game-full-overview.jpg';
      await fs.writeFile(path.join(root,relative),buffer);
      const labelFile='maps/'+key+'/game-full-overview.labels.json';
      await fs.writeFile(path.join(root,labelFile),JSON.stringify({width:stats.width,height:stats.height,placements:stats.placements,farmland:stats.farmland},null,2)+'\n');
      delete stats.placements;delete stats.farmland;delete stats.allNamedCells;
      const sourceFiles=['rewrite/data/client/manifest.json','maps/'+key+'/buildings.json','maps/'+key+'/farmland.json','maps/'+key+'/'+key+'.bin'];
      stats.inputs={};for(const name of sourceFiles)stats.inputs[name]=crypto.createHash('sha256').update(await fs.readFile(path.join(root,name))).digest('hex');
      report.maps.push({...stats,file:relative,labelsFile:labelFile,bytes:buffer.length,sha256:crypto.createHash('sha256').update(buffer).digest('hex')});
      console.log(JSON.stringify({map:key,file:relative,width:stats.width,height:stats.height,counts:stats.counts,names:stats.names,geography:stats.geography.length,bytes:buffer.length}));
      await page.close();
    }
    assert.deepEqual(report.errors,[]);assert.deepEqual(report.missing,[]);
    report.errors=0;
    await fs.writeFile(path.join(root,'maps/japan_hre_source_20261026/full-map-preview-verification.json'),JSON.stringify(report,null,2)+'\n');
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
