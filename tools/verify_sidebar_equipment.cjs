'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5505',folder=path.resolve(process.argv[3]||'build/sidebar-equipment-20261024/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,layouts:[],errors:[],missing:[]};
  const check=(ok,label)=>{assert.ok(ok,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const native of [false,true]){
      const page=await browser.newPage({hasTouch:native});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      if(native)await page.route('https://ew4.local/**',async route=>{
        const url=new URL(route.request().url()),headers={...route.request().headers()};
        if(headers.origin)headers.origin=new URL(base).origin;
        const response=await route.fetch({url:new URL(url.pathname+url.search,base).href,headers});await route.fulfill({response});
      });
      await page.goto(native?'https://ew4.local/index.html':base);await page.waitForFunction(()=>startupReady);
      for(const [width,height] of [[2376,1080],[1920,873],[844,390],[390,844],[1280,800]])for(const screen of [80,100,150]){
        const label=[native?'Android':'Web',width,height,screen].join(' ');
        await page.setViewportSize({width,height});
        await page.evaluate(screen=>{showMain();settings.screen=screen;sizeUi();},screen);
        await page.locator('[data-mode="2"]').click();await page.waitForFunction(()=>!conquestEntering);
        await page.evaluate(()=>chooseScenario(catalog.scenarios.find(s=>s.id==='conquest1')));
        await page.waitForTimeout(650);
        check(await page.locator('#country-list').evaluate(el=>Math.abs(el.getBoundingClientRect().right-innerWidth)<.1),label+' country list touches screen right');
        for(const index of [0,1,-1]){
          const row=index<0?page.locator('#country-list .country-entry').last():page.locator('#country-list .country-entry').nth(index);
          await row.click();
          check(await row.evaluate(el=>{
            const a=el.getBoundingClientRect(),b=el.parentElement.getBoundingClientRect();
            const scrollRounding=a.height/el.offsetHeight;
            return Math.abs(a.left-b.left)<.1&&Math.abs(a.right-b.right)<.1&&a.top>=b.top-scrollRounding&&a.bottom<=b.bottom+scrollRounding;
          }),label+' selected country fills sidebar and scrolls into view '+index);
          check(await row.evaluate(el=>[...el.children].every(n=>{
            const a=n.getBoundingClientRect(),b=el.getBoundingClientRect();return a.left>=b.left-.1&&a.right<=b.right+.1;
          })),label+' selected flag and name fit');
        }
        if(screen===100){
          await page.locator('#country-list .country-entry').first().click();
          await page.screenshot({path:path.join(folder,`${native?'android':'web'}-${width}x${height}-countries.png`)});
        }
        await page.evaluate(()=>{showMain();return menus.headquarters();});
        await page.evaluate(()=>menus.equipment(menus.data.roster[0].id));
        await page.waitForTimeout(350);
        await page.locator('[data-native-id=lbox_equipitem] button:not(:disabled)').first().click();
        const geometry=await page.evaluate(()=>{
          const l=$('detail-content').nativeLayout,box=l.get('group_desc').getBoundingClientRect(),button=l.get('btn_equip').getBoundingClientRect(),text=l.get('text_desc').getBoundingClientRect(),scale=l.body.getBoundingClientRect().width/l.body.offsetWidth;
          return {centerError:Math.abs(button.x+button.width/2-box.x-box.width/2),width:button.width/scale,height:button.height/scale,
            contained:button.x>=box.x&&button.right<=box.right&&button.y>=box.y&&button.bottom<box.bottom,
            clear:text.bottom<button.top,scroll:getComputedStyle(l.get('text_desc')).overflowY};
        });
        check(geometry.centerError<.1,label+' equipment button centered');
        check(Math.abs(geometry.width-83)<.1&&Math.abs(geometry.height-30)<.1,label+' equipment button enlarged');
        check(geometry.contained&&geometry.clear&&geometry.scroll==='auto',label+' description and button stay separate');
        check(await page.locator('[data-native-id=btn_equip]').isEnabled(),label+' unequip available');
        if(screen===100)await page.screenshot({path:path.join(folder,`${native?'android':'web'}-${width}x${height}-equipment.png`)});
        if(screen===100&&width===844){
          check(await page.evaluate(()=>{
            const l=$('detail-content').nativeLayout;
            l.text('text_desc','提高周围部队的补给和作战能力。'.repeat(20));
            const text=l.get('text_desc');text.scrollTop=text.scrollHeight;
            return text.scrollHeight>text.clientHeight&&text.scrollTop>0;
          }),label+' long item description remains scrollable');
        }
        report.layouts.push({native,viewport:{width,height},screen,equipment:geometry});
        await page.locator('#close-detail').click();
        await page.evaluate(()=>showMain());await page.locator('[data-mode="1"]').click();
        await page.evaluate(()=>menus.openChapter(catalog.chapters.find(c=>c.id===5)));
        await page.waitForTimeout(650);
        check(await page.locator('#campaign-screen>.menu-bar').evaluate(el=>{
          const r=el.getBoundingClientRect();return Math.abs(r.left)<.1&&Math.abs(r.right-innerWidth)<.1&&Math.abs(r.top)<.1;
        }),label+' campaign header spans screen');
        check(await page.locator('#campaign-back').evaluate(el=>Math.abs(el.getBoundingClientRect().left)<.1),label+' campaign back touches screen left');
        check(await page.locator('#campaign-battles').evaluate(el=>Math.abs(el.getBoundingClientRect().right-innerWidth)<.1),label+' campaign list touches screen right');
        if(screen===100)await page.screenshot({path:path.join(folder,`${native?'android':'web'}-${width}x${height}-campaign.png`)});
      }
      // Exercise both transactions on the isolated migrated account.
      await page.evaluate(()=>menus.equipment(menus.data.roster[0].id));
      await page.locator('[data-native-id=lbox_equipitem] button:not(:disabled)').first().click();
      const item=await page.evaluate(()=>$('detail-content').nativeLayout.get('btn_equip').getAttribute('aria-label').replace(/^卸下 /,''));
      await page.locator('[data-native-id=btn_equip]').click();
      await page.locator('[data-native-id=grid_items]').getByRole('button',{name:item,exact:true}).click();
      await page.locator('[data-native-id=btn_equip]').click();
      check(await page.locator('[data-native-id=lbox_equipitem]').getByRole('button',{name:item,exact:true}).isEnabled(),'real unequip/equip round trip '+native);
      await page.locator('#close-detail').click();await page.evaluate(()=>showMain());
      await page.locator('[data-mode="1"]').click();await page.locator('.chapter-entry').first().click();await page.locator('.chapter-preview-confirm').click();
      await page.waitForTimeout(650);
      check(await page.locator('#campaign-battles [aria-pressed=true]').evaluate(el=>getComputedStyle(el).transform==='none'&&el.offsetWidth===160),'campaign selected row keeps existing layout '+native);
      check(await page.locator('#campaign-battles').evaluate(el=>Math.abs(el.getBoundingClientRect().right-innerWidth)<.1),'campaign list touches screen right '+native);
      await page.close();
    }
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing resources');
  }catch(error){report.failure=error.message;process.exitCode=1;}
  finally{await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,layouts:report.layouts.length,errors:report.errors.length,missing:report.missing.length,failure:report.failure}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
