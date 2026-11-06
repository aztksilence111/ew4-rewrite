'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56183',folder=path.resolve(process.argv[3]||'build/save-features-qa-20261105/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  let checks=0;const errors=[],check=(ok,label)=>{assert(ok,label);checks++;};
  const text=locator=>locator.evaluate(e=>[e.textContent,...[...e.querySelectorAll('.original-text[aria-label]')].map(n=>n.getAttribute('aria-label'))].join(''));
  try{
    for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      const page=await browser.newPage({viewport});page.on('pageerror',e=>errors.push(e.message));
      await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(async()=>{const view=await rpc('start',{scenario:'conquest1',country:'fra'});await rpc('leave',{revision:view.revision});});
      await page.locator('#main-settings').click();
      for(const id of ['save-export','save-import','save-manager']){
        check(await page.locator('#'+id).isVisible(),id+' visible');
        check(await page.locator('#'+id).evaluate(e=>e.scrollWidth<=e.clientWidth+1),'control text fits');
      }
      await page.locator('#save-manager').click();await page.locator('.save-retention').last().waitFor();
      check(await page.locator('.save-retention').count()===8,'eight backup slots');
      check((await text(page.locator('.save-manager-identity'))).includes('原版'),'current Mod named');
      check(await page.locator('.save-manager-dialog').evaluate(e=>{const r=e.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1;}),'manager fits viewport');
      const first=page.locator('.save-retention').first();await first.getByRole('button',{name:'保存',exact:true}).click();
      await page.locator('#save-manager-modal').getByRole('button',{name:'取消',exact:true}).click();
      await first.getByRole('button',{name:'保存',exact:true}).click();
      await page.locator('#save-manager-modal').getByRole('button',{name:'确认',exact:true}).click();
      await page.waitForFunction(()=>!document.getElementById('save-manager-modal').hasAttribute('aria-busy'));
      await page.waitForFunction(()=>document.querySelector('.save-retention .save-retention-actions button:nth-child(2)')?.disabled===false);
      check(await page.locator('.save-retention').first().getByRole('button',{name:'加载',exact:true}).isEnabled(),'saved backup loadable');
      await page.screenshot({path:path.join(folder,'manager-'+viewport.width+'.png')});
      await page.locator('#save-manager-modal .close-icon').click();
      await page.waitForFunction(()=>document.getElementById('save-manager-modal').hidden);
      await page.locator('#close-settings').click();
      await page.evaluate(()=>menus.saveMenu(false,2));
      check(await page.locator('.save-mod-name').count()===7,'every battle slot names its Mod');
      check(await page.locator('.save-mod-name').evaluateAll(es=>es.every(e=>e.title==='原版')),'base saves labelled');
      await page.locator('#close-detail').click();
      await page.close();
    }
    const page=await browser.newPage({viewport:{width:1280,height:720}});page.on('pageerror',e=>errors.push(e.message));
    await page.goto(base);await page.waitForFunction(()=>startupReady);
    await page.evaluate(()=>start('conquest1','fra'));await page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition,null,{timeout:90000});
    if(await page.evaluate(()=>stageIntroOpen))await page.locator('#detail-confirm').click();
    for(let i=0;i<16;i++){
      const pending=await page.evaluate(()=>({story:!!state.story,dialogue:!!state.dialogue}));if(!pending.story&&!pending.dialogue)break;
      if(pending.story){await page.waitForFunction(()=>!storyPlayer.preparing&&!storyPlayer.commandPending);await page.locator('#story-player button[aria-label="跳过剧情"]').click();}
      else await page.evaluate(()=>act('dialogue'));
      await page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition,null,{timeout:90000});
    }
    await page.locator('#open-diplomacy').click();await page.waitForFunction(()=>diplomacyUi.snapshot&&!diplomacyUi.dialog.hasAttribute('aria-busy'));
    check(await page.locator('[data-op="purchase_surrender"]').count()>0,'paid surrender actions available');
    check(await page.locator('[data-op="demand_surrender"]').count()>0,'probability option retained');
    await page.evaluate(()=>{const f=state.factions.find(f=>f.relation==='hostile'&&!f.defeated),q=diplomacyUi.snapshot.factions.find(d=>d.id===f.id).purchase;diplomacyUi.ask(f,q,'purchase_surrender');});
    check((await text(page.locator('.diplomacy-confirm'))).includes('存活单位造价合计'),'quote itemizes unit cost');
    check((await text(page.locator('.diplomacy-confirm'))).includes('/ 工'),'quote has industrial payment');
    for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(viewport);await page.screenshot({path:path.join(folder,'surrender-'+viewport.width+'.png')});
      check(await page.locator('.diplomacy-confirm').evaluate(e=>e.clientWidth>=e.scrollWidth-1),'surrender copy wraps');
    }
    await page.evaluate(()=>{diplomacyUi.close(false);openSettings();});
    const response=await page.request.post(base+'/api/save-export'),archive=await response.body();check(response.ok(),'actual export endpoint');
    await page.locator('#save-import-file').setInputFiles({name:'export.zip',mimeType:'application/zip',buffer:archive});
    await page.locator('#save-manager-modal').getByRole('button',{name:'覆盖并导入',exact:true}).waitFor();
    check((await text(page.locator('.save-manager-message'))).includes('恢复副本'),'conflict preview explains retained original');
    await page.locator('#save-manager-modal').getByRole('button',{name:'取消',exact:true}).click();
    await page.locator('#save-import-file').setInputFiles({name:'export.zip',mimeType:'application/zip',buffer:archive});
    await page.locator('#save-manager-modal').getByRole('button',{name:'覆盖并导入',exact:true}).click();
    await page.waitForFunction(()=>typeof startupReady!=='undefined'&&startupReady&&document.getElementById('play').hidden&&document.getElementById('save-manager-modal').hidden,null,{timeout:90000});
    const manager=await page.evaluate(()=>rpc('save-manager'));check(manager.slots[0].exists,'backup survived export and import');
    await page.locator('#main-settings').click();await page.locator('#save-manager').click();
    await page.locator('.save-retention').first().getByRole('button',{name:'加载',exact:true}).click();
    await page.locator('#save-manager-modal').getByRole('button',{name:'确认',exact:true}).click();
    await page.waitForFunction(()=>typeof startupReady!=='undefined'&&startupReady&&document.getElementById('save-manager-modal').hidden,null,{timeout:90000});
    check((await page.evaluate(()=>rpc('save-manager'))).slots[0].exists,'actual retention restore completes');
    await page.close();check(errors.length===0,errors.join('\n'));
    await fs.writeFile(path.join(folder,'result.json'),JSON.stringify({checks,errors},null,2));console.log(JSON.stringify({checks,errors}));
  }finally{await browser.close();}
})().catch(e=>{console.error(String(e.stack||e.message).split('\n').slice(0,3).join('\n'));process.exit(1);});
