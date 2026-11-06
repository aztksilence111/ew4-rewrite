'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56184',folder=path.resolve(process.argv[3]||'build/surrender-probability-qa-20261105/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  let checks=0;const errors=[],check=(value,label)=>{assert(value,label);checks++;};
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',e=>errors.push(e.message));
  const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition,null,{timeout:90000});
  const readText=()=>page.locator('.diplomacy-confirm').evaluate(e=>[e.textContent,...[...e.querySelectorAll('.original-text[aria-label]')].map(n=>n.getAttribute('aria-label'))].join(''));
  async function open(){await page.locator('#open-diplomacy').click();await page.waitForFunction(()=>diplomacyUi.snapshot&&!diplomacyUi.dialog.hasAttribute('aria-busy'));}
  try{
    await page.goto(base);await page.waitForFunction(()=>typeof startupReady!=='undefined'&&startupReady,null,{timeout:90000});
    await page.evaluate(()=>load(3,2));await idle();await open();
    const target=await page.locator('[data-op="demand_surrender"]:enabled').first().getAttribute('data-target');
    const quote=await page.evaluate(id=>diplomacyUi.snapshot.factions.find(f=>f.id===id).quote,target);
    check(quote.breakdown&&quote.breakdown.maximumChance===100,'actual API exposes saved weights and probability breakdown');
    const b=quote.breakdown;
    check(quote.chance===Math.max(0,Math.min(b.maximumChance,Math.floor(b.lossPercent+b.occupationPercent+b.corePercent+b.commanderPercent+b.legionPercent+b.buildingPercent+b.superiorityPercent+quote.pairBonus+0.5))),'actual API final probability equals weighted contributions');
    await page.locator('[data-op="demand_surrender"][data-target="'+target+'"]').click();
    const copy=await readText();
    for(const label of ['核心失守','我方占领','重叠核心','将领阵亡','阵营军团多','建筑','兵力占优','概率上限'])check(copy.includes(label),'confirmation renders '+label);
    check(b.initialCommanders!==null&&copy.includes(b.deadCommanders+'/'+b.initialCommanders),'confirmation shows frozen initial roster and death count');
    await page.evaluate(id=>{const f=state.factions.find(f=>f.id===id),q=diplomacyUi.snapshot.factions.find(f=>f.id===id).quote;
      diplomacyUi.ask(f,{...q,breakdown:{...q.breakdown,initialCommanders:4,deadCommanders:2,commanderPercent:12.5}},'demand_surrender');},target);
    const fractional=await readText();check(fractional.includes('2/4')&&fractional.includes('12.5'),'confirmation preserves fractional commander contribution');
    await page.evaluate(id=>diplomacyUi.ask(state.factions.find(f=>f.id===id),diplomacyUi.snapshot.factions.find(f=>f.id===id).quote,'demand_surrender'),target);
    for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844},{width:320,height:568}]){
      await page.setViewportSize(viewport);
      await page.waitForFunction(()=>{const r=diplomacyUi.dialog.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1;});
      check(await page.locator('.diplomacy-confirm').evaluate(e=>e.scrollWidth<=e.clientWidth+1),'probability breakdown wraps at '+viewport.width);
      await page.locator('.diplomacy-confirm-actions').scrollIntoViewIfNeeded();
      check(await page.locator('.diplomacy-confirm-actions').evaluate(e=>{const r=e.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight+1;}),'confirmation actions remain reachable at '+viewport.width);
      await page.locator('.diplomacy-confirm').evaluate(e=>e.scrollTop=0);
      await page.screenshot({path:path.join(folder,'probability-'+viewport.width+'.png')});
    }
    await page.locator('.diplomacy-confirm-actions button').first().click();
    await page.evaluate(()=>diplomacyUi.close(false));await page.evaluate(()=>load(2,2));await idle();await open();
    await page.locator('[data-op="demand_surrender"][data-target="'+target+'"]').click();
    await page.locator('.diplomacy-confirm-actions button').last().click();await idle();await page.waitForFunction(()=>!diplomacyUi.submitting);
    check(await page.locator('[data-op="demand_surrender"][data-target="'+target+'"]').isDisabled(),'failed weighted attempt locks retry');
    await page.evaluate(()=>diplomacyUi.close(false));await page.evaluate(()=>load(0,2));await idle();await open();
    check(await page.locator('[data-op="demand_surrender"][data-target="'+target+'"]').isDisabled(),'retry lock survives actual autosave reload');
    await page.evaluate(()=>diplomacyUi.close(false));
    await page.evaluate(()=>start('conquest1','fra'));await idle();
    if(await page.evaluate(()=>stageIntroOpen))await page.locator('#detail-confirm').click();
    for(let i=0;i<16;i++){
      const pending=await page.evaluate(()=>({story:!!state.story,dialogue:!!state.dialogue}));if(!pending.story&&!pending.dialogue)break;
      if(pending.story){await page.waitForFunction(()=>!storyPlayer.preparing&&!storyPlayer.commandPending);await page.locator('#story-player button[aria-label="跳过剧情"]').click();}
      else await page.evaluate(()=>act('dialogue'));
      await idle();
    }
    await open();
    const fresh=await page.evaluate(()=>diplomacyUi.snapshot.factions.find(f=>state.factions.some(s=>s.id===f.id&&s.relation==='hostile'&&!s.defeated)).quote.breakdown);
    check(fresh.maximumChance===95&&fresh.buildingPercent<0,'fresh conquest enables reference-derived military weights');
    check(errors.length===0,errors.join('\n'));
    await fs.writeFile(path.join(folder,'result.json'),JSON.stringify({checks,errors},null,2));console.log(JSON.stringify({checks,errors}));
  }finally{await browser.close();}
})().catch(e=>{console.error(String(e.stack||e.message).split('\n').slice(0,3).join('\n'));process.exit(1);});
