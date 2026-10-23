'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const {createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright');
const base=process.env.EW4_PERF_URL||'http://127.0.0.1:5210';
const phase=process.argv[2]||'after';
const folder=process.env.EW4_RENDER_REPORT||'E:/Workplace/game/o4mod/rewrite/reports/render-skip-20261021';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  const report={phase,scope:'Isolated profile, desktop Edge; not Android device validation',checks:0,errors:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady);
    await page.evaluate(()=>start('conquest1','fra'));
    await page.waitForTimeout(500);
    report.render=await page.evaluate(async()=>{
      const original=battlefield.frame,frames=[];let draws=0;
      const draw=ctx.drawImage.bind(ctx);ctx.drawImage=(...args)=>{draws++;return draw(...args);};
      battlefield.frame=function(...args){const now=performance.now();original.apply(this,args);frames.push(performance.now()-now);};
      const measure=async()=>{frames.length=0;draws=0;await new Promise(r=>setTimeout(r,1600));const times=[...frames].sort((a,b)=>a-b);return {frames:times.length,draws,p50:times[Math.floor(times.length*.5)]??0,p95:times[Math.floor(times.length*.95)]??0};};
      try{
        const idle=await measure();$('pause').hidden=false;const covered=await measure();$('pause').hidden=true;
        return {idle,covered,units:state.units.length,cells:state.cells.length,visibleUnits:battlefield.visibleUnits?.length??null,visibleCells:battlefield.visibleCells?.length??null};
      }finally{battlefield.frame=original;ctx.drawImage=draw;}
    });
    let requests=0,bytes=0;const bodies=[];const collect=r=>{if(r.url().includes('/api/action')){requests++;bodies.push(r.body().then(b=>{bytes+=b.length;}));}};
    page.on('response',collect);
    report.turn=await page.evaluate(async()=>{
      settings.skipTurnFast=true;
      const now=performance.now(),original=battlefield.frame;let skippedFrames=0;
      battlefield.frame=function(...args){if(skipAi)skippedFrames++;return original.apply(this,args);};
      try{window.perfTurn=act('end');
        while(!aiRunning&&busy)await new Promise(r=>setTimeout(r,5));
        if(aiRunning)$('skip-ai').click();await window.perfTurn;
        return {ms:performance.now()-now,skippedFrames,revision:state.revision,round:state.round,phase:state.phase,dialogue:state.dialogue,active:state.activeFaction,player:state.playerFaction};
      }finally{battlefield.frame=original;}
    });
    page.off('response',collect);await Promise.all(bodies);report.turn.requests=requests;report.turn.bytes=bytes;
    check(report.turn.active===report.turn.player||report.turn.phase==='Finished'||report.turn.dialogue,'AI stops at interaction boundary');
    if(phase==='after'){
      check(await page.evaluate(async()=>{const latest=await rpc('action',{op:'state'});return JSON.stringify(latest.cells)===JSON.stringify(state.cells)&&latest.revision===state.revision;}),'final delta reconstruction equals full backend state');
      check(report.render.covered.frames<=1,'covered battlefield stops repainting');
      check(report.turn.skippedFrames===0,'skipped intermediate actions do not repaint battlefield');
      const gpu=await page.evaluate(()=>!!ctx.gpu);
      check(report.render.idle.frames<=(gpu?115:52),'animated idle scene respects renderer frame schedule');
      if(gpu)check(report.render.idle.frames>52,'Pixi animated scene exceeds old 30 FPS cap');
      report.cache=await page.evaluate(()=>{
        const scene={draw:ctx,state,map:manifest.maps[state.map],visibleRect:battlefield.visibleRect,zoom:view.zoom,showGrid:false,selection:null,selected:null,selectedAt:0,motions:new Map(),facings,arrivals:new Map(),combatMotions:new Map()};
        battlefield.frame(scene,500);const canvas=battlefield.staticCanvas,key=battlefield.groundSignature;
        battlefield.frame({...scene,state:{...state,units:state.units.map(u=>({...u,health:u.health-1}))}},500);
        const reused=battlefield.staticCanvas===canvas&&battlefield.groundSignature===key;
        const cells=state.cells.map((c,i)=>i===state.units[0].cell?{...c,owner:null}:c);
        battlefield.frame({...scene,state:{...state,cells}},500);const invalidated=battlefield.groundSignature!==key;
        battlefield.frame(scene,500);return {reused,invalidated};
      });
      check(report.cache.reused&&report.cache.invalidated,'ground cache tracks ground changes');
      report.lifecycle=await page.evaluate(async()=>{
        let count=0;const original=battlefield.frame;battlefield.frame=function(...args){count++;return original.apply(this,args);};
        try{window.ew4SetAppVisible(false);await new Promise(r=>setTimeout(r,200));const paused=count;
          window.ew4SetAppVisible(true);await new Promise(r=>setTimeout(r,200));return {paused,resumed:count>paused};
        }finally{battlefield.frame=original;}
      });
      check(report.lifecycle.paused===0&&report.lifecycle.resumed,'native pause/resume stops and wakes rendering');
      report.staticFrames=await page.evaluate(async()=>{
        const saved=state,original=battlefield.frame;let count=0;
        try{
          state={...state,units:[],cells:state.cells.map(c=>({...c,objective:false,objectiveMarker:0,buildingKind:null}))};requestRender();
          await new Promise(r=>setTimeout(r,150));battlefield.frame=function(...args){count++;return original.apply(this,args);};
          await new Promise(r=>setTimeout(r,250));return count;
        }finally{state=saved;battlefield.frame=original;requestRender();}
      });
      check(report.staticFrames===0,'scene without visible animation becomes idle');
      for(const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
        await page.setViewportSize(size);await page.waitForTimeout(200);
        check(await page.evaluate(()=>{const d=ctx.gpu?ctx.renderer.extract.pixels(ctx.stage):ctx.getImageData(0,0,canvas.width,canvas.height).data;const colors=new Set();for(let i=0;i<d.length;i+=4*397)colors.add(d[i]+','+d[i+1]+','+d[i+2]);return colors.size>20;}),'nonblank '+size.width);
        await page.screenshot({path:folder+'/'+phase+'-'+size.width+'.png'});
      }
      await page.mouse.move(200,200);await page.mouse.wheel(0,150);await page.waitForTimeout(150);
      check(await page.evaluate(()=>battlefield.zoom===view.zoom),'wheel zoom redraws');
      const mobile=await browser.newPage({viewport:{width:844,height:390},deviceScaleFactor:3});
      try{
        await mobile.goto(base);await mobile.waitForFunction(()=>startupReady);await mobile.evaluate(()=>start('conquest1','fra'));await mobile.waitForTimeout(200);
        report.pixels=await mobile.evaluate(()=>({dpr:viewport.dpr,pixels:canvas.width*canvas.height}));
        check(report.pixels.dpr<=3&&report.pixels.pixels<=6*1024*1024,'high-DPI map respects current resolution budget');
        await mobile.screenshot({path:folder+'/after-dpr3.png'});
      }finally{await mobile.close();}
    }
    check(report.errors.length===0,'no browser errors');
    await fs.writeFile(folder+'/'+phase+'.json',JSON.stringify(report,null,2));console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
