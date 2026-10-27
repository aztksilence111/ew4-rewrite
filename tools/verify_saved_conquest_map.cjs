'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5588',fixed=process.argv.includes('--fixed');
// The endpoint must use an isolated copy: loading autosaves and slot 6 is overwritten.
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={renderer:fixed?'fixed':'formal-0.2.23',cases:[],errors:[]};
  try{
    for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      const context=await browser.newContext({viewport,deviceScaleFactor:2}),page=await context.newPage();
      page.on('pageerror',e=>report.errors.push(e.stack));
      if(fixed)await page.route(/\/pixi-battle\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',body:fs.readFileSync(path.join(__dirname,'../rewrite/Ew4.Web/wwwroot/pixi-battle.js'),'utf8')}));
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      const result=await page.evaluate(async()=>{
        const signature=()=>JSON.stringify({round:state.round,units:state.units.map(u=>[u.id,u.owner,u.cell,u.health]),cells:state.cells.map(c=>[c.owner,c.buildingKind,c.buildingLevel]),factions:state.factions.map(f=>[f.id,f.resources]),ai:state.playerAi});
        const measure=()=>{render(performance.now(),true);suspendRendering();const a=ctx.renderer.extract.pixels(),colors=new Set();for(let i=0;i<a.length;i+=4*97)colors.add(a[i]+','+a[i+1]+','+a[i+2]);return colors.size;};
        const loadCopy=async(slot,mode)=>{await installState(await rpc('load',{slot,mode}),true);suspendRendering();};
        try{
          await loadCopy(1,2);
          const original=signature(),resources=state.factions.find(f=>f.id===state.playerFaction).resources;
          const playerUnits=state.units.filter(u=>u.owner===state.playerFaction).length,tasks=state.playerAi.tasks.length;
          const initialColors=measure();let frames=0,minColors=initialColors;
          await Promise.all([...new Set(state.units.map(u=>visualProfile(u)?.library).filter(Boolean))].map(loadLibrary));
          for(let cycle=0;cycle<4;cycle++){
            const units=state.units.filter(u=>u.owner===state.playerFaction);
            for(const zoom of [.45,1,2])for(const unit of units.filter((_,i)=>i%5===0)){
              const p=point(unit.cell);view.x=p.x;view.y=p.y;view.zoom=zoom;clampView();
              minColors=Math.min(minColors,measure());frames++;
            }
            const unit=units[cycle],p=point(unit.cell);view.x=p.x;view.y=p.y;view.zoom=1.3;clampView();measure();
            await selectCell(unit.cell);if(selected?.id!==unit.id)throw Error('Saved unit cannot be selected');
            await act('player_ai_stop');await act('player_ai_start');
            if(signature()!==original)throw Error('AI execute changed the saved battle');
            await rpc('save',{slot:6,mode:2});await loadCopy(6,2);
            if(signature()!==original)throw Error('Save/reload changed the saved battle');
            await loadCopy(0,1);measure();await loadCopy(1,2);measure();
            if(signature()!==original)throw Error('Scenario switch changed the saved battle');
          }
          return {ok:true,round:state.round,playerUnits,tasks,resources,initialColors,minColors,frames,stats:ctx.stats};
        }catch(e){suspendRendering();return {ok:false,error:e.stack,stats:ctx?.stats};}
      });
      report.cases.push({viewport,...result});await context.close();
    }
    console.log(JSON.stringify(report));
    assert.deepEqual(report.errors,[]);assert(report.cases.every(c=>c.ok&&c.round===9&&c.playerUnits===31&&c.tasks===4&&c.minColors>100));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
