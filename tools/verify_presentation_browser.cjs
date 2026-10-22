'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5381',folder=path.resolve(process.argv[3]||path.join(__dirname,'../build/presentation-restoration-20261022'));
(async()=>{
  const report={checks:0,errors:[],missing:[],screenshots:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    await page.evaluate(()=>start('conquest1','fra'));
    report.move=await page.evaluate(async()=>{
      settings.speed=5;window.presentationSounds=[];effects.sound=file=>{if(file)presentationSounds.push(file);};
      for(const unit of state.units.filter(u=>u.owner===state.playerFaction)){
        await selectCell(unit.cell,true);
        const cell=selection.range.find(c=>c!==unit.cell&&!state.units.some(u=>u.cell===c));if(cell==null)continue;
        presentationSounds.length=0;await act('move',{unit:unit.id,cell});const move=[...presentationSounds];
        await $('undo-move').onclick();return {move,undo:presentationSounds.slice(move.length)};
      }throw Error('movable fixture missing');
    });
    check(report.move.move.filter(s=>/move|sfx_leg|sfx_naval/.test(s)).length===1,'real move plays one movement cue');
    check(report.move.undo.length===0,'instant undo plays no action cues');
    report.recruit=await page.evaluate(async()=>{
      const candidates=[];
      for(const unit of state.units.filter(u=>u.owner===state.playerFaction&&!u.commanderId)){
        await selectCell(unit.cell,true);if(selection.disband){await act('disband',{unit:unit.id});break;}
      }
      for(let cell=0;cell<state.cells.length;cell++){
        if(state.cells[cell].owner!==state.playerFaction||state.units.some(u=>u.cell===cell)||!state.cells[cell].buildingKind)continue;
        await selectCell(cell,true);const resources=state.factions.find(f=>f.id===state.playerFaction).resources;
        candidates.push({cell,resources,offers:selection.offers.slice(0,2)});
        const offer=selection.offers.find(o=>['money','industry','food'].every(key=>(o.cost[key]||0)<=resources[key])&&manifest.profiles[o.profile]?.stats.type!=='fort');if(!offer)continue;
        presentationSounds.length=0;const previous=state.units.length;await act('recruit',{cell,offer:offer.id});
        if(state.units.length>previous)return {sounds:[...presentationSounds],notices:state.notices.filter(n=>n.kind==='unit.recruited')};
      }throw Error('recruit fixture missing '+JSON.stringify(candidates.slice(0,3)));
    });
    check(report.recruit.sounds.filter(s=>s==='sfx_draft.wav').length===1&&report.recruit.notices.length>0,'real draft plays once from recruit notice');
    report.events=await page.evaluate(async()=>{
      selected=undefined;selection=undefined;
      const original=state,unit=state.units.find(u=>u.owner===state.playerFaction),fortProfile=Object.keys(manifest.profiles).find(k=>manifest.profiles[k].stats.type==='fort');
      const run=async(next)=>{presentationSounds.length=0;const created=[],effect=effects.effect.bind(effects),wait=presentationDelay;
        effects.effect=(name,...args)=>{created.push(name);return effect(name,...args);};presentationDelay=async()=>{};
        try{await installState(next);}finally{effects.effect=effect;presentationDelay=wait;}return {sounds:[...presentationSounds],created};};
      const fort={...unit,id:'fixture-fort',profile:fortProfile,category:'fort',constructionTurns:1};
      const fortResult=await run({...original,units:[...original.units,fort],recruited:[fort.id],notices:[{kind:'unit.recruited',subject:fort.id}]});
      const eventResult=await run({...original,units:[...original.units,{...unit,id:'fixture-event'}],recruited:['fixture-event'],notices:[]});
      state={...original,units:original.units.map(u=>u.id===unit.id?{...u,health:Math.max(1,u.health-10)}:u)};
      const supply=await run({...original,revision:original.revision+1,suppliedUnit:unit.id,recruited:[],notices:[]});
      const reload=await run({...original,suppliedUnit:null,recruited:[],notices:[]});
      state=original;requestRender();return {fort:fortResult,event:eventResult,supply,reload};
    });
    check(!report.events.fort.sounds.includes('sfx_draft.wav')&&!report.events.fort.created.includes('effect_recruit.xml'),'fort construction bypasses recruit arrival cues');
    check(!report.events.event.sounds.includes('sfx_draft.wav'),'event spawn bypasses ordinary draft sound');
    check(report.events.supply.sounds.filter(s=>s==='sfx_supply.wav').length===1&&report.events.supply.created.includes('effect_recover.xml'),'explicit supply notice plays heal cue');
    check(!report.events.reload.sounds.length,'ordinary state install does not replay supply cue');
    report.occupy=await page.evaluate(()=>{
      const run=(buildingKind,changed,instant=false)=>{presentationSounds.length=0;playOccupationSound({cells:[{}, {buildingKind}],notices:changed?[{kind:'cell.owner_changed',subject:'1'}]:[]},[{path:[0,1],instant}]);return [...presentationSounds];};
      return {city:run('city',true),farm:run('farmland',true),own:run('city',false),undo:run('city',true,true)};
    });
    check(report.occupy.city.join(',')==='sfx_occupy.wav'&&!report.occupy.farm.length&&!report.occupy.own.length&&!report.occupy.undo.length,'occupation cue excludes farmland, unchanged ownership and instant undo');
    await page.evaluate(()=>start('campaign1_02b',null));
    const baselineApp=await fs.readFile(path.join(folder,'baseline/app.js'),'utf8');
    const combatSource=baselineApp.slice(baselineApp.indexOf('async function playCombat('),baselineApp.indexOf('function playImpact(')).replace('async function playCombat(','async function previousPlayCombat(');
    report.combat=await page.evaluate(async source=>{
      (0,eval)(source);
      await selectCell(2544,true);const attacker=selected.id,defender=state.units.find(u=>u.cell===selection.targets[0]).id;
      const input={attacker,defender,damage:20,counterattack:true,counterDamage:10},wait=presentationDelay,timeline=effects.timeline,add=damageNumbers.add;
      const run=async fn=>{const waits=[],salvos=[],bodies=[];combatMotions.clear();const set=combatMotions.set.bind(combatMotions);
        combatMotions.set=(id,m)=>{bodies.push({id,delay:m.delay,segments:JSON.parse(JSON.stringify(m.segments))});return set(id,m);};
        presentationDelay=async ms=>{waits.push(ms);};effects.timeline=(name,x,y,o)=>{if(o?.started!=null)salvos.push({name,started:o.started});};damageNumbers.add=()=>{};
        try{await fn(input);}finally{combatMotions.set=set;}const start=Math.min(...salvos.map(s=>s.started));
        return {waits,salvos:salvos.map(s=>({name:s.name,delay:s.started-start})),bodies};};
      try{return {before:await run(previousPlayCombat),after:await run(playCombat)};}
      finally{presentationDelay=wait;effects.timeline=timeline;damageNumbers.add=add;combatMotions.clear();effects.clear();}
    },combatSource);
    check(JSON.stringify(report.combat.before.salvos)===JSON.stringify(report.combat.after.salvos),'actual attack and counterattack firing schedule unchanged');
    check(report.combat.before.waits.length===report.combat.after.waits.length&&report.combat.before.waits.every((v,i)=>Math.abs(v-report.combat.after.waits[i])<20),'actual impact and blocking waits unchanged within execution overhead');
    report.smoke=await page.evaluate(async()=>{
      const original=state,u=state.units[0],profileId=Object.keys(manifest.profiles).find(k=>manifest.profiles[k].stats.type==='fort');await loadLibrary(manifest.profiles[profileId].library);
      const r=new Ew4Battlefield.Renderer(battlefield.api),canvas=document.createElement('canvas');canvas.width=300;canvas.height=300;const draw=canvas.getContext('2d'),p=point(u.cell),calls=[];
      r.staticGround=r.objectives=r.flags=r.unitMarkers=r.labels=r.highlights=()=>{};
      const element=effects.element;effects.element=function(name,...args){calls.push(name);return element.call(this,name,...args);};
      try{for(const [category,health,dead] of [['fort',320,false],['fort',240,false],['fort',160,false],['fort',80,false],['fort',0,false],['infantry',80,false],['fort',80,true]]){
        draw.setTransform(1,0,0,1,150-p.x,150-p.y);
        r.frame({draw,state:{...original,units:[{...u,category,profile:profileId,maxHealth:320,health,burning:0}]},map:manifest.maps[state.map],visibleRect:{left:p.x-150,top:p.y-150,width:300,height:300},zoom:1,motions:new Map(),facings,arrivals:new Map(),deaths:dead?new Map([[u.id,{started:0,duration:500}]]):new Map(),combatMotions:new Map(),selection:null},100);
      }}finally{effects.element=element;}return calls;
    });
    check(report.smoke.join(',')==='damaged 1,damaged 2,damaged 3','only living damaged forts receive correct smoke grade');
    const previousEffects=await fs.readFile(path.join(folder,'baseline/effects.js'),'utf8');
    report.cost=await page.evaluate(source=>{
      const current=Ew4Effects;(0,eval)(source);const Before=Ew4Effects.Effects;globalThis.Ew4Effects=current;
      const canvas=document.createElement('canvas');canvas.width=400;canvas.height=300;const draw=canvas.getContext('2d');
      const run=Type=>{const fx=new Type({...effects.api,volume:()=>0},effects.data),frames=[];let peak=0;
        for(let frame=0;frame<120;frame++){
          if(frame%30===0)for(let i=0;i<6;i++){fx.effect('effect_gunfire.xml',90+i*35,150);fx.instances.at(-1).started=frame/60*1000;}
          const start=performance.now();fx.frame(draw,frame/60*1000);frames.push(performance.now()-start);
          peak=Math.max(peak,fx.instances.reduce((n,i)=>n+i.particles.length,0));
        }return {peak,meanMs:frames.reduce((a,b)=>a+b,0)/frames.length,p95Ms:frames.sort((a,b)=>a-b)[Math.floor(frames.length*.95)]};};
      run(Before);run(current.Effects);return {before:run(Before),after:run(current.Effects),scope:'same 24 gunfire emitters / 120 fixed frames; local Edge, no phone FPS claim'};
    },previousEffects);
    check(report.cost.after.peak<report.cost.before.peak,'same workload reduces peak particle count');
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.evaluate(()=>{effects.clear();effects.timeline('strike4',view.x,view.y);requestRender();});await page.waitForTimeout(120);
      check(await page.locator('#map').evaluate(c=>{const pixels=c.getContext('2d').getImageData(0,0,c.width,c.height).data,colors=new Set();for(let i=0;i<pixels.length;i+=4*397)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);return colors.size>20;}),'nonblank battle pixels '+width);
      const name='presentation-'+width+'.png';await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);
    }
    await page.route('**/effects.json',async route=>{
      const response=await route.fetch(),old=await response.json();delete old.elements;
      old.sounds=old.sounds.filter(s=>!['sfx_leg.wav','sfx_cavalrymove.wav','sfx_armourmove.wav','sfx_naval.wav','sfx_draft.wav','sfx_occupy.wav','sfx_supply.wav'].includes(s));
      await route.fulfill({response,json:old});
    });
    await page.reload();await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    check(await page.evaluate(()=>effects.data.elements['damaged 3'].length===3&&effects.data.sounds.includes('sfx_draft.wav')&&effects.audio.has('sfx_leg.wav')),'old Mod effects receive presentation defaults without migration');
    check(!report.errors.length&&!report.missing.length,'no browser exceptions or missing resources');
  }catch(e){report.failure=e.message;process.exitCode=1;}
  finally{await fs.writeFile(path.join(folder,'browser.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
