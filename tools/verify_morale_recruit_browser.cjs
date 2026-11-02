'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2]||'http://127.0.0.1:57943',out=path.resolve(process.argv[3]||'build/morale-animation-20261101/browser');
const report={checks:0,errors:[],missing:[],cases:[]},check=(value,label)=>{assert.ok(value,label);report.checks++;};
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const [source,renderer,width,height,speed] of [[true,'pixi',1280,720,1],[false,'canvas',1280,720,5],[false,'pixi',844,390,3],[false,'canvas',390,844,5]]){
      const context=await browser.newContext({viewport:{width,height},hasTouch:width<1000,isMobile:width<1000}),page=await context.newPage();
      const label=[source?'source':'compat',renderer,width].join('-');
      page.on('pageerror',e=>report.errors.push(label+': '+e.message));
      page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(source)await page.route(base+'/',async route=>{const response=await route.fetch();await route.fulfill({response,body:(await response.text()).replace(/((?:src|href)=")compat\//g,'$1')});});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));
      await page.waitForFunction(()=>startupReady,null,{timeout:90000});await page.evaluate(()=>start('conquest1','fra'));
      const morale=await page.evaluate(async()=>{
        const unit=state.units.find(u=>u.owner===state.playerFaction),p=point(unit.cell),original=state;
        view.x=p.x;view.y=p.y;view.zoom=1.6;clampView();settings.speed=1;
        const next=value=>({...state,revision:state.revision+1,combat:null,movements:[],recruited:[],notices:[],units:state.units.map(u=>u.id===unit.id?{...u,morale:value}:u)});
        await installState(next(0),true);view.x=p.x;view.y=p.y;view.zoom=1.6;clampView();
        await Promise.all(['buildings','buildings_hd'].map(n=>image('images/'+n+'.png').decode()));
        await installState(next(1));
        const first=battlefield.moraleTransitions.get(unit.id),started=first.started;
        await installState({...state});const duplicate=battlefield.moraleTransitions.get(unit.id)?.started===started;
        const now=performance.now(),samples=[];
        for(const age of [50,260,600]){
          first.started=now-age;renderDirty=true;render(now,true);cancelAnimationFrame(renderRequest);renderRequest=0;
          samples.push(canvas.toDataURL());
        }
        first.started=now-700;renderDirty=true;render(now,true);cancelAnimationFrame(renderRequest);renderRequest=0;
        const completed=!battlefield.moraleTransitions.size;
        await installState(next(-1));const down=battlefield.moraleTransitions.get(unit.id)?.direction;
        await installState(next(0));const recovery=battlefield.moraleTransitions.get(unit.id);
        await installState(next(-2),true);const load=!battlefield.moraleTransitions.size;
        skipAi=true;try{await installState(next(1));}finally{skipAi=false;}const skip=!battlefield.moraleTransitions.size;
        await installState(original,true);
        return {duplicate,completed,down,recovery:recovery&&{direction:recovery.direction,from:recovery.from,to:recovery.to},load,skip,samples};
      });
      const images=morale.samples.map(data=>PNG.sync.read(Buffer.from(data.split(',')[1],'base64')).data);
      const changed=(a,b)=>{let count=0;for(let i=0;i<a.length;i+=4)if(a[i]!==b[i]||a[i+1]!==b[i+1]||a[i+2]!==b[i+2])count++;return count;};
      morale.pixels=[changed(images[0],images[1]),changed(images[1],images[2])];delete morale.samples;
      check(morale.duplicate&&morale.completed&&morale.load&&morale.skip,label+' morale lifecycle');
      check(morale.down===1&&morale.recovery.direction===-1&&!morale.recovery.to,label+' decline and recovery');
      check(morale.pixels.every(n=>n>20),label+' moving morale pixels');
      const setup=await page.evaluate(async speed=>{
        settings.speed=5;await selectCell(2083,false,true);await act('disband',{unit:selected.id});settings.speed=speed;
        const occupied=new Set(state.units.map(u=>u.cell)),funds=state.factions.find(f=>f.id===state.playerFaction).resources,candidates=[];
        for(let cell=0;cell<state.cells.length;cell++){
          const site=state.cells[cell];if(!site.active||site.owner!==state.playerFaction||!site.buildingKind||occupied.has(cell))continue;
          await selectCell(cell,false,true);
          candidates.push({cell,offers:selection.offers.map(o=>({id:o.id,cost:o.cost,library:manifest.profiles[o.profile].library})),site:site.buildingKind});
          const offer=selection.offers.find(o=>manifest.profiles[o.profile].library!=='army_fort'&&['money','industry','food'].every(key=>(o.cost[key]||0)<=funds[key]));
          if(!offer)continue;
          selection={...selection,offers:[offer]};openRecruitment(cell);view.x=point(cell).x;view.y=point(cell).y;view.zoom=1.6;clampView();
          window.recruitProbe={cell,oldCount:state.units.length,frames:[],waits:[],started:0};
          const delay=presentationDelay;presentationDelay=duration=>{recruitProbe.waits.push({duration,covered:!$('detail-modal').hidden,arrivals:arrivals.size});return delay(duration);};
          const frame=battlefield.frame; battlefield.frame=function(scene,time){const result=frame.call(this,scene,time);if(arrivals.size)recruitProbe.frames.push({elapsed:time-[...arrivals.values()][0],covered:!$('detail-modal').hidden,visible:this.visibleUnitMarkers.some(m=>arrivals.has(m.unit.id))});return result;};
          const actOriginal=act;act=async(...args)=>{recruitProbe.started=performance.now();await actOriginal(...args);recruitProbe.completed=performance.now();recruitProbe.count=state.units.length;recruitProbe.busy=busy;};
          return {cell,offer:offer.id,profile:offer.profile};
        }
        throw Error('No affordable empty recruitment site '+JSON.stringify({funds,candidates}));
      },speed);
      await page.locator('#detail-confirm').click();
      await page.waitForFunction(()=>recruitProbe.frames.length>=2,null,{timeout:15000});
      await page.screenshot({path:path.join(out,label+'-recruit.png')});
      await page.waitForFunction(()=>recruitProbe.completed,null,{timeout:15000});
      const recruitment=await page.evaluate(()=>({...recruitProbe,modalHidden:$('detail-modal').hidden,arrivals:arrivals.size,waiters:presentationWaiters.size}));
      check(recruitment.count===recruitment.oldCount+1,label+' real recruitment committed');
      check(recruitment.frames.length>=2&&recruitment.frames.every(f=>!f.covered&&f.visible),label+' recruitment actually renders while modal closed');
      check(recruitment.frames.at(-1).elapsed-recruitment.frames[0].elapsed>50,label+' multiple arrival positions');
      const expectedDuration=60/320*1000/1.15;
      check(recruitment.waits.some(w=>w.arrivals===1&&Math.abs(w.duration-expectedDuration)<.001)&&recruitment.waits.every(w=>w.duration<=expectedDuration+.001&&!w.covered),label+' native player timing without idle-loop wait');
      check(!recruitment.busy&&!recruitment.arrivals&&!recruitment.waiters&&recruitment.modalHidden,label+' input and presentation released');
      report.cases.push({label,speed,morale,setup,recruitment:{frames:recruitment.frames.length,elapsed:recruitment.completed-recruitment.started,waits:recruitment.waits}});
      await context.close();
    }
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing resources');
  }finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify(report));
})().catch(error=>{console.error(error);process.exitCode=1;});
