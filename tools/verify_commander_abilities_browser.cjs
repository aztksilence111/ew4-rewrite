'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const [base,output]=process.argv.slice(2);
(async()=>{
  await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,errors:[],missing:[],views:[]},check=(pass,label)=>{assert.ok(pass,label);report.checks++;};
  try{
    for(const [source,width,height] of [[true,1280,720],[false,844,390],[false,390,844]]){
      const context=await browser.newContext({viewport:{width,height},hasTouch:width<1000,isMobile:width<1000}),page=await context.newPage();
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(source)await page.route(base+'/',async route=>{const response=await route.fetch();await route.fulfill({response,body:(await response.text()).replace(/((?:src|href)=")compat\//g,'$1')});});
      await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(async()=>{menus.data=await rpc('headquarters');const held=menus.data.roster.find(c=>c.id==='ability.donor');menus.commanderInfo(menus.definition(held),held);});
      await page.evaluate(async()=>{await Promise.all(document.getAnimations().map(a=>a.finished.catch(()=>{})));});
      check(await page.locator('.commander-ability').count()===40,'eight original and 32 new rows');
      check(await page.locator('.commander-ability').evaluateAll(rows=>rows.slice(0,3).map(row=>row.dataset.ability).join(','))==='demo.air,movement,infantry','custom and original order follows configuration');
      check((await page.locator('[data-ability="infantry"]').getAttribute('title')).startsWith('步兵指挥 '),'original ability label available on hover');
      const panel=page.locator('.commander-abilities');
      const bounds=await panel.evaluate(el=>{const r=el.getBoundingClientRect();return {fits:r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight,scrolls:el.scrollHeight>el.clientHeight+1,height:el.offsetHeight,
        rowsFit:[...el.children].every(row=>{const b=row.getBoundingClientRect();return b.top>=r.top-1&&b.bottom<=r.bottom+1;})};});
      check(bounds.fits&&!bounds.scrolls&&bounds.rowsFit&&bounds.height===541,'panel grows to fit all forty abilities, allowing one pixel for the shared border');
      const air=page.locator('[data-ability="demo.air"]');await air.scrollIntoViewIfNeeded();
      check(await air.locator('.ability-name').count()===0&&/航空能力/.test(await air.getAttribute('title')),'icon-only row retains hover name');
      check(await air.locator('.ability-stars').getAttribute('data-value')==='12','actual custom value');
      check(await air.locator('.ability-stars>img').count()===1,'default above-five bounded star art without Mod opt-in');
      check(await air.locator('img').first().evaluate(img=>img.complete&&img.naturalWidth>0),'custom icon rendered');
      await page.screenshot({path:path.join(output,width+'-air.png')});
      const last=page.locator('[data-ability="demo.extra.31"]');await last.scrollIntoViewIfNeeded();
      check(await last.locator('.ability-stars .original-text').getAttribute('aria-label')==='×2147483647','full int32 value displayed');
      check(await last.evaluate(el=>{const r=el.getBoundingClientRect();return [...el.querySelectorAll('.ability-name,.ability-stars')].every(child=>{const c=child.getBoundingClientRect();return c.left>=r.left&&c.right<=r.right+1&&c.top>=r.top&&c.bottom<=r.bottom+1;});}),'text and stars fit custom row');
      await page.screenshot({path:path.join(output,width+'-last.png')});
      await page.evaluate(()=>{window.abilityData=menus.data;menus.data={...menus.data,commanderAbilities:[],commanderAbilityOrder:['movement','training','infantry'],commanderAbilityPresentation:{}};const held=menus.data.roster.find(c=>c.id==='ability.donor');menus.commanderInfo(menus.definition(held),held);});
      check(await page.locator('.commander-ability').count()===8&&await page.locator('.commander-ability').first().getAttribute('data-ability')==='movement','original-only order retains eight fixed rows');
      await page.evaluate(()=>{menus.data=window.abilityData;delete window.abilityData;});
      await page.evaluate(async()=>menus.regroup('ability.receiver','ability.donor'));
      const teaching=page.locator('.regroup-ability[data-ability="demo.air"]');
      check(await teaching.getAttribute('data-before')==='2'&&await teaching.getAttribute('data-value')==='4','real teaching preview includes new ability');
      await page.screenshot({path:path.join(output,width+'-teaching.png')});
      report.views.push({viewport:{width,height},source,panel:bounds});await context.close();
    }
    check(!report.errors.length&&!report.missing.length,'no script or resource errors');
    await fs.writeFile(path.join(output,'verification.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
