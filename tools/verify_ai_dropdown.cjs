'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5586',folder=path.resolve(process.argv[3]||'build/apk-dropdown-20261027/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  const report={checks:0,errors:[],missing:[],views:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  try{
    for(const [width,height,touch] of [[1280,720,false],[844,390,true],[390,844,true],[568,320,true]]){
      const context=await browser.newContext({viewport:{width,height},hasTouch:touch,isMobile:touch});
      const page=await context.newPage();page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      const press=async selector=>{const item=page.locator(selector);if(touch)await item.tap();else await item.click();};
      const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition);
      await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});await page.evaluate(()=>start('conquest1','fra'));await idle();
      await press('#menu-button');await page.waitForTimeout(350);
      if(await page.evaluate(()=>$('pause').classList.contains('ai-compact')&&!$('pause').classList.contains('ai-sidebar-expanded')))await press('.ai-control-title');
      check(await page.locator('#ai-mission').evaluate(e=>e.tagName==='BUTTON'),'in-page selector, no native picker');
      check(await page.locator('.ai-mission-picker select').count()===0,'no select invokes Android chooser');
      await page.locator('#ai-mission').scrollIntoViewIfNeeded();
      const layout=()=>page.evaluate(()=>({top:$('ai-policy').offsetTop,height:$('ai-control').offsetHeight}));
      const before=await layout();await press('#ai-mission');
      check(await page.locator('#ai-mission-options').isVisible(),'opens with pointer '+width);
      check(await page.locator('#ai-mission').getAttribute('aria-expanded')==='true','expanded accessibility state');
      const geometry=await page.evaluate(()=>{
        const trigger=$('ai-mission').getBoundingClientRect(),list=$('ai-mission-options').getBoundingClientRect(),options=[...$('ai-mission-options').children];
        return {below:list.top>=trigger.bottom-1,width:Math.abs(list.width-trigger.width)<1,left:Math.abs(list.left-trigger.left)<1,
          allClickable:options.every(o=>{const r=o.getBoundingClientRect();return o.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));})};
      });check(geometry.below&&geometry.width&&geometry.left&&geometry.allClickable,'anchored below, all four options reachable '+JSON.stringify(geometry));
      const after=await layout();check(before.top===after.top&&before.height===after.height,'menu does not push controls '+JSON.stringify({before,after}));
      await page.evaluate(()=>{window.dropdownFirstOption=$('ai-mission-options').firstElementChild;aiControl.sync();});
      check(await page.evaluate(()=>!$('ai-mission-options').hidden&&window.dropdownFirstOption===$('ai-mission-options').firstElementChild),'sync retains menu and option elements');
      await page.screenshot({path:path.join(folder,'dropdown-'+width+'x'+height+'.png')});
      await press('#ai-mission-options [data-kind="defend"]');await idle();
      check(await page.evaluate(()=>state.playerAi.draft.kind==='defend'),'selection updates actual task');
      check(await page.locator('#ai-mission-options').isHidden()&&await page.locator('#ai-mission').getAttribute('value')==='defend','selection closes and updates label');
      await press('#ai-mission');await press('#pause>.pause-dialog h2');
      check(await page.locator('#ai-mission-options').isHidden(),'outside tap closes');
      check(await page.evaluate(()=>state.playerAi.draft.kind==='defend'),'outside tap keeps selected task');
      await press('#ai-mission');await press('#ai-mission');check(await page.locator('#ai-mission-options').isHidden(),'trigger toggles closed');
      await press('#ai-mission');check(await page.evaluate(()=>window.ew4Back())==='handled','Android back handled');
      check(await page.locator('#ai-mission-options').isHidden()&&await page.locator('#pause').isVisible(),'Android back closes only dropdown');
      await page.locator('#ai-mission').focus();await page.keyboard.press('ArrowDown');await page.keyboard.press('End');await page.keyboard.press('Enter');await idle();
      check(await page.evaluate(()=>state.playerAi.draft.kind==='line_defend'),'keyboard selects last option');
      await press('#ai-mission');await page.keyboard.press('Escape');check(await page.locator('#ai-mission-options').isHidden(),'Escape closes');
      await press('#ai-mission');await page.locator('#ai-mission-options [data-kind="attack"]').click();await idle();
      check(await page.evaluate(()=>state.playerAi.draft.kind==='attack'),'first option can be restored');
      report.views.push({width,height,touch});await context.close();
    }
    check(!report.errors.length&&!report.missing.length,'no script or resource errors');
  }finally{await browser.close();await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2)+'\n');}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
