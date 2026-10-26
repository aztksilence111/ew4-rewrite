'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5579',folder=path.resolve(process.argv[3]||'build/ai-targets-20261026/browser'),renderer=process.argv[4]||'pixi';
(async()=>{
  await fs.mkdir(folder,{recursive:true});const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  const page=await browser.newPage({viewport:{width:1280,height:720}}),report={renderer,checks:0,errors:[],missing:[],views:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition);
  const pause=async()=>{await page.locator('#menu-button').click();await page.waitForFunction(()=>!$('pause').classList.contains('pause-sidebars-pending'));await page.waitForTimeout(320);};
  const settle=async()=>{await idle();await page.waitForTimeout(320);};
  const clickCell=async cell=>{const p=await page.evaluate(cell=>{const p=point(cell);return{x:(p.x-view.x)*view.zoom+viewport.w/2,y:(p.y-view.y)*view.zoom+viewport.h/2};},cell);await page.mouse.click(p.x,p.y);};
  try{
    await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:90000});await page.evaluate(()=>start('conquest1','fra'));await idle();
    const selected=await page.evaluate(async()=>{
      const units=state.units.filter(u=>u.owner===state.playerFaction&&!u.commanderId).slice(0,2);window.aiTargetUnits=units.map(u=>u.id);
      await act('player_ai_config',{offer:'units',units:window.aiTargetUnits});await act('player_ai_config',{offer:'policy',policy:'manual'});
      await act('disband',{unit:units[0].id});return units.map(u=>({id:u.id,cell:u.cell}));
    });
    check(await page.evaluate(()=>state.playerAi.draft.units.length===1&&state.playerAi.draft.units[0]===window.aiTargetUnits[1]),'removed unit is pruned from authoritative selection');
    await pause();check(await page.locator('#ai-control').evaluate(el=>el.getBoundingClientRect().width>=224),'AI panel enlarged');
    check(await page.locator('.ai-control-status').evaluate(el=>(el.textContent||el.querySelector('[aria-label]')?.getAttribute('aria-label')||'').includes('1 支部队')),
      'sidebar reports live selected count: '+await page.locator('.ai-control-status').evaluate(el=>el.outerHTML));
    await page.locator('#ai-units').click();
    check(await page.evaluate(()=>aiControl.chosen.size===1&&document.querySelector('.ai-selection-count').textContent==='1 支部队'),'map reports surviving count');
    await page.locator('#ai-clear-selection').click();check(await page.evaluate(()=>aiControl.chosen.size===0&&!$('ai-map-confirm').disabled),'one-click clear empties selection');
    await page.locator('#ai-map-cancel').click();await settle();check(await page.evaluate(()=>state.playerAi.draft.units.length===1),'cancel does not submit clear');
    await page.locator('#ai-units').click();
    await page.evaluate(()=>{const u=state.units.find(u=>u.id===window.aiTargetUnits[1]),p=point(u.cell);view.x=p.x;view.y=p.y;view.zoom=1;clampView();requestRender();});
    await page.locator('#ai-clear-selection').click();await clickCell(selected[1].cell);
    await page.locator('#ai-map-confirm').click();await settle();check(await page.evaluate(()=>state.playerAi.draft.units.length===1),'surviving selection can be edited and submitted');
    await page.locator('#ai-goal').click();
    const cells=await page.evaluate(()=>{const u=state.units.find(u=>u.id===window.aiTargetUnits[1]);return [u.cell,u.cell+1,u.cell-1];});
    for(const cell of cells)await clickCell(cell);
    check(await page.locator('.ai-goal-row').count()===3,'map accepts multiple goals');
    await page.locator('.ai-goal-row').nth(2).locator('[data-action=up]').click();
    await page.locator('.ai-goal-row').nth(1).locator('[data-action=up]').click();
    check(await page.evaluate(cells=>JSON.stringify(aiControl.targets)===JSON.stringify([cells[2],cells[0],cells[1]]),cells),'priority arrows reorder targets');
    await page.locator('.ai-goal-row').nth(1).locator('[data-action=down]').click();
    check(await page.evaluate(cells=>JSON.stringify(aiControl.targets)===JSON.stringify([cells[2],cells[1],cells[0]]),cells),'lowering priority updates order');
    await page.locator('.ai-goal-row').nth(1).locator('[data-action=remove]').click();
    check(await page.evaluate(cells=>JSON.stringify(aiControl.targets)===JSON.stringify([cells[2],cells[0]]),cells),'remove changes only chosen goal');
    await page.waitForTimeout(100);await page.screenshot({path:path.join(folder,'targets-desktop.png')});
    check(await page.evaluate(()=>battlefield.aiOverlay.goals.length===2&&battlefield.aiGoalNumbers.size>=2),'numbered targets reach renderer');
    await page.locator('#ai-map-confirm').click();await settle();
    check(await page.evaluate(cells=>JSON.stringify(state.playerAi.draft.goals)===JSON.stringify([cells[2],cells[0]])&&!$('ai-execute').disabled,cells),'backend confirms priority order and unlocks start');
    await page.locator('#ai-execute').click();await settle();
    check(await page.evaluate(cells=>{const u=state.units.find(u=>u.id===window.aiTargetUnits[1]);return JSON.stringify(u.aiGoals)===JSON.stringify([cells[2],cells[0]])&&u.aiControlled;},cells),'start copies target queue to unit');
    await page.evaluate(()=>rpc('save',{slot:2,mode:2}));
    await page.evaluate(async()=>{await installState(await rpc('load',{slot:2,mode:2}),true);});await idle();await pause();
    check(await page.evaluate(()=>state.playerAi.enabled&&state.playerAi.draft.goals.length===2),'real save load preserves multi-target execution');
    await page.locator('#ai-execute').click();await settle();
    check(await page.evaluate(()=>state.playerAi.draft.goals.length===2),'stop retains remaining ordered targets');
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(100);await page.evaluate(()=>{aiControl.fit();pauseObjectives.fit();});
      if(await page.evaluate(()=>$('pause').classList.contains('ai-compact')&&!$('pause').classList.contains('ai-sidebar-expanded')))await page.locator('.ai-control-title').click();
      await page.screenshot({path:path.join(folder,'pause-'+width+'x'+height+'.png')});
      await page.locator('#ai-units').click();
      check(await page.locator('#ai-clear-selection').evaluate(el=>{const b=el.getBoundingClientRect();return b.x>=0&&b.y>=0&&b.right<=innerWidth&&b.bottom<=innerHeight&&el.scrollWidth<=el.clientWidth+2;}),'clear all fits '+width+'x'+height);
      await page.screenshot({path:path.join(folder,'selection-'+width+'x'+height+'.png')});await page.locator('#ai-map-cancel').click();await settle();
      await page.locator('#ai-goal').click();check(await page.locator('#ai-goal-editor').evaluate(el=>{const b=el.getBoundingClientRect();return b.x>=0&&b.y>=0&&b.right<=innerWidth&&b.bottom<=innerHeight;}),'goal editor fits '+width+'x'+height);
      await page.screenshot({path:path.join(folder,'targets-'+width+'x'+height+'.png')});await page.locator('#ai-map-cancel').click();await settle();report.views.push([width,height]);
    }
    await page.locator('#ai-units').click();await page.locator('#ai-clear-selection').click();await page.locator('#ai-map-confirm').click();await settle();
    check(await page.evaluate(()=>!state.playerAi.draft.unitsConfirmed&&state.playerAi.draft.units.length===0&&$('ai-execute').disabled),'confirming clear resets draft');
    check(!report.errors.length&&!report.missing.length,'no script errors or missing assets');
    await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
