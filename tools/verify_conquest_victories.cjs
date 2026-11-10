'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56230',out=path.resolve(process.argv[3]||'build/conquest-victories-qa-20261110/browser');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[]},check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try{
    for(const compat of [false,true]){
      const context=await browser.newContext({viewport:{width:1280,height:720}}),page=await context.newPage();
      if(!compat)await page.route(/\/compat\/[^/]+\.js(?:\?|$)/,route=>route.fulfill({path:path.resolve('rewrite/Ew4.Web/wwwroot',new URL(route.request().url()).pathname.split('/').pop())}));
      let victories={},catalogRequests=0;
      await page.route('**/api/catalog?*',async route=>{
        const response=await route.fetch(),body=await response.json();catalogRequests++;
        body.data.conquestVictories=victories;
        // Exercise the same custom-country flag path used by imported Mods.
        body.data.countries.sample={name:'示例国',flagImage:'rewrite/ui/sample-win.png'};
        await route.fulfill({response,json:body});
      });
      await page.route(/\/content\/(?:r-[^/]+\/)?extensions\/ui\/sample-win\.png$/,async route=>{
        const response=await page.request.get(base+'/content/ui/image_flag_hd/fra1.png');
        await route.fulfill({response});
      });
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push([r.url(),r.status()]);});
      await page.goto(base);await page.waitForFunction(()=>typeof catalog!=='undefined'&&catalog&&startupReady&&document.documentElement.dataset.fontsReady==='true');
      const open=async()=>{await page.locator('[data-mode="2"]').click();await page.waitForFunction(()=>!conquestEntering&&!document.getElementById('scenario-menu').hidden);};
      const flags=id=>page.locator('[data-scenario="'+id+'"] .conquest-victories img');
      await open();
      check(await page.locator('.conquest-victories').count()===0,'fresh account shows no flags');
      await page.locator('#menu-back').click();
      victories={conquest1:['aus','gbr'],conquest2:['usa'],conquest3:['den','swe'],conquest4:['fra','tur'],conquest5:['spa'],conquest6:['pru']};
      const before=catalogRequests;await open();
      check(catalogRequests>before,'opening conquest refreshes account progression');
      check((await flags('conquest1').evaluateAll(rows=>rows.map(r=>r.dataset.country))).join(',')==='aus,gbr','countries stay on the matching conquest card');
      for(const [width,height] of [[1920,1080],[1280,720],[844,390],[390,844]]){
        await page.setViewportSize({width,height});
        await page.waitForFunction(()=>[...document.querySelectorAll('.conquest-victories img')].every(i=>i.complete&&i.naturalWidth>0));
        const geometry=await page.locator('#scenarios button').evaluateAll(rows=>rows.map(row=>{
          const card=row.getBoundingClientRect(),line=row.querySelector('.conquest-line').getBoundingClientRect();
          return [...row.querySelectorAll('.conquest-victories img')].every(img=>{
            const flag=img.getBoundingClientRect();return flag.left>=card.left&&flag.right<=card.right&&flag.top>=line.bottom-.1&&flag.bottom<=card.bottom
              &&flag.width>0&&flag.height>0&&img.alt.includes('已通关');
          });
        }));
        check(geometry.every(Boolean),'flags fit below caption within all cards '+width+'x'+height);
        if(width===1280||width===390)await page.screenshot({path:path.join(out,(compat?'compat':'source')+'-'+width+'.png')});
      }
      await page.locator('[data-scenario="conquest1"]').click();await page.locator('#countries').waitFor();
      check(await page.evaluate(()=>chosenScenario==='conquest1'&&countryChoices.length>0),'flag overlay preserves card selection');
      await page.locator('#cancel-country').click();
      await page.evaluate(()=>{conquestPages.pages[1].slots=[{scenario:'conquest6'},null];turnConquestPage(1);});
      await page.waitForFunction(()=>!conquestEntering);
      check(await flags('conquest6').count()===1&&await page.locator('.scenario-placeholder .conquest-victories').count()===0,'later pages show flags only on populated slots');
      await page.locator('#menu-back').click();
      victories={conquest1:['sample']};await open();
      await page.locator('#conquest-prev').click();await page.waitForFunction(()=>!conquestEntering);
      check((await flags('conquest1').getAttribute('src')).endsWith('/extensions/ui/sample-win.png'),'Mod flag image uses extension resource path');
      await page.waitForFunction(()=>document.querySelector('.conquest-victories img').naturalWidth>0);
      await page.locator('#menu-back').click();victories={};await open();
      check(await page.locator('.conquest-victories').count()===0,'account refresh removes flags from another namespace');
      await page.evaluate(()=>{catalog.conquestVictories={conquest1:Object.keys(catalog.countries).filter(id=>id!=='sample')};scenarioList();});
      await page.waitForFunction(()=>!conquestEntering);
      check(await flags('conquest1').evaluateAll(rows=>{
        const host=rows[0].parentElement.getBoundingClientRect();return rows.every(img=>{const r=img.getBoundingClientRect();return r.width>0&&r.left>=host.left-.1&&r.right<=host.right+.1;});
      }),'many completed countries stay inside the flag strip');
      await page.locator('#menu-back').click();await page.locator('[data-mode="1"]').click();await page.locator('.chapter-entry').first().waitFor();
      check(await page.locator('.conquest-victories').count()===0,'campaign menu remains independent');
      await context.close();
    }
    check(report.errors.length===0,'no browser exceptions');check(report.missing.length===0,'no missing resources');
    console.log(JSON.stringify(report));
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(path.join(out,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
})();
