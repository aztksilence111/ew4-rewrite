'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5510',folder=path.resolve(process.argv[3]||'build/device-plan-20261024/entry');
const median=values=>{const sorted=[...values].sort((a,b)=>a-b),middle=Math.floor(sorted.length/2);return sorted.length%2?sorted[middle]:(sorted[middle-1]+sorted[middle])/2;};
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report=process.argv.includes('--resume')?JSON.parse(await fs.readFile(path.join(folder,'results.json'),'utf8')):{runs:[],errors:[],scope:'Fresh browser pages and decoded-image caches; desktop engine at phone viewport, not physical device or cold OS cache. Only prefetch and first-screen waiting are toggled.'};
  try{
    for(let group=0;group<2;group++)for(const stage of group?['after','before']:['before','after'])for(const dwell of [0,1500,5000])for(let sample=0;sample<3;sample++){
      if(report.runs.some(r=>r.group===group&&r.stage===stage&&r.dwell===dwell&&r.sample===sample))continue;
      const page=await browser.newPage({viewport:{width:844,height:390}});
      page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      if(stage==='before')await page.evaluate(()=>{
        preloadBattleResources=async()=>{};
        const originalStage=performanceLog.stage.bind(performanceLog),originalWarm=battlefield.prewarm.bind(battlefield);
        performanceLog.stage=(name,action)=>{
          if(name!=='images')return originalStage(name,action);
          const map=manifest.maps[state.map],files=[...map.tiles.map(t=>t.file),'images/map_pt@2x.png',manifest.mapDrawing?.borderTexture,'images/tiles.png','images/tiles_hd.png','images/buildings.png','images/buildings_hd.png'].filter(Boolean);
          return originalStage(name,()=>Promise.all(files.map(loadImage)));
        };
        battlefield.prewarm=(world,map)=>{
          const w=canvas.clientWidth/.48+512,h=canvas.clientHeight/.48+512;
          return originalWarm(world,map,{left:view.x-w/2,top:view.y-h/2,width:w,height:h});
        };
      });
      const result=await page.evaluate(async dwell=>{
        await chooseScenario(catalog.scenarios.find(s=>s.id==='conquest1'));
        const frames=[];let previous=performance.now(),running=true;
        const heartbeat=()=>{const now=performance.now();frames.push(now-previous);previous=now;if(running)requestAnimationFrame(heartbeat);};requestAnimationFrame(heartbeat);
        if(dwell)await new Promise(resolve=>setTimeout(resolve,dwell));
        running=false;
        const preload={images:images.size,libraries:libraries.size};
        await start('conquest1','fra');
        const first=performanceLog.records.at(-1),templates={coasts:battlefield.surface.coasts.size,borders:battlefield.surface.borders.size};
        const loadedTiles=manifest.maps[state.map].tiles.filter(t=>images.has(t.file)).length;
        await showMain();await start('conquest1','fra');
        const repeat=performanceLog.records.at(-1);
        return {first,repeat,templates,loadedTiles,preload,maxCountryFrameMs:Math.max(0,...frames)};
      },dwell);
      assert.equal(result.first.status,'ready');assert.equal(result.repeat.status,'ready');
      report.runs.push({stage,group,dwell,sample,...result});await page.close();
      await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2));
    }
    report.summary=[];
    for(const dwell of [0,1500,5000])for(const stage of ['before','after']){
      const rows=report.runs.filter(r=>r.stage===stage&&r.dwell===dwell);
      report.summary.push({stage,dwell,samples:rows.length,entryMedianMs:median(rows.map(r=>r.first.elapsedMs)),repeatMedianMs:median(rows.map(r=>r.repeat.elapsedMs)),
        imageMedianMs:median(rows.map(r=>r.first.stages.images)),templateMedianMs:median(rows.map(r=>r.first.stages.templates)),loadedTiles:rows.map(r=>r.loadedTiles),templates:rows.map(r=>r.templates)});
    }
    assert.deepEqual(report.errors,[]);report.checks=report.runs.length*2;
  }finally{await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,summary:report.summary}));
})().catch(e=>{console.error(e);process.exitCode=1;});
