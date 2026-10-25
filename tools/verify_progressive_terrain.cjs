'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],folder=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const report={scope:'Desktop Edge DPR2, cold camera input and idle refinement; raster sampling tolerance 0.2%; not phone frame timings',cases:[],errors:[]};
  try{
    for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844]]){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:2});
      page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>ctx?.gpu&&[...images.values()].every(i=>i.complete));
      const cold=await page.evaluate(async()=>{
        suspendRendering();selection=null;selected=null;effects.clear();
        const origin={...view},scale=battlefield.detailScale,samples=[];
        const originals=new Map();let phases={};
        for(const method of ['frame','previewGround','ground','ownership','mapTiles','units','labels','objectives']){
          const original=battlefield[method];originals.set(method,original);battlefield[method]=function(...args){const began=performance.now();try{return original.apply(this,args);}finally{phases[method]=(phases[method]||0)+performance.now()-began;}};
        }
        const end=ctx.end;ctx.end=function(...args){const began=performance.now();try{return end.apply(this,args);}finally{phases.submit=(phases.submit||0)+performance.now()-began;}};
        cameraInputUntil=performance.now()+10000;
        battlefield.invalidateGround();
        for(const relative of [1,.85,.65,.48,.28]){
          Object.assign(view,origin,{zoom:scale*relative});clampView();
          phases={};const builds=battlefield.staticBuilds||0,previews=battlefield.previewBuilds||0,began=performance.now();
          render(2000);suspendRendering();
          samples.push({relative,ms:performance.now()-began,builds:(battlefield.staticBuilds||0)-builds,previews:(battlefield.previewBuilds||0)-previews,pixels:battlefield.chunkPixels,phases});
          await new Promise(requestAnimationFrame);
        }
        cameraInputUntil=0;
        const unit=state.units.find(u=>u.owner===state.playerFaction&&u.cell%state.width<state.width-1);
        motions.set(unit.id,{path:[unit.cell,unit.cell+1],started:1900,stepMs:200});
        battlefield.invalidateGround();phases={};
        const builds=battlefield.staticBuilds||0,previews=battlefield.previewBuilds||0,began=performance.now();
        render(2000);suspendRendering();
        samples.push({relative:'movement',ms:performance.now()-began,builds:(battlefield.staticBuilds||0)-builds,previews:(battlefield.previewBuilds||0)-previews,pixels:battlefield.chunkPixels,phases});
        motions.clear();
        for(const [method,original] of originals)battlefield[method]=original;ctx.end=end;
        cameraInputUntil=0;requestRender();return samples;
      });
      assert(cold.every(s=>s.builds===s.previews),'camera input must not synchronously rasterize complete terrain');
      assert(cold.some(s=>s.previews>0),'cold path exercised');
      await page.waitForFunction(()=>{
        const r=battlefield.visibleRect;return !battlefield.pendingCityLabels?.size&&[...battlefield.chunks.values()].every(c=>!c.preview||c.x+512<r.left||c.x>r.left+r.width||c.y+512<r.top||c.y>r.top+r.height);
      },null,{timeout:30000});
      await page.waitForFunction(()=>[...images.values()].every(i=>i.complete));
      await page.waitForTimeout(250);
      const refined=await page.evaluate(()=>{
        suspendRendering();render(2000);suspendRendering();const a=new Uint8Array(ctx.renderer.extract.pixels()),beforeImage=ctx.renderer.extract.canvas().toDataURL();
        cameraInputUntil=Infinity;
        const levels=[...battlefield.chunks.values()].map(c=>({x:c.x,y:c.y,density:c.density,image:c.canvas?.toDataURL()}));
        battlefield.invalidateGround();
        for(const c of levels)battlefield.chunkGround({left:c.x,top:c.y,width:512,height:512},c.density);
        render(2000);suspendRendering();cameraInputUntil=0;const b=ctx.renderer.extract.pixels();let differingPixels=0;const colors=new Set();
        for(let i=0;i<a.length;i+=4){if(Math.max(Math.abs(a[i]-b[i]),Math.abs(a[i+1]-b[i+1]),Math.abs(a[i+2]-b[i+2]))>2)differingPixels++;if(i%397===0)colors.add(a[i]+','+a[i+1]+','+a[i+2]);}
        const changedChunks=levels.filter(c=>battlefield.chunks.get([c.x,c.y,c.density].join(':'))?.canvas?.toDataURL()!==c.image).map(({image,...c})=>c);
        return {differingPixels,pixels:a.length/4,colors:colors.size,chunkPixels:battlefield.chunkPixels,previews:[...battlefield.chunks.values()].filter(c=>c.preview).length,changedChunks,beforeImage,afterImage:ctx.renderer.extract.canvas().toDataURL()};
      });
      for(const key of ['beforeImage','afterImage']){await fs.writeFile(path.join(folder,name+'-'+key+'.png'),Buffer.from(refined[key].split(',')[1],'base64'));delete refined[key];}
      report.cases.push({name,width,height,cold,refined});
      assert(refined.differingPixels/refined.pixels<.002,'idle refinement matches rebuilt terrain within raster sampling tolerance');assert(refined.colors>40,'nonblank refined scene');assert(refined.chunkPixels<=12*1024*1024,'bounded terrain cache');
      await page.screenshot({path:path.join(folder,name+'.png')});await page.close();
    }
    assert.deepEqual(report.errors,[]);
  }finally{await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
