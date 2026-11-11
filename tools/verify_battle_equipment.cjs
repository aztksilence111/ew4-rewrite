'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56234',out=path.resolve(process.argv[3]||'build/battle-equipment-20261111/browser');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const fixture=JSON.parse(await fs.readFile(path.resolve('build/battle-equipment-20261111/fixture.json'),'utf8'));
  const report={checks:0,errors:[],missing:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try{
    for(const [width,height,compat] of [[1920,873,false],[844,390,true],[390,844,true]]){
      const page=await browser.newPage({viewport:{width,height},hasTouch:compat});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      if(compat)await page.route('https://ew4.local/**',async route=>{
        const url=new URL(route.request().url()),headers={...route.request().headers()};
        if(headers.origin)headers.origin=new URL(base).origin;
        const response=await route.fetch({url:new URL(url.pathname+url.search,base).href,headers});await route.fulfill({response});
      });
      await page.goto(compat?'https://ew4.local/index.html':base);
      await page.waitForFunction(()=>startupReady&&document.documentElement.dataset.fontsReady==='true');
      await page.evaluate(()=>load(0,2));await page.waitForFunction(()=>state&&!busy);
      await page.evaluate(async fixture=>{await selectCell(fixture.cell,true);await openUnitInfo(state.units.find(u=>u.id===fixture.id));},fixture);
      await page.locator('[data-native-id="btn_info"]').click();
      check(await page.locator('.commander-ability>img').count()===8,'all native ability icons');
      const center=await page.locator('.commander-equipment').evaluate(el=>{const a=el.getBoundingClientRect(),b=el.querySelector('.equip').getBoundingClientRect();return Math.abs((b.top+b.bottom-a.top-a.bottom)/2)<2;});
      check(center,'battle item button centered like reference');
      await page.screenshot({path:path.join(out,`detail-${width}.png`)});
      await page.locator('.commander-equipment .equip').click();
      await page.waitForSelector('[data-native-id="grid_items"] [data-item-id="ew4.item.13"]');
      check(await page.locator('[data-native-id="btn_prev"]').isDisabled()&&await page.locator('[data-native-id="btn_next"]').isDisabled(),'original navigation icons retained in disabled state');
      const slot=page.locator('[data-native-id="lbox_equipitem"] [data-item-id="ew4.item.20"]');
      check(await slot.count()===1,'equipped item visible');await slot.click();
      check(await page.locator('[data-native-id="btn_equip"]').isEnabled(),'own deployed commander can unequip');
      const before=await page.evaluate(f=>({...state.units.find(u=>u.id===f.id)}),fixture);
      await page.locator('[data-native-id="btn_equip"]').click();
      await page.waitForFunction(()=>!menus.data.roster[0].items.includes('ew4.item.20'));
      await page.locator('[data-native-id="grid_items"] [data-item-id="ew4.item.20"]').click();
      await page.locator('[data-native-id="btn_equip"]').click();
      await page.waitForFunction(()=>menus.data.roster[0].items.includes('ew4.item.20'));
      check(await page.evaluate(({before,fixture})=>{const u=state.units.find(u=>u.id===fixture.id);return u.health===before.health&&u.remainingMovement===before.remainingMovement&&u.acted===before.acted;},{before,fixture}),'real roundtrip does not refill health or movement');
      await page.locator('[data-native-id="grid_items"] [data-item-id="ew4.item.13"]').click();
      const description=await page.locator('[data-native-id="text_desc"] .original-text').getAttribute('aria-label');
      check(/具体效果：恢复\d+点生命/.test(description)&&/当前部队实际恢复\d+点/.test(description),'original consumable text followed by rule and actual amount');
      check(await page.locator('[data-native-id="btn_equip"]').isEnabled(),'healing is available on damaged fixture');
      const geometry=await page.locator('[data-native-id="group_desc"]').evaluate(el=>{const b=el.querySelector('[data-native-id="btn_equip"]'),t=el.querySelector('[data-native-id="text_desc"]'),a=el.getBoundingClientRect(),br=b.getBoundingClientRect(),tr=t.getBoundingClientRect();return br.bottom<=a.bottom&&tr.bottom<=br.top&&getComputedStyle(t).overflowY==='auto';});
      check(geometry,'scrollable text and action do not overlap');
      await page.screenshot({path:path.join(out,`items-${width}.png`)});
      if(width===390){
        await page.locator('[data-native-id="btn_equip"]').click();
        await page.waitForFunction(f=>state.units.find(u=>u.id===f.id).health>f.health,{...fixture,health:before.health});
        await page.waitForFunction(()=>menus.data.items.find(i=>i.id==='ew4.item.13').quantity===2&&document.querySelector('[data-native-id="grid_items"] [data-item-id="ew4.item.13"]'));
        check(true,'consumable uses real effect from detail page');
      }
      await page.locator('#close-detail').click();
      check(await page.locator('.commander-equipment-slots img').count()===2,'return shows refreshed equipment');
      await page.locator('#close-detail').click();await page.waitForSelector('[data-native-id="btn_info"]');
      await page.locator('#close-detail').click();
      await page.evaluate(async()=>{const u=state.units.find(u=>u.owner!==state.playerFaction&&u.commanderId);await openUnitInfo(u);});
      await page.locator('[data-native-id="btn_info"]').click();await page.locator('.commander-equipment .equip').click();
      check(await page.locator('[data-native-id="btn_equip"]').isHidden(),'enemy equipment remains read only');
      await page.close();
    }
    check(report.errors.length===0,'no browser errors');check(report.missing.length===0,'no missing resources');
  }catch(e){report.failure=e.message;process.exitCode=1;}
  finally{await browser.close();await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
