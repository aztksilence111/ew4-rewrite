'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5405',folder=path.resolve(process.argv[3]||'build/pause-ai-20261022/browser');
const report={checks:0,errors:[],missing:[],screenshots:[]};
const check=(value,label)=>{assert.ok(value,label);report.checks++;};
const deferred=()=>{let resolve;const promise=new Promise(r=>{resolve=r;});return {promise,resolve};};
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  let armed=null,held=null,aiCalls=0,saveCalls=0;
  const hold=ops=>{assert.equal(armed,null);const gate={ops,seen:deferred(),release:deferred()};armed=gate;return gate;};
  const waitHeld=gate=>Promise.race([gate.seen.promise,new Promise((_,reject)=>{const timer=setTimeout(()=>reject(Error('AI request did not reach gate')),20000);timer.unref();})]);
  const shot=async name=>{await page.screenshot({path:path.join(folder,name+'.png')});report.screenshots.push(name);};
  await page.route('**/api/action?*',async route=>{
    const op=JSON.parse(new URL(route.request().url()).searchParams.get('q')).op;
    if(op==='ai'||op==='ai_batch')aiCalls++;
    if(armed?.ops.includes(op)){
      const gate=armed;armed=null;held=gate;
      const response=await route.fetch();gate.seen.resolve();
      await gate.release.promise;held=null;await route.fulfill({response});
    }else await route.continue();
  });
  page.on('request',r=>{if(new URL(r.url()).pathname==='/api/saves')saveCalls++;});
  const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning&&!aiTask&&!sessionTransition,null,{timeout:20000});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    await page.evaluate(()=>{settings.autoTurnFast=false;return start('conquest1','fra');});
    let gate=hold(['ai']);
    await page.evaluate(()=>{window.pauseTurn=act('end');});await waitHeld(gate);
    await page.locator('#menu-button').click();
    const savesBefore=saveCalls;
    await page.locator('#save').click();
    check(await page.locator('#notice').innerText()==='请等待当前行动完成后再保存','in-flight save explains the wait');
    check(saveCalls===savesBefore&&await page.locator('#detail-modal').isHidden(),'in-flight save never opens slot picker');
    await page.locator('#pause-settings').click();
    check(await page.locator('#settings').isVisible(),'settings available while AI response is pending');
    gate.release.resolve();await page.evaluate(()=>window.pauseTurn);await idle();
    check(await page.locator('#pause').isVisible()&&await page.locator('#settings').isVisible(),'AI result preserves pause and settings');
    await page.locator('#close-settings').click();
    check(await page.evaluate(()=>aiCanContinue(state)),'paused AI turn still has work to do');
    const stoppedCalls=aiCalls,pausedRevision=await page.evaluate(()=>state.revision);
    check(await page.evaluate(()=>!victoryUi.api.ready(state)&&!localMultiplayerUi.api.ready()),'AI result panels defer to pause');
    await page.waitForTimeout(300);
    check(aiCalls===stoppedCalls&&await page.evaluate(r=>state.revision===r,pausedRevision),'pause prevents another AI command');
    check(await page.evaluate(async()=>state.revision===(await rpc('action',{op:'state'})).revision),'paused state matches completed backend command');
    await page.locator('#save').click();await page.waitForSelector('.save-slot-confirm');
    await page.locator('.save-slot-confirm').nth(1).click();
    await page.waitForFunction(()=>$('detail-modal').hidden);
    check(await page.evaluate(async()=>{const s=(await rpc('saves',{mode:2})).find(s=>s.slot===1);return s.exists&&s.round===state.round;}),'save works after current action settles while paused');
    await shot('desktop-paused');

    gate=hold(['ai']);await page.locator('#resume').click();await waitHeld(gate);
    check(aiCalls>stoppedCalls,'resume continues the suspended AI turn');
    await page.locator('#menu-button').click();gate.release.resolve();await idle();
    check(await page.locator('#pause').isVisible(),'second pause survives another result');

    // Allow behavior-only regression when the existing mobile pause layout is tracked separately.
    for(const [name,width,height] of (process.argv.includes('--desktop-only')?[]:[['landscape',844,390],['portrait',390,844]])){
      await page.setViewportSize({width,height});
      await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
      check(await page.locator('#restart').isVisible()&&await page.locator('#return-menu').isVisible(),name+' restart and exit remain reachable');
      check(await page.locator('#pause .dialog').evaluate(el=>{const b=el.getBoundingClientRect();return b.left>=0&&b.top>=0&&b.right<=innerWidth&&b.bottom<=innerHeight;}),name+' pause fits viewport');
      await shot(name+'-paused');
    }
    await page.setViewportSize({width:1280,height:720});
    await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
    gate=hold(['ai']);await page.locator('#resume').click();await waitHeld(gate);
    await page.locator('#menu-button').click();
    check(await page.locator('#restart').evaluate(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;}),
      'restart inside viewport: '+JSON.stringify(await page.evaluate(()=>({viewport:[innerWidth,innerHeight],pause:$('pause').className,menu:document.querySelector('#pause>.pause-dialog').getBoundingClientRect().toJSON(),style:document.querySelector('#pause>.pause-dialog').getAttribute('style'),restart:$('restart').getBoundingClientRect().toJSON()}))));
    await page.locator('#restart').click();
    check(await page.evaluate(()=>sessionTransition&&aiStopRequested),'restart immediately requests AI interruption');
    const restartCalls=aiCalls;gate.release.resolve();await idle();
    check(await page.evaluate(()=>state.round===1&&state.activeFaction===state.playerFaction&&state.startCountry==='fra'&&$('pause').hidden),'restart resets the battle and preserves country');
    const restartedRevision=await page.evaluate(()=>state.revision);await page.waitForTimeout(300);
    check(aiCalls===restartCalls&&await page.evaluate(r=>state.revision===r,restartedRevision),'old AI never continues into restarted game');

    // Hold a genuine action inside presentation to exercise interruption of animation waits.
    await page.evaluate(()=>{
      window.realInstallState=installState;window.presentationGate=true;
      installState=async function(next,...args){if(aiRunning&&window.presentationGate){window.presentationGate=false;await presentationDelay(30000);}return realInstallState(next,...args);};
      window.pauseTurn=act('end');
    });
    await page.waitForFunction(()=>aiRunning&&presentationWaiters.size>0&&!window.presentationGate);
    await page.locator('#menu-button').click();await page.locator('#restart').click();await idle();
    check(await page.evaluate(()=>state.round===1&&presentationWaiters.size===0&&combatMotions.size===0),'restart cancels long presentation waits');
    await page.evaluate(()=>{installState=window.realInstallState;settings.autoTurnFast=true;});

    // Fast skip must obey the same pause and transition barriers as single-step AI.
    gate=hold(['ai_batch']);await page.evaluate(()=>{window.pauseTurn=act('end');});await waitHeld(gate);
    check(await page.evaluate(()=>fastSkipping()),'automatic fast skip starts without manual button');
    await page.locator('#menu-button').click();gate.release.resolve();await idle();
    check(await page.locator('#pause').isVisible(),'batch response preserves pause');
    const batchCalls=aiCalls;await page.waitForTimeout(200);check(aiCalls===batchCalls,'paused fast skip schedules no next batch');

    gate=hold(['ai_batch']);await page.locator('#resume').click();await waitHeld(gate);
    await page.locator('#menu-button').click();await page.locator('#return-menu').click();
    check(await page.evaluate(()=>sessionTransition&&aiStopRequested),'exit immediately requests AI interruption');
    const exitCalls=aiCalls;gate.release.resolve();await idle();
    check(await page.evaluate(()=>!state&&$('play').hidden&&!$('main-menu').hidden&&$('pause').hidden),'exit returns to main menu while AI was running');
    check(await page.evaluate(async()=>{try{await rpc('action',{op:'state'});return false;}catch(e){return e.message==='请先开始游戏';}}),'exit releases backend session using the latest revision');
    await page.waitForTimeout(300);check(aiCalls===exitCalls,'old AI cannot reopen an exited battle');

    await page.evaluate(()=>{settings.autoTurnFast=false;return start('conquest1','fra');});
    gate=hold(['ai']);await page.evaluate(()=>{window.pauseTurn=act('end');});await waitHeld(gate);
    await page.route('**/api/start?*',route=>route.fulfill({json:{ok:false,code:'client.failure',message:'restart failure fixture'}}));
    await page.locator('#menu-button').click();await page.locator('#restart').click();gate.release.resolve();await idle();
    report.restartFailure=await page.evaluate(()=>({state:!!state,play:$('play').hidden,pause:$('pause').hidden,notice:$('notice').querySelector('.original-text')?.getAttribute('aria-label')||$('notice').textContent}));
    check(report.restartFailure.state&&!report.restartFailure.play&&!report.restartFailure.pause&&report.restartFailure.notice==='restart failure fixture','failed restart leaves paused battle available');
    await page.unroute('**/api/start?*');await page.locator('#restart').click();await idle();
    check(await page.evaluate(()=>state.round===1),'failed restart can be retried');
    await page.locator('#menu-button').click();await page.locator('#return-menu').click();await idle();

    // Opening pause before the end-turn response arrives must stop the first AI step too.
    await page.evaluate(()=>start('conquest1','fra'));
    gate=hold(['end']);await page.evaluate(()=>{window.pauseTurn=act('end');});await waitHeld(gate);
    await page.locator('#menu-button').click();const beforeFirstAi=aiCalls;
    gate.release.resolve();await page.evaluate(()=>window.pauseTurn);await idle();
    check(aiCalls===beforeFirstAi&&await page.locator('#pause').isVisible(),'pause before end-turn response prevents first AI step');
    await page.locator('#return-menu').click();await idle();

    // A late event popup may appear underneath pause but must never take its controls.
    await page.evaluate(async()=>{
      await start('conquest1','fra');
      window.eventOriginal=state;$('menu-button').click();
      await installState({...state,scenarioPopup:{id:'pause-event-fixture',title:'事件',text:'等待处理',choices:[{id:'ok',text:'继续',available:true}]} });
    });
    const top=id=>page.locator('#'+id).evaluate(el=>{const b=el.getBoundingClientRect();return el.contains(document.elementFromPoint(b.x+b.width/2,b.y+b.height/2));});
    check(await top('restart'),'late event cannot cover pause commands');
    await page.locator('#pause-settings').click();check(await top('confirm-settings'),'settings stay above pending event');
    await page.locator('#close-settings').click();await page.locator('#save').click();await page.waitForSelector('.save-slot-confirm');
    check(await top('close-detail'),'save picker stays above pending event');await page.locator('#close-detail').click();
    await shot('pause-over-event');await page.locator('#resume').click();
    check(await page.locator('#scenario-event').isVisible()&&await top('menu-button'),'return preserves event and pause remains reachable');
    await page.locator('#menu-button').click();await page.evaluate(()=>{state=eventOriginal;refreshHud();});
    await page.locator('#return-menu').click();await idle();

    await page.evaluate(()=>start('campaign1_01'));
    check(await page.evaluate(()=>stageIntroOpen),'campaign briefing shown before dialogue');
    await page.locator('#close-detail').click();await idle();
    check(await page.locator('#dialogue').isVisible(),'real campaign dialogue available after briefing');
    const dialogueRevision=await page.evaluate(()=>state.revision);
    await page.locator('#menu-button').click();await page.locator('#pause-settings').click();await page.locator('#close-settings').click();
    check(await page.evaluate(r=>state.revision===r,dialogueRevision),'pause and settings never acknowledge dialogue');
    await page.locator('#resume').click();await page.locator('#dialogue-ok').click();await idle();
    check(await page.evaluate(r=>state.revision===r+1,dialogueRevision),'real campaign dialogue still acknowledges once');
    await page.locator('#menu-button').click();await page.locator('#return-menu').click();await idle();
    check(report.errors.length===0,'no browser exceptions');check(report.missing.length===0,'no missing resources');
  }catch(e){report.failure=e.message;process.exitCode=1;}
  finally{held?.release.resolve();armed?.release.resolve();await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
