'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const {createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright');
const folder=process.env.EW4_RENDER_REPORT||'E:/Workplace/game/o4mod/rewrite/reports/custom-mechanics/followup';
const base=process.env.EW4_PERF_URL||'http://127.0.0.1:5185';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  await page.route('**/*',r=>r.continue());
  const report={checks:0,errors:[],missing:[],screenshots:[],scope:base+' isolated profile, Edge headless on this machine; no device FPS claim'};
  const check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const shot=async name=>{await page.screenshot({path:folder+'/'+name+'.png'});report.screenshots.push(name);};
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>!!catalog&&!!originalTextRenderer);
    await page.evaluate(()=>start('conquest1','fra'));await page.waitForTimeout(350);
    report.relations=await page.evaluate(()=>({ally:state.factions.filter(f=>f.id!==state.playerFaction&&f.camp===state.factions.find(p=>p.id===state.playerFaction).camp).every(f=>Ew4Battlefield.badgeColor(state,f.id)==='blue'),neutral:state.factions.filter(f=>f.neutral).map(f=>({country:f.country,color:Ew4Battlefield.badgeColor(state,f.id)}))}));
    check(report.relations.ally,'all allied countries use blue badges');
    check(report.relations.neutral.length===4&&report.relations.neutral.every(f=>f.color==='black'),'all four neutral countries use black badges');
    check(await page.evaluate(()=>{const player=state.factions.find(f=>f.id===state.playerFaction);return state.factions.filter(f=>!f.neutral&&f.camp!==player.camp).every(f=>Ew4Battlefield.badgeColor(state,f.id)==='red');}),'enemy countries use red badges');
    for(const [country,name] of [['che','neutral-switzerland'],['koi','allied-milan']]){
      await page.evaluate(async country=>{const faction=state.factions.find(f=>f.country===country);const u=state.units.find(u=>u.owner===faction.id);await selectCell(u.cell,true);view.x=point(u.cell).x;view.y=point(u.cell).y;},country);
      await page.waitForTimeout(100);await shot(name);
      if(country==='che')check(await page.evaluate(()=>Ew4Battlefield.selectionHostile(state,selection.cell)),'neutral selection is red while badge remains black');
    }
    await page.evaluate(()=>{centerPlayer();selected=undefined;selection=undefined;document.querySelector('#selection').hidden=true;});
    await page.waitForTimeout(250);
    report.cache=await page.evaluate(async()=>{
      const wait=()=>new Promise(requestAnimationFrame),waitFrames=async()=>{requestRender();await wait();await wait();};
      const key=battlefield.staticKey;await waitFrames();const reused=key===battlefield.staticKey;
      const builds=battlefield.staticBuilds;view.x+=48;await waitFrames();const panned=builds===battlefield.staticBuilds;
      const panKey=battlefield.staticKey;view.zoom*=.9;await waitFrames();const zoomed=panKey!==battlefield.staticKey;
      const old=state;state={...state,cells:state.cells.map((c,i)=>i===state.units[0].cell?{...c,owner:state.playerFaction}:c)};
      await waitFrames();const changed=battlefield.staticState===state;
      state=old;centerPlayer();await waitFrames();
      return {reused,panned,zoomed,changed,unitPixels:battlefield.unitFramePixels};
    });
    for(const key of ['reused','panned','zoomed','changed'])check(report.cache[key],'static cache correctness: '+key);
    check(report.cache.unitPixels<=8*1024*1024,'unit frame cache remains within pixel budget');
    report.culling=await page.evaluate(()=>{
      let textures=0;const fx=new Ew4Effects.Effects({...effects.api,volume:()=>0},effects.data);
      fx.texture=()=>{textures++;return document.createElement('canvas');};
      fx.effect('effect_strike4.xml',1e6,1e6);fx.instances[0].started=0;
      fx.frame(ctx,100,'foreground',battlefield.visibleRect);const alive=fx.instances.length>0;
      fx.frame(ctx,100000,'foreground',battlefield.visibleRect);
      return {textures,alive,expired:fx.instances.length===0};
    });
    check(report.culling.textures===0&&report.culling.alive&&report.culling.expired,'offscreen particles skip textures and expire on their original timeline');
    report.visual=await page.evaluate(()=>{
      const directGround=battlefield.staticGround,directUnits=battlefield.unitAnimation,cachedText=battlefield.mapText;
      function capture(){ctx.setTransform(viewport.dpr,0,0,viewport.dpr,0,0);ctx.fillStyle='#688e91';ctx.fillRect(0,0,viewport.w,viewport.h);ctx.translate(viewport.w/2,viewport.h/2);ctx.scale(view.zoom,view.zoom);ctx.translate(-view.x,-view.y);battlefield.frame({draw:ctx,state,map:manifest.maps[state.map],visibleRect:battlefield.visibleRect,zoom:view.zoom,showGrid:false,selection:null,selected:null,selectedAt:0,motions:new Map(),facings,arrivals:new Map(),combatMotions:new Map()},500);return ctx.getImageData(0,0,canvas.width,canvas.height).data;}
      const cached=capture();
      battlefield.staticGround=function(){this.ground();this.facilities(true);this.terrain(false);this.ownership();this.mapTiles();this.mapText();this.borders();this.terrain(true);this.facilities(false);};
      battlefield.unitAnimation=function(lib,item,tick){this.api.animation(lib,item,tick);};
      battlefield.mapText=function(){const lib=this.api.libraries.get('maptext');this.draw.save();this.draw.globalAlpha*=60/255;for(const label of this.map.labels||[]){const p={x:Number(label.tx),y:Number(label.ty)};if(!this.visible(p,Math.max(Number(label.width),Number(label.height))))continue;this.draw.save();this.draw.translate(p.x,p.y);this.api.animation(lib,lib.byName.get(label.libName),0,0,this.draw);this.draw.restore();}this.draw.restore();};
      let raw;try{raw=capture();}finally{battlefield.staticGround=directGround;battlefield.unitAnimation=directUnits;battlefield.mapText=cachedText;}
      let error=0,changed=0;for(let i=0;i<raw.length;i+=4){let delta=0;for(let c=0;c<3;c++)delta+=Math.abs(raw[i+c]-cached[i+c]);error+=delta;if(delta>90)changed++;}
      return {meanChannelError:error/(raw.length/4*3),stronglyChangedFraction:changed/(raw.length/4)};
    });
    check(report.visual.meanChannelError<3&&report.visual.stronglyChangedFraction<.025,'cached scene preserves the directly rendered image');
    await page.waitForTimeout(200);
    report.performance=await page.evaluate(async()=>{
      const durations=[],intervals=[];let calls=0;const original=battlefield.frame,drawImage=ctx.drawImage.bind(ctx);
      battlefield.frame=function(...args){const t=performance.now();original.apply(this,args);durations.push(performance.now()-t);};
      ctx.drawImage=(...args)=>{calls++;return drawImage(...args);};
      const stats=a=>({mean:a.reduce((s,v)=>s+v,0)/a.length,p95:[...a].sort((a,b)=>a-b)[Math.floor(a.length*.95)]});
      const measure=async count=>{durations.length=0;intervals.length=0;calls=0;let last=await new Promise(requestAnimationFrame);for(let i=0;i<count;i++){const now=await new Promise(requestAnimationFrame);intervals.push(now-last);last=now;}return {frame:stats(durations),interval:stats(intervals),draws:calls/count};};
      try{const idle=await measure(90);effects.timeline('strike4',view.x,view.y);const impact=await measure(60);return {idle,impact};}
      finally{battlefield.frame=original;ctx.drawImage=drawImage;}
    });
    check(report.performance.idle.draws<400,'steady scene uses fewer than 400 main-canvas draw calls');
    await shot('cached-battlefield');
    for(const size of [{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(size);await page.waitForTimeout(120);
      check(await page.evaluate(()=>battlefield.staticCanvas.width>=canvas.width&&battlefield.staticCanvas.height>=canvas.height&&battlefield.staticCanvas.width*battlefield.staticCanvas.height<=10*1024*1024),'static cache covers viewport within its pixel budget at '+size.width);
      check(await page.locator('#map').evaluate(c=>{const d=c.getContext('2d').getImageData(0,0,c.width,c.height).data;const s=new Set();for(let i=0;i<d.length;i+=4*397)s.add(d[i]+','+d[i+1]+','+d[i+2]);return s.size>20;}),'nonblank battlefield at '+size.width);await shot('cached-'+size.width);
    }
    await page.setViewportSize({width:1280,height:720});
    await page.evaluate(()=>start('campaign1_02b',null));
    await page.evaluate(async()=>{await selectCell(2544,true);view.x=point(2544).x;view.y=point(2544).y;});
    check(await page.evaluate(()=>selection.targets.length>0),'real naval battle has a legal target');
    const health=await page.evaluate(()=>state.units.reduce((n,u)=>n+u.health,0));
    await page.evaluate(()=>{window.testCombat=act('attack',{unit:selected.id,target:state.units.find(u=>u.cell===selection.targets[0]).id});});
    await page.waitForTimeout(200);
    check(await page.evaluate(()=>combatMotions.size>0&&effects.instances.length>0),'real combat animation and particle effects remain active with caching');
    await shot('cached-real-combat');
    await page.evaluate(()=>window.testCombat);
    check(await page.evaluate(old=>state.units.reduce((n,u)=>n+u.health,0)<old,health),'real combat commits damage');
    await page.waitForFunction(()=>combatMotions.size===0);
    check(await page.evaluate(()=>battlefield.staticState===state),'combat result refreshes cached battlefield');
    check(report.errors.length===0&&report.missing.length===0,'no script or resource failures');
  }catch(e){report.failure=e.message;throw e;}
  finally{await fs.writeFile(folder+'/render-verification.json',JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
