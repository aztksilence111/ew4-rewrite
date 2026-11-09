'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56226',out=path.resolve(process.argv[3]||'build/ai-mobile-20261109/render'),before=process.argv[4]==='before';
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],samples:[],scope:'Desktop Edge touch emulation; synthetic late-game army, redraw counts and JS submission cost, no device temperature claim'};
  const check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try{
    for(const compat of [false,true]){
      const context=await browser.newContext({hasTouch:true,viewport:{width:844,height:390},deviceScaleFactor:2}),page=await context.newPage();
      await page.route(/\/compat\/[^/]+\.js(?:\?|$)/,async route=>{
        const name=new URL(route.request().url()).pathname.split('/').pop();
        if(compat&&!before)return route.continue();
        let body=await fs.readFile(path.resolve('rewrite/Ew4.Web/wwwroot',name),'utf8');
        if(before&&name==='app.js')body=body.replace(/  \/\/ Idle flags[\s\S]*?(?=  renderDirty=false;lastRenderTime=time;)/,'');
        return route.fulfill({body,contentType:'text/javascript'});
      });
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>!busy&&[...images.values()].every(i=>i.complete));
      await page.evaluate(async()=>{
        const original=state,unit=state.units.find(u=>u.owner===state.playerFaction);
        await selectCell(unit.cell,true);view.x=point(unit.cell).x;view.y=point(unit.cell).y;
        const cells=state.cells.flatMap((c,i)=>c.active&&!c.sea?[i]:[]).slice(0,1500);
        state={...state,units:[...state.units,...cells.map((cell,i)=>({...unit,id:'render.fixture.'+i,cell,commanderId:null}))]};
        window.renderFixture={original,frames:0,cost:0,frame:battlefield.frame};
        battlefield.frame=function(...args){const t=performance.now();const value=renderFixture.frame.apply(this,args);renderFixture.frames++;renderFixture.cost+=performance.now()-t;return value;};
        requestRender();
      });
      check(await page.evaluate(()=>state.units.length>=1500),'late-game fixture has at least 1500 units');
      const sample=async(label)=>page.evaluate(async label=>{
        renderFixture.frames=0;renderFixture.cost=0;const started=performance.now();
        requestRender();await new Promise(resolve=>setTimeout(resolve,1600));
        return {label,frames:renderFixture.frames,elapsedMs:performance.now()-started,jsDrawMs:renderFixture.cost};
      },label);
      // A selected unit keeps the scene animated without moving the camera.
      const idle=await sample((compat?'compat':'source')+'-idle');report.samples.push(idle);
      if(!before)check(idle.frames/idle.elapsedMs*1000<=34&&idle.frames>=12,'touch idle redraws at most 30 Hz');
      await page.evaluate(()=>{cameraInputUntil=performance.now()+2200;});
      const active=await sample((compat?'compat':'source')+'-camera');report.samples.push(active);
      if(!before)check(active.frames>idle.frames*1.25,'camera interaction retains a higher redraw rate');
      const immediate=await page.evaluate(()=>{suspendRendering();lastRenderTime=performance.now();renderDirty=true;const n=renderFixture.frames;render(lastRenderTime+.1);return renderFixture.frames===n+1;});
      check(immediate,'input and state changes render immediately');
      const forced=await page.evaluate(()=>{suspendRendering();lastRenderTime=performance.now();renderDirty=false;const n=renderFixture.frames;render(lastRenderTime+.1,true);return renderFixture.frames===n+1;});
      check(forced,'forced frame bypasses idle budget');
      const hidden=await page.evaluate(()=>{ew4SetAppVisible(false);const n=renderFixture.frames;render(performance.now()+100);return renderFixture.frames===n&&renderRequest===0;});
      check(hidden,'hidden application stops rendering');
      await page.evaluate(()=>{ew4SetAppVisible(true);cameraInputUntil=0;requestRender();});
      await page.waitForFunction(()=>!battlefieldNeedsFrame);
      check(await page.evaluate(()=>canvas.width>0&&canvas.height>0&&battlefield.preparedState===state),'resumed surface refreshes current state');
      await page.setViewportSize({width:390,height:844});await page.waitForFunction(()=>viewport.w===390);
      check(await page.evaluate(()=>viewport.h===844),'portrait resize refreshes immediately');
      await page.screenshot({path:path.join(out,(compat?'compat':'source')+'-portrait.png')});
      if(!before){
        const highRefresh=await page.evaluate(()=>{
          const original=battlefield.frame,start=performance.now();let count=0;
          battlefield.frame=function(){count++;this.animated=true;};
          const simulate=active=>{
            count=0;lastRenderTime=-Infinity;renderDirty=false;cameraInputUntil=active?start+10000:0;
            for(let i=0;i<120;i++){render(start+i*1000/120);suspendRendering();}
            return count;
          };
          try{return {idle:simulate(false),active:simulate(true)};}
          finally{battlefield.frame=original;cameraInputUntil=0;lastRenderTime=-Infinity;requestRender();}
        });
        check(highRefresh.idle===30&&highRefresh.active===60,'120 Hz display follows 30/60 Hz rendering budget');
        report.samples.push({label:(compat?'compat':'source')+'-simulated120Hz',...highRefresh});
      }
      await page.evaluate(()=>{battlefield.frame=renderFixture.frame;state=renderFixture.original;requestRender();});
      if(compat&&!before){
        await page.evaluate(()=>start('campaign1_02b',null));
        await page.evaluate(()=>selectCell(2544,true));
        check(await page.evaluate(()=>selection.targets.length>0),'real naval battle has a legal target');
        const health=await page.evaluate(()=>state.units.reduce((n,u)=>n+u.health,0));
        await page.evaluate(()=>{window.mobileCombat=act('attack',{unit:selected.id,target:state.units.find(u=>u.cell===selection.targets[0]).id});});
        await page.waitForFunction(()=>combatMotions.size>0&&effects.instances.length>0);
        check(true,'real combat motion and effects remain active');
        await page.evaluate(()=>mobileCombat);
        check(await page.evaluate(h=>state.units.reduce((n,u)=>n+u.health,0)<h,health),'real combat commits damage');
      }
      await context.close();
    }
    check(report.errors.length===0&&report.missing.length===0,'no script or asset errors');
  }catch(e){report.failure=e.message;throw e;}
  finally{await browser.close();await fs.writeFile(path.join(out,'results.json'),JSON.stringify(report,null,2));}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
