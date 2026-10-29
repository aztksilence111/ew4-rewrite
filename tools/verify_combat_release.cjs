'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2]||'http://127.0.0.1:56145',out=path.resolve(process.argv[3]||'build/combat-release-check-20261029/browser');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],cases:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const [renderer,width,height,speed] of [['pixi',1280,720,1],['canvas',1280,720,5],['pixi',844,390,3]]){
      const context=await browser.newContext({viewport:{width,height},hasTouch:width===844,isMobile:width===844}),page=await context.newPage();
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(process.argv.includes('--baseline'))await page.route(/\/(?:compat\/)?app\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',path:path.resolve('build/combat-release-check-20261029/baseline-app.js')}));
      else if(process.argv.includes('--source'))await page.route(/\/(?:compat\/)?app\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',path:path.resolve('rewrite/Ew4.Web/wwwroot/app.js')}));
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(()=>start('conquest1','fra'));
      const initial=await page.evaluate(async speed=>{
        settings.speed=5;await selectCell(2083,true,true);await act('move',{unit:selected.id,cell:2085});
        settings.speed=speed;await selectCell(2085,true,true);
        if(!selection.targets.includes(2007))throw Error('Expected original France artillery target');
        const attacker=selected.id,defender=state.units.find(u=>u.cell===2007).id,oldHealth=state.units.find(u=>u.id===defender).health;
        const a=point(selected.cell),b=point(2007);view.x=(a.x+b.x)/2;view.y=(a.y+b.y)/2;view.zoom=1.5;requestRender();
        const nextUnit=state.units.find(u=>u.owner===state.playerFaction&&u.id!==attacker&&selection.range!==null&&Math.hypot(point(u.cell).x-point(selected.cell).x,point(u.cell).y-point(selected.cell).y)<130);
        window.releaseProbe={attacker,defender,oldHealth,nextUnit:nextUnit.id,hits:[],responses:[],waits:[]};
        const delay=presentationDelay;presentationDelay=duration=>{releaseProbe.waits.push({duration,afterHit:releaseProbe.hits.length>0});return delay(duration);};
        const hud=refreshHud;refreshHud=(...args)=>{if(releaseProbe.healthAt==null&&state.units.find(u=>u.id===defender)?.health!==oldHealth)releaseProbe.healthAt=performance.now();return hud(...args);};
        const add=damageNumbers.add.bind(damageNumbers);damageNumbers.add=(...args)=>{releaseProbe.hits.push({at:performance.now(),health:state.units.find(u=>u.id===defender)?.health,busy});return add(...args);};
        const send=rpc;rpc=async(...args)=>{const next=await send(...args);if(args[0]==='action'&&args[1].op!=='select')releaseProbe.responses.push({op:args[1].op,at:performance.now(),combat:next.combat,revision:next.revision,tail:combatMotions.has(attacker)});return next;};
        window.releaseAttack=act('attack',{unit:attacker,target:defender}).then(()=>{releaseProbe.completed=performance.now();releaseProbe.newHealth=state.units.find(u=>u.id===defender)?.health;const tail=combatMotions.get(attacker);releaseProbe.remaining=tail?tail.started+tail.delay+tail.segments.at(-1).end-performance.now():0;releaseProbe.busy=busy;});
        return {attacker,defender,oldHealth};
      },speed);
      await page.waitForFunction(()=>releaseProbe.completed,null,{timeout:20000});
      const result=await page.evaluate(()=>({...releaseProbe,hits:releaseProbe.hits.map(h=>({...h,afterCompletion:releaseProbe.completed-h.at}))}));
      report.cases.push({renderer,width,height,speed,...initial,...result});
      check(result.hits.length===1,'one hit for original artillery '+renderer+speed);
      const attack=result.responses.find(r=>r.op==='attack');
      check(result.newHealth===result.oldHealth-attack.combat.damage,'authoritative health installed '+renderer+speed);
      check(!result.busy&&result.remaining>100,'input unlocks while artillery recovery still playing '+JSON.stringify(result));
      check(!result.waits.some(w=>w.afterHit)&&result.healthAt-result.hits[0].at<150,'health commits at hit without recovery wait '+renderer+speed);
      const interaction=await page.evaluate(async()=>{
        const probe=releaseProbe;let unit,cell;
        const origin=point(state.units.find(u=>u.id===probe.attacker).cell),distance=u=>Math.hypot(point(u.cell).x-origin.x,point(u.cell).y-origin.y);
        for(const candidate of state.units.filter(u=>u.owner===state.playerFaction&&u.id!==probe.attacker&&u.category!=='fort').sort((a,b)=>distance(a)-distance(b))){
          await selectCell(candidate.cell,false,true);
          const legal=selection?.range.find(cell=>cell!==candidate.cell&&!state.units.some(u=>u.cell===cell));
          if(legal!=null){unit=candidate;cell=legal;break;}
        }
        if(cell==null)throw Error('Next unit has no legal move');
        const selectedId=selected?.id;
        const tail=combatMotions.get(probe.attacker),duringRecovery=!!tail&&performance.now()<tail.started+tail.delay+tail.segments.at(-1).end;
        const revision=state.revision;window.releaseMove=act('move',{unit:unit.id,cell});
        return {selectedId,expected:unit.id,duringRecovery,revision,cell};
      });
      check(interaction.selectedId===interaction.expected&&interaction.duringRecovery,'next unit selected and commanded during recovery '+renderer+speed);
      await page.waitForFunction(()=>releaseProbe.responses.some(r=>r.op==='move'),null,{timeout:15000});
      check(await page.evaluate(()=>releaseProbe.responses.find(r=>r.op==='move').tail),'backend accepts next move before old recovery ends '+renderer+speed);
      await page.evaluate(()=>releaseMove);
      check(await page.evaluate(({id,cell,revision})=>state.revision>revision&&state.units.find(u=>u.id===id)?.cell===cell,{id:interaction.expected,...interaction}),'next move committed without stale revision '+renderer+speed);
      await page.waitForFunction(()=>!combatMotions.size&&!presentationWaiters.size,null,{timeout:10000});
      check(await page.evaluate(()=>!busy&&!finishPresentation),'no remaining input lock or blocking timers '+renderer+speed);
      const png=PNG.sync.read(await page.screenshot({path:path.join(out,renderer+'-'+width+'-'+speed+'.png')})),colors=new Set();
      for(let i=0;i<png.data.length;i+=124)colors.add(png.data.subarray(i,i+3).toString('hex'));
      check(colors.size>100,'nonblank rendered battle '+renderer+speed);
      if(renderer==='pixi'&&width===1280){
        const supplemental=await page.evaluate(async()=>{
          const original=state,originalAdd=damageNumbers.add,results=[];
          const artillery=state.units.find(u=>u.id===releaseProbe.attacker),fort=state.units.find(u=>u.category==='fort'),infantry=state.units.find(u=>u.category==='infantry');
          await Promise.all(['army_artillery','army_fort'].map(loadLibrary));
          const setup=()=>{
            effects.clear();damageNumbers.clear();combatMotions.clear();deaths.clear();settings.speed=5;
            state={...original,units:[{...artillery,id:'release-a',profile:'ew4.unit.fra.light_artillery.1',cell:2085},
              {...fort,id:'release-b',profile:'ew4.unit.gbr.fortress.0',cell:2007},{...infantry,id:'release-c',cell:2008}]};
          };
          try{
            for(const reverse of [false,true]){
              setup();const hits=[];damageNumbers.add=(amount)=>hits.push({amount,at:performance.now()});
              const pending=playCombat({attacker:reverse?'release-b':'release-a',defender:reverse?'release-a':'release-b',damage:21,counterattack:true,counterDamage:13});
              const motions=[...combatMotions.values()],salvos=[...effects.instances],first=salvos.map(i=>i.started+Math.min(...i.events.map(e=>Number(e.at)||0))*1000/i.speed);
              await pending;
              results.push({kind:'counter',reverse,sameStart:motions[0].started===motions[1].started,salvoGap:Math.max(...first)-Math.min(...first),hits,
                remaining:motions.map(m=>m.started+m.delay+m.segments.at(-1).end-performance.now()),waiters:presentationWaiters.size,
                fullClips:motions.some(m=>m.segments.length>=2)&&motions.every(m=>m.segments.length>=1&&m.segments.every((s,i)=>i===0||s.start===m.segments[i-1].end))});
            }
            setup();let hits=[];damageNumbers.add=(amount)=>hits.push(amount);
            await playCombat({attacker:'release-a',defender:'release-b',damage:40,counterattack:false,splashTarget:'release-c',splashDamage:15},new Set(['release-c']));
            results.push({kind:'splash',hits,death:deaths.has('release-c'),unrelatedAlive:!deaths.has('release-b'),waiters:presentationWaiters.size});
            setup();hits=[];damageNumbers.add=amount=>hits.push(amount);
            await playCombat({attacker:'release-a',defender:'release-b',damage:999,counterattack:false},new Set(['release-b']));
            results.push({kind:'lethal',hits,death:deaths.has('release-b'),attackers:combatMotions.size,waiters:presentationWaiters.size});
            setup();hits=[];damageNumbers.add=amount=>hits.push(amount);
            const pending=playCombat({attacker:'release-a',defender:'release-b',damage:21,counterattack:true,counterDamage:13});
            aiRunning=true;skipAi=true;clearAiPresentation();await pending;
            results.push({kind:'skip',hits,motions:combatMotions.size,waiters:presentationWaiters.size,finish:!!finishPresentation});
          }finally{aiRunning=false;skipAi=false;state=original;damageNumbers.add=originalAdd;clearAiPresentation();requestRender();}
          return results;
        });
        report.supplemental=supplemental;
        for(const s of supplemental){
          check(s.waiters===0,'no blocking waiter after '+s.kind);
          if(s.kind==='counter'){
            check(s.sameStart&&s.salvoGap<.001&&s.fullClips,'synchronized salvos and complete recovery clips '+s.reverse);
            check(s.hits.map(h=>h.amount).join(',')==='21,13'&&Math.abs(s.hits[0].at-s.hits[1].at)<30,'counter damage stays simultaneous '+s.reverse);
            check(s.remaining.some(t=>t>100),'counter ends before recovery '+s.reverse);
          }else if(s.kind==='skip')check(!s.hits.length&&!s.motions&&!s.finish,'skip cancels both attacks and their timers');
          else check(s.death&&(s.kind==='splash'?s.unrelatedAlive&&s.hits.join(',')==='40,15':s.attackers===1&&s.hits.join(',')==='999'),'death and splash behavior '+s.kind);
        }
      }
      await context.close();
    }
    check(!report.errors.length&&!report.missing.length,'no script or resource errors '+JSON.stringify(report));
  }catch(e){report.failure=e.message;throw e;}
  finally{await fs.writeFile(path.join(out,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
