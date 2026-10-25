'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],folder=path.resolve(process.argv[3]),reference=path.resolve(process.argv[4]);
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const source=await fs.readFile(reference,'utf8'),browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const report={scope:'Desktop Edge: original-resolution terrain during input and comparison with original Canvas layers; not phone timing',cases:[],errors:[]};
  try{
    for(const [name,width,height,dpr,scenario] of [['phone',1188,540,2,'conquest1'],['landscape',844,390,2,'conquest1'],['portrait',390,844,2,'conquest1'],['conquest4',1188,540,2,'conquest4']]){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:dpr});
      page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.evaluate(scenario=>start(scenario,'fra'),scenario);
      await page.waitForFunction(()=>ctx?.gpu&&[...images.values()].every(i=>i.complete));
      const result=await page.evaluate(async source=>{
        suspendRendering();selection=null;selected=null;effects.clear();
        const optimized=Ew4Battlefield;(0,eval)(source);const Reference=Ew4Battlefield.Renderer;globalThis.Ew4Battlefield=optimized;
        const origin={...view},samples=[];cameraInputUntil=0;
        Object.assign(view,origin,{zoom:battlefield.detailScale*.28});clampView();render(2000);suspendRendering();
        const idle=new Uint8Array(ctx.renderer.extract.pixels());
        aiRunning=true;cameraInputUntil=Infinity;render(2000);suspendRendering();
        const active=ctx.renderer.extract.pixels();let interactionDifferences=0;
        for(let i=0;i<idle.length;i++)if(idle[i]!==active[i])interactionDifferences++;
        const methods=['grid','objectives','units','flags','unitMarkers','labels','highlights'],saved=new Map();
        for(const method of methods){saved.set(method,battlefield[method]);battlefield[method]=()=>{};}
        render(2000);suspendRendering();
        const actual=new Uint8Array(ctx.renderer.extract.pixels()),actualImage=ctx.renderer.extract.canvas().toDataURL();
        const expected=document.createElement('canvas');expected.width=ctx.canvas.width;expected.height=ctx.canvas.height;
        const draw=expected.getContext('2d',{willReadFrequently:true}),m=ctx.getTransform(),r={...battlefield.visibleRect};
        draw.setTransform(m.a,m.b,m.c,m.d,m.e,m.f);
        const ref=new Reference({...battlefield.api,effects:undefined});
        for(const method of methods)ref[method]=()=>{};
        // Use the original layer order at the actual screen resolution.
        ref.staticGround=function(){this.draw.fillStyle='#688e91';this.draw.fillRect(r.left,r.top,r.width,r.height);this.ground();this.facilities(true);this.terrain(false);this.ownership();this.mapTiles();this.mapText();this.borders();this.terrain(true);this.facilities(false);this.bridges();};
        ref.frame({draw,state,map:manifest.maps[state.map],visibleRect:r,zoom:view.zoom,detailScale:battlefield.detailScale,motions:new Map(),arrivals:new Map(),facings:new Map()},2000);
        const expectedPixels=draw.getImageData(0,0,expected.width,expected.height).data;
        let total=0,differing=0;
        for(let i=0;i<actual.length;i+=4){let maximum=0;for(let c=0;c<3;c++){const delta=Math.abs(actual[i+c]-expectedPixels[i+c]);maximum=Math.max(maximum,delta);total+=delta;}if(maximum>32)differing++;}
        const actor=state.units[0];motions.set(actor.id,{path:[actor.cell,actor.cell+1],started:1900,stepMs:200});render(2000);suspendRendering();motions.clear();
        const moving=ctx.renderer.extract.pixels();let movementDifferences=0;
        for(let i=0;i<actual.length;i++)if(actual[i]!==moving[i])movementDifferences++;
        for(const [method,original] of saved)battlefield[method]=original;
        const originals=new Map(),raster={};
        for(const method of ['groundLayers','staticGround','units','labels']){const original=battlefield[method];originals.set(method,original);battlefield[method]=function(...args){const began=performance.now();try{return original.apply(this,args);}finally{raster[method]=(raster[method]||0)+performance.now()-began;}};}
        const end=ctx.end;ctx.end=function(...args){const began=performance.now();try{return end.apply(this,args);}finally{raster.submit=(raster.submit||0)+performance.now()-began;}};
        const groundRect=battlefield.gpuGroundRect;
        for(let i=0;i<35;i++){
          Object.assign(view,origin,{zoom:battlefield.detailScale*[1,.7,.48,.28,.45][i%5],x:origin.x+Math.sin(i)*300,y:origin.y+Math.cos(i)*150});clampView();
          for(const key of Object.keys(raster))delete raster[key];const began=performance.now();render(2000);suspendRendering();samples.push({ms:performance.now()-began,phases:{...raster}});
          await new Promise(requestAnimationFrame);
        }
        const nativeTiles=manifest.maps[state.map].tiles.map(tile=>images.get(tile.file)).filter(image=>ctx.sources.has(image));
        const preservedSizes=nativeTiles.every(image=>{const entry=ctx.sources.get(image);return entry.base.width===image.naturalWidth&&entry.base.height===image.naturalHeight;});
        for(const [method,original] of originals)battlefield[method]=original;ctx.end=end;
        Object.assign(view,origin,{zoom:battlefield.detailScale*.28});clampView();render(2000);suspendRendering();
        return {samples,interactionDifferences,movementDifferences,meanDifference:total/(actual.length/4*3),differingFraction:differing/(actual.length/4),nativeTiles:nativeTiles.length,preservedSizes,chunkPixels:battlefield.chunkPixels,sourcePixels:ctx.sourcePixels,overview:!!battlefield.overview,actualImage,referenceImage:expected.toDataURL()};
      },source);
      for(const key of ['actualImage','referenceImage']){await fs.writeFile(path.join(folder,name+'-'+key+'.png'),Buffer.from(result[key].split(',')[1],'base64'));delete result[key];}
      await page.screenshot({path:path.join(folder,name+'-during-input.png')});
      await page.screenshot({path:path.join(folder,name+'-river-coast.png'),clip:{x:width*.25,y:height*.38,width:width*.5,height:height*.5}});
      assert.equal(result.interactionDifferences,0,'input and idle use identical terrain quality');
      assert.equal(result.movementDifferences,0,'unit movement does not reduce terrain quality');
      assert(result.nativeTiles>0&&result.preservedSizes,'native map textures retain their original resolution');
      assert.equal(result.chunkPixels,0,'no screen-space terrain rasterization');assert.equal(result.overview,false,'no reduced-resolution overview');
      assert(result.meanDifference<4&&result.differingFraction<.05,'direct terrain matches original native-resolution Canvas layers');
      report.cases.push({name,width,height,dpr,scenario,...result});await page.close();
    }
    assert.deepEqual(report.errors,[]);
  }finally{await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({errors:report.errors,cases:report.cases.map(({samples,...r})=>({...r,medianMs:samples.map(s=>s.ms).sort((a,b)=>a-b)[Math.floor(samples.length/2)],maxMs:Math.max(...samples.map(s=>s.ms))}))}));
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
