'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5585',phase=process.argv[3]||'after';
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:process.env.EW4_HARDWARE_GPU?['--mute-audio']:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  const report={phase,errors:[],missing:[],runs:[]};
  try{
    for(const renderer of (process.argv[5]?[process.argv[5]]:['pixi','canvas'])){
      const page=await browser.newPage({viewport:{width:1440,height:900}});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:180000});
      for(const scenario of (process.argv[4]?[process.argv[4]]:['conquest8','conquest9'])){
        await page.evaluate(s=>start(s,'fra'),scenario);await page.waitForFunction(()=>state&&!busy&&!sessionTransition,null,{timeout:180000});
        const run=await page.evaluate(async()=>{
          settings.geographicLanguage='zh';await prepareMapFonts(state);
          const map=manifest.maps[state.map];view={x:map.width*.53,y:map.height*.54,zoom:.14};clampView();
          battlefield.cityLabels.clear();battlefield.pendingCityLabels?.clear();render(performance.now(),true);
          await new Promise(resolve=>setTimeout(resolve,800));
          const labels=battlefield.labels,cache=battlefield.cacheCityLabel,frames=[],labelTimes=[],slowFrames=[];
          const end=ctx.end;let endMs=0;
          ctx.end=function(...args){const t=performance.now();try{return end.apply(this,args);}finally{endMs=performance.now()-t;}};
          let built=0,draws=0,groundBuilds=0,groundMs=0;const drawImage=ctx.drawImage,ground=battlefield.groundLayers;
          ctx.drawImage=function(source,...args){if(source?.ew4Repaint)draws++;return drawImage.call(this,source,...args);};
          battlefield.cacheCityLabel=function(...args){built++;return cache.apply(this,args);};
          battlefield.labels=function(){const t=performance.now();labels.call(this);labelTimes.push(performance.now()-t);};
          battlefield.groundLayers=function(){const t=performance.now();groundBuilds++;try{return ground.call(this);}finally{groundMs+=performance.now()-t;}};
          const initial={x:view.x,y:view.y},upload=ctx.stats?.uploads||0;
          pointers.set(-1,{x:400,y:400,startX:400,startY:400});
          try{
            for(let i=0;i<32;i++){
              await new Promise(requestAnimationFrame);cameraInputUntil=performance.now()+160;
              view.x=initial.x+Math.sin(i/31*Math.PI)*1000;view.y=initial.y+Math.sin(i/31*Math.PI*2)*250;clampView();
              const t=performance.now();render(t,true);const ms=performance.now()-t;frames.push(ms);if(ms>100)slowFrames.push({frame:i,ms,endMs});
            }
          }finally{pointers.delete(-1);battlefield.labels=labels;battlefield.cacheCityLabel=cache;battlefield.groundLayers=ground;ctx.drawImage=drawImage;ctx.end=end;}
          const sorted=values=>[...values].sort((a,b)=>a-b),p=(values,n)=>sorted(values)[Math.floor((values.length-1)*n)];
          const debug=ctx.gpu&&ctx.renderer.gl.getExtension('WEBGL_debug_renderer_info'),driver=debug?ctx.renderer.gl.getParameter(debug.UNMASKED_RENDERER_WEBGL):null;
          return {map:state.map,renderer:ctx.gpu?'pixi':'canvas',driver,frames:frames.length,frameMedian:p(frames,.5),frameP95:p(frames,.95),frameMax:Math.max(...frames),
            labelMedian:p(labelTimes,.5),labelP95:p(labelTimes,.95),builtDuringDrag:built,drawsPerLabelPass:draws/labelTimes.length,groundBuilds,groundMs,uploadsDuringDrag:(ctx.stats?.uploads||0)-upload,cacheSize:battlefield.cityLabels.size,slowFrames};
        });
        report.runs.push(run);
        if(phase==='after')assert.equal(run.builtDuringDrag,0,'No synchronous city rasterization during drag');
        if(phase==='after'&&renderer==='pixi')assert.equal(run.uploadsDuringDrag,0,'No first city texture upload during drag');
        if(phase==='after'){
          const layout=await page.evaluate(()=>{
            const far=battlefield.cityLabelLayout().rows;
            const overlaps=far.some((a,i)=>far.slice(i+1).some(b=>a.box.left<b.box.right&&a.box.right>b.box.left&&a.box.top<b.box.bottom&&a.box.bottom>b.box.top));
            const old=battlefield.zoom;battlefield.zoom=2.5;
            const near=battlefield.cityLabelLayout().rows.length;const cell=battlefield.namedSites.at(-1).cell;
            battlefield.selection={cell};const selected=battlefield.cityLabelLayout().rows.some(row=>row.cell===cell);battlefield.selection=undefined;battlefield.zoom=old;
            const levels={};for(const c of state.cells)if(c.buildingKind==='city')levels[c.buildingLevel]=(levels[c.buildingLevel]||0)+1;
            return {all:battlefield.namedSites.length,far:far.length,near,overlaps,selected,levels};
          });
          assert.ok(!layout.overlaps&&layout.near>layout.far&&layout.selected,'stable nonoverlapping labels expand with zoom and retain selected city '+JSON.stringify(layout));
          assert.equal(Object.keys(layout.levels).length,7,'seven starting city tiers in actual client');
          run.layout=layout;
          if(renderer==='pixi')await page.screenshot({path:path.resolve('maps/japan_hre_source_20261026/game-'+run.map+'-overview.jpg'),type:'jpeg',quality:90});
        }
      }
      await page.close();
    }
    assert.deepEqual(report.errors,[]);assert.deepEqual(report.missing,[]);
  }finally{
    await fs.mkdir('build/map-review-tests-20261027',{recursive:true});
    await fs.writeFile(path.resolve('build/map-review-tests-20261027/city-pan-'+phase+'.json'),JSON.stringify(report,null,2)+'\n');
    await browser.close();
  }
  console.log(JSON.stringify(report));
})().catch(error=>{console.error(error);process.exitCode=1;});
