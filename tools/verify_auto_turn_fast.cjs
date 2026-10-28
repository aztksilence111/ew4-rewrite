'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5479',folder=path.resolve(process.argv[3]||'build/auto-turn-fast-20261023/browser');
const profile=path.resolve(process.argv[4]||'build/auto-turn-fast-20261023/test-profile');
const report={checks:0,errors:[],missing:[],screenshots:[],turns:[]};
const check=(value,label)=>{assert.ok(value,label);report.checks++;};
const deferred=()=>{let resolve;const promise=new Promise(r=>{resolve=r;});return {promise,resolve};};
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  let armed=null,held=null;const calls=[];
  const hold=ops=>{assert.equal(armed,null);const gate={ops,seen:deferred(),release:deferred()};armed=gate;return gate;};
  const waitHeld=gate=>Promise.race([gate.seen.promise,new Promise((_,reject)=>{const timer=setTimeout(()=>reject(Error('AI request did not reach gate')),20000);timer.unref();})]);
  await page.route('**/api/action?*',async route=>{
    const op=JSON.parse(new URL(route.request().url()).searchParams.get('q')).op;
    if(op==='ai'||op==='ai_batch')calls.push(op);
    if(armed?.ops.includes(op)){
      const gate=armed;armed=null;held=gate;
      const response=await route.fetch();gate.seen.resolve();
      await gate.release.promise;held=null;await route.fulfill({response});
    }else await route.continue();
  });
  const ready=()=>page.waitForFunction(()=>startupReady,null,{timeout:60000});
  const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning&&!aiTask&&!sessionTransition,null,{timeout:20000});
  const finish=async()=>{await page.waitForFunction(()=>window.autoTurnDone,null,{timeout:180000});await page.evaluate(()=>autoTurn);await idle();await page.evaluate(()=>{if(turnUi.open)closeDetail();});};
  const end=()=>page.evaluate(()=>{if(turnUi.open)closeDetail();window.autoTurnDone=false;window.autoTurn=act('end').finally(()=>{autoTurnDone=true;});});
  const saveSession=async slot=>{
    await page.evaluate(slot=>rpc('save',{slot,mode:2}),slot);
    return JSON.parse(await fs.readFile(path.join(profile,'saves/mode-2/slot-'+slot+'.json'),'utf8')).session;
  };
  try{
    await page.goto(base);await ready();
    check(await page.evaluate(()=>settings.autoTurnFast===true&&!settings.followAi),'automatic fast skip defaults on');
    check(await page.locator('#skip-ai').isHidden(),'manual skip button hidden outside AI turns');
    for(const legacy of [false,true]){
      await page.evaluate(legacy=>localStorage.setItem('ew4.settings',JSON.stringify({skipTurnFast:legacy,sound:23})),legacy);
      await page.reload();await ready();
      check(await page.evaluate(()=>settings.autoTurnFast===true&&settings.sound===0&&settings.music===0&&!('skipTurnFast' in settings)),'legacy switch adopts new default and localhost stays muted '+legacy);
    }
    await page.locator('#main-settings').click();
    check(await page.locator('#skip-turn-fast').isChecked(),'settings checkbox starts checked');
    check(await page.locator('#skip-turn-fast').getAttribute('aria-label')==='过回合自动快进','new setting label');
    for(const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(size);await page.locator('#skip-turn-fast').scrollIntoViewIfNeeded();
      check(await page.locator('#skip-turn-fast').evaluate(e=>{
        const b=e.getBoundingClientRect(),a=e.parentElement.querySelector('span').getBoundingClientRect();
        return a.right<b.left&&a.left>=0&&b.right<=innerWidth&&a.top>=0&&b.bottom<=innerHeight;
      }),'caption and checkbox fit '+size.width);
      const name='settings-'+size.width+'.png';await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);
    }
    await page.locator('#skip-turn-fast').uncheck();
    await page.reload();await ready();
    check(await page.evaluate(()=>settings.autoTurnFast===false),'explicit opt out persists');
    await page.locator('#main-settings').click();await page.locator('#skip-turn-fast').check();
    await page.reload();await ready();
    check(await page.evaluate(()=>settings.autoTurnFast===true),'explicit opt in persists');
    await page.setViewportSize({width:1280,height:720});
    await page.evaluate(()=>start('conquest1','fra'));await page.evaluate(()=>rpc('save',{slot:6,mode:2}));
    let firstAuto;
    for(let turn=0;turn<2;turn++){
      const before=calls.length;await end();await finish();
      const result=await page.evaluate(async()=>{
        const full=await rpc('action',{op:'state'});
        return {round:state.round,human:state.factions.find(f=>f.id===state.activeFaction).human,
          released:!skipAi&&!aiRunning&&!aiFocus&&$('ai-action').hidden&&$('skip-ai').hidden,
          consistent:full.revision===state.revision&&JSON.stringify(full.cells)===JSON.stringify(state.cells)&&JSON.stringify(full.units)===JSON.stringify(state.units)};
      });
      const ops=calls.slice(before);report.turns.push({...result,batches:ops.length});
      check(ops.length>0&&ops.every(op=>op==='ai_batch'),'automatic turn uses batches from its first request '+turn);
      check(result.round===turn+2&&result.human&&result.released&&result.consistent,'automatic turn returns correct state and controls '+turn);
      if(turn===0)firstAuto=await saveSession(1);
    }
    for(const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(size);await page.waitForTimeout(150);
      check(await page.evaluate(()=>{
        const pixels=ctx.gpu?ctx.renderer.extract.pixels(ctx.stage):ctx.getImageData(0,0,canvas.width,canvas.height).data;
        const colors=new Set();for(let i=0;i<pixels.length;i+=4*397)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);
        return colors.size>20&&$('skip-ai').hidden;
      }),'human battlefield renders with skip button hidden '+size.width);
      const name='battlefield-'+size.width+'.png';await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);
    }
    await page.setViewportSize({width:1280,height:720});
    await page.evaluate(async()=>{settings.autoTurnFast=false;await installState(await rpc('load',{slot:6,mode:2}),true);});
    let gate=hold(['ai']);await end();await waitHeld(gate);
    check(await page.evaluate(()=>!skipAi&&!fastSkipping()),'opt out starts ordinary AI');
    for(const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(size);
      check(await page.locator('#skip-ai').evaluate(el=>{
        const r=el.getBoundingClientRect();return !el.hidden&&!el.disabled&&r.x>=0&&r.y>=0&&r.right<=innerWidth&&r.bottom<=innerHeight&&document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)===el;
      }),'manual skip is visible and clickable '+size.width);
      const name='manual-skip-'+size.width+'.png';await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);
    }
    await page.setViewportSize({width:1280,height:720});
    await page.evaluate(()=>{window.skipWait= presentationDelay(30000);});
    await page.locator('#skip-ai').click();
    check(await page.evaluate(()=>fastSkipping()&&!settings.autoTurnFast&&$('skip-ai').disabled&&presentationWaiters.size===0),'manual skip always enables full fast skip and cancels animation waits');
    gate.release.resolve();await finish();
    check(calls.at(-1)==='ai_batch','manual skip switches to batches with automatic mode off');
    assert.deepEqual(await saveSession(2),firstAuto);report.checks++;

    await page.evaluate(async()=>{settings.autoTurnFast=true;await installState(await rpc('load',{slot:6,mode:2}),true);});
    gate=hold(['ai_batch']);await end();await waitHeld(gate);
    check(await page.locator('#skip-ai').isHidden(),'automatic mode needs no manual skip button');
    await page.locator('#menu-button').click();gate.release.resolve();await finish();
    const pausedCalls=calls.length;await page.waitForTimeout(250);
    check(calls.length===pausedCalls&&await page.evaluate(()=>aiCanContinue(state)&&!skipAi),'pause stops automatic batches and releases skip');
    gate=hold(['ai_batch']);await page.locator('#resume').click();await waitHeld(gate);
    check(await page.evaluate(()=>fastSkipping()),'resume restarts automatic fast skip');
    await page.locator('#menu-button').click();gate.release.resolve();await idle();
    await page.evaluate(()=>rpc('save',{slot:4,mode:2}));
    await page.locator('#return-menu').click();await idle();
    gate=hold(['ai_batch']);await page.evaluate(()=>{window.loadTurn=load(4,2);});await waitHeld(gate);
    check(await page.evaluate(()=>fastSkipping()),'loading an AI turn automatically fast forwards');
    await page.locator('#menu-button').click();gate.release.resolve();await page.evaluate(()=>loadTurn);await idle();
    await page.locator('#pause-settings').click();await page.locator('#skip-turn-fast').uncheck();
    await page.locator('#close-settings').click();gate=hold(['ai']);await page.locator('#resume').click();await waitHeld(gate);
    check(await page.evaluate(()=>!skipAi),'changing settings while paused applies on resume');
    await page.locator('#menu-button').click();await page.locator('#pause-settings').click();await page.locator('#skip-turn-fast').check();
    check(await page.evaluate(()=>fastSkipping()&&$('skip-ai').hidden),'enabling automatic mode during AI activates fast skip');
    await page.locator('#close-settings').click();gate.release.resolve();await idle();
    await page.locator('#return-menu').click();await idle();
    await page.evaluate(()=>{settings.autoTurnFast=true;return start('conquest1','fra');});
    for(const boundary of ['dialogue','scenarioPopup','human','finished']){
      const before=calls.length;
      await page.evaluate(async boundary=>{
        const original=state,enemy=state.factions.find(f=>!f.human).id;
        state={...state,activeFaction:boundary==='human'?state.playerFaction:enemy,
          dialogue:boundary==='dialogue'?'Boundary fixture':null,
          scenarioPopup:boundary==='scenarioPopup'?{id:'fixture',title:'Fixture',text:'Fixture',choices:[]}:null,
          phase:boundary==='finished'?'Finished':'Active'};
        try{await runAi();}finally{state=original;refreshHud();}
      },boundary);
      check(calls.length===before,'automatic mode stops at '+boundary+' boundary');
    }
    check(report.errors.length===0&&report.missing.length===0,'no browser exceptions or missing assets');
  }catch(e){report.failure=e.message;process.exitCode=1;}
  finally{held?.release.resolve();armed?.release.resolve();await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
