'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],folder=path.resolve(process.argv[3]),reference=path.resolve(process.argv[4]);
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const source={battlefield:await fs.readFile(path.join(reference,'battlefield.js'),'utf8'),pixi:await fs.readFile(path.join(reference,'pixi-battle.js'),'utf8')};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const report={scope:'Generic repeated-map fixtures, original complete chunk reference versus native GPU terrain, desktop Edge DPR2; cross-rasterizer mean RGB tolerance 4/255; not phone timings',cases:[],errors:[]};
  try{
    for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844]]){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:2});
      page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>ctx?.gpu&&[...images.values()].every(i=>i.complete));
      const cases=await page.evaluate(async source=>{
        suspendRendering();cameraInputUntil=Infinity;effects.clear();
        const optimized={battlefield:Ew4Battlefield,pixi:Ew4Pixi},original={state,ctx,battlefield,view:{...view}};
        (0,eval)(source.battlefield);(0,eval)(source.pixi);
        const reference={battlefield:Ew4Battlefield,pixi:Ew4Pixi};globalThis.Ew4Battlefield=optimized.battlefield;globalThis.Ew4Pixi=optimized.pixi;
        let inputActive=false;
        const map=manifest.maps[state.map],results=[],api={...battlefield.api,effects:undefined,get cameraActive(){return inputActive;}},time=2000;
        const oldWidth=state.width,oldHeight=state.height,origin={...view};
        for(const repeat of [1,2,4]){
          const w=oldWidth*repeat,h=oldHeight*repeat,cells=[];
          for(let row=0;row<h;row++)for(let col=0;col<w;col++)cells.push(state.cells[(row%oldHeight)*oldWidth+col%oldWidth]);
          const units=[],overlays=[],tiles=[],labels=[];
          for(let y=0;y<repeat;y++)for(let x=0;x<repeat;x++){
            for(const unit of state.units)units.push({...unit,id:unit.id+':'+x+':'+y,cell:(Math.floor(unit.cell/oldWidth)+y*oldHeight)*w+unit.cell%oldWidth+x*oldWidth});
            for(const o of map.overlays)overlays.push({...o,cell:(Math.floor(o.cell/oldWidth)+y*oldHeight)*w+o.cell%oldWidth+x*oldWidth,x:o.x+x*oldWidth*64,y:o.y+y*oldHeight*54});
            for(const t of map.tiles)tiles.push({...t,x:t.x+x*oldWidth*64,y:t.y+y*oldHeight*54});
            for(const l of map.labels||[])labels.push({...l,libName:l.libName+':'+x+':'+y,tx:Number(l.tx)+x*oldWidth*64,ty:Number(l.ty)+y*oldHeight*54});
          }
          // Retain real label identities so both renderers use the same library entries.
          labels.forEach((l,i)=>l.libName=map.labels[i%map.labels.length].libName);
          const fixture={...state,width:w,height:h,cells,units},fixtureMap={...map,overlays,tiles,labels},poses=[];
          for(const mode of ['reference','optimized','optimized','reference']){
            inputActive=false;
            const lib=mode==='reference'?reference:optimized,surface=document.createElement('canvas'),draw=new lib.pixi.Context(surface),renderer=new lib.battlefield.Renderer(api);
            renderer.scheduleChunkWarm=()=>{};ctx=draw;
            await renderer.prewarm(fixture,fixtureMap,{left:origin.x-innerWidth/origin.zoom/2,top:origin.y-innerHeight/origin.zoom/2,width:innerWidth/origin.zoom,height:innerHeight/origin.zoom});
            const scene={draw,state:fixture,map:fixtureMap,zoom:origin.zoom,detailScale:Math.max(1,Math.min(innerWidth/568,innerHeight/320,3)),showGrid:false,selection:null,selected:null,motions:new Map(),facings:new Map(),arrivals:new Map(),deaths:new Map(),combatMotions:new Map()};
            const renderAt=(x,y,relative=1)=>{
              const zoom=origin.zoom*relative,r={left:x-innerWidth/zoom/2,top:y-innerHeight/zoom/2,width:innerWidth/zoom,height:innerHeight/zoom};
              draw.begin(Math.round(innerWidth*2),Math.round(innerHeight*2));draw.setTransform(2*zoom,0,0,2*zoom,innerWidth-2*zoom*x,innerHeight-2*zoom*y);
              renderer.frame({...scene,zoom,visibleRect:r},time);draw.end();
            };
            renderAt(origin.x,origin.y);renderAt(origin.x,origin.y);
            if(poses.length<2)poses.push(new Uint8Array(draw.renderer.extract.pixels()));
            const samples=[];
            for(let i=0;i<60;i++){await new Promise(requestAnimationFrame);const began=performance.now();renderAt(origin.x+Math.sin(i/10)*30,origin.y+Math.cos(i/10)*30);samples.push(performance.now()-began);}
            const unit=units.find(u=>u.owner===fixture.playerFaction),moving=[];
            scene.motions.set(unit.id,{path:[unit.cell,unit.cell+1],started:time-100,stepMs:200});
            for(let i=0;i<20;i++){await new Promise(requestAnimationFrame);const began=performance.now();renderAt(origin.x,origin.y);moving.push(performance.now()-began);}
            scene.motions.clear();
            const cold=[];inputActive=true;
            for(const relative of [.7,.48,.28]){const began=performance.now(),builds=renderer.staticBuilds||0;renderAt(origin.x,origin.y,relative);cold.push({relative,ms:performance.now()-began,builds:(renderer.staticBuilds||0)-builds});}
            const row={repeat,cells:cells.length,units:units.length,overlays:overlays.length,mode,samples,moving,cold,visibleUnits:renderer.visibleUnits.length,stageChildren:draw.stage.children.length,poolObjects:draw.pool.reduce((n,s)=>n+!!s.sprite+!!s.graphics,0),chunkPixels:renderer.chunkPixels};
            results.push(row);renderer.invalidateGround();draw.renderer.destroy(true);for(const s of [...draw.sources.keys()])draw.release(s);
          }
          let differingPixels=0,totalDifference=0,colors=new Set();const [a,b]=poses;
          for(let i=0;i<a.length;i+=4){if(Math.max(Math.abs(a[i]-b[i]),Math.abs(a[i+1]-b[i+1]),Math.abs(a[i+2]-b[i+2]))>2)differingPixels++;for(let c=0;c<3;c++)totalDifference+=Math.abs(a[i+c]-b[i+c]);if(i%397===0)colors.add(a[i]+','+a[i+1]+','+a[i+2]);}
          results.push({repeat,differingPixels,meanDifference:totalDifference/(a.length/4*3),pixels:a.length/4,colors:colors.size});
        }
        state=original.state;ctx=original.ctx;Object.assign(view,original.view);cameraInputUntil=0;requestRender();
        return results;
      },source);
      for(const row of cases){if('differingPixels' in row){assert(row.meanDifference<4,'native GPU terrain matches original complete terrain within cross-rasterizer tolerance');assert(row.colors>40,'nonblank');}else if(row.mode==='optimized')assert.equal(row.chunkPixels,0,'no intermediate terrain raster');}
      report.cases.push({name,width,height,rows:cases});await page.screenshot({path:path.join(folder,name+'.png')});await page.close();
    }
    assert.deepEqual(report.errors,[]);
  }finally{await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2));await browser.close();}
  const median=s=>[...s].sort((a,b)=>a-b)[Math.floor(s.length/2)];
  console.log(JSON.stringify(report.cases.map(c=>({name:c.name,rows:c.rows.map(r=>'samples' in r?{...r,samples:undefined,moving:undefined,medianMs:median(r.samples),movingMedianMs:median(r.moving)}:r)}))));
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
