'use strict';
const assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5588';
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={cases:[],errors:[]};
  try{
    for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      const context=await browser.newContext({viewport,deviceScaleFactor:2}),page=await context.newPage();
      page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest1','fra'));await page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition);
      await page.evaluate(async()=>{
        window.textureRound=state.round;
        const units=state.units.filter(u=>u.owner===state.playerFaction&&u.canMove&&u.category!=='naval');
        window.textureUnit=units[0].id;
        await act('player_ai_config',{offer:'policy',policy:'free'});
        await act('player_ai_config',{offer:'units',units:units.map(u=>u.id)});
        await act('player_ai_config',{offer:'goal',cell:units[0].cell});
        await rpc('save',{slot:2,mode:2});
        await act('player_ai_start');
        if(!state.playerAi.enabled)throw Error('AI task did not start');
        await rpc('save',{slot:2,mode:2});
        const unit=state.units.find(u=>u.id===window.textureUnit),p=point(unit.cell);
        view.x=p.x;view.y=p.y;view.zoom=1.3;clampView();requestRender();
      });
      await page.waitForTimeout(150);
      const pos=await page.evaluate(()=>{const p=point(state.units.find(u=>u.id===window.textureUnit).cell);return {x:(p.x-view.x)*view.zoom+viewport.w/2,y:(p.y-view.y)*view.zoom+viewport.h/2};});
      await page.mouse.click(pos.x,pos.y);await page.waitForFunction(()=>!!selected);
      const result=await page.evaluate(async()=>{
        const measure=()=>{render(performance.now(),true);suspendRendering();const pixels=ctx.renderer.extract.pixels(),colors=new Set();for(let i=0;i<pixels.length;i+=4*97)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);return colors.size;};
        const snapshot=()=>JSON.stringify({units:state.units.map(u=>[u.id,u.owner,u.cell,u.health]),sites:state.cells.map(c=>[c.owner,c.buildingKind,c.buildingLevel]),factions:state.factions.map(f=>[f.id,f.resources])});
        const before=measure(),unit=selected.id,saved=snapshot();
        await installState(await rpc('load',{slot:2,mode:2}),true);const after=measure();
        const intact=snapshot()===saved;
        const actual=state.units.find(u=>u.id===unit);
        // Exercise the bounded health-ring cache while an earlier ring is queued in the same frame.
        const draw=new Ew4Pixi.Context(document.createElement('canvas')),rings=new Ew4Battlefield.Renderer({});rings.draw=draw;
        for(let i=1;i<=256;i++)rings.healthArc(i,300);
        const old=rings.healthArc(1,300);
        draw.begin(32,32);draw.drawImage(old,0,0,24,24);
        const oldEntry=draw.sources.get(old);draw.drawImage(rings.healthArc(257,300),0,0,24,24);
        const aliveUntilEnd=!oldEntry.base.destroyed;draw.end();const retiredAfterEnd=oldEntry.base.destroyed;
        draw.renderer.destroy(true);
        return {gpu:!!ctx.gpu,before,after,intact,round:state.round,expectedRound:window.textureRound,ai:actual.aiControlled,enabled:state.playerAi.enabled,aliveUntilEnd,retiredAfterEnd};
      });
      assert(result.gpu&&result.before>100&&result.after>100,JSON.stringify(result));
      assert(result.intact&&result.round===result.expectedRound&&result.ai&&result.enabled,JSON.stringify(result));
      assert(result.aliveUntilEnd&&result.retiredAfterEnd,'health-ring eviction must preserve already queued sprites');
      report.cases.push({viewport,...result});await context.close();
    }
    assert.deepEqual(report.errors,[]);console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
