'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5481',folder=path.resolve(process.argv[3]||'build/commander-capacity-20261023/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}}),report={checks:0,errors:[],missing:[],screenshots:[]};
  const check=(pass,label)=>{assert.ok(pass,label);report.checks++;};
  const noticeText=()=>page.locator('#notice').evaluate(el=>el.querySelector('.original-text')?.getAttribute('aria-label')||el.textContent);
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    await page.evaluate(()=>menus.headquarters());
    for(let group=0;group<3;group++){
      await page.evaluate(async group=>{
        const section=d=>Math.max(0,Math.min(2,Math.floor((d.rating-1)/3)));
        const call=async args=>{menus.data=await rpc('commander',{revision:menus.data.revision,...args});};
        while(menus.data.roster.filter(c=>section(menus.data.definitions.find(d=>d.id===c.definitionId))===group).length<20){
          if(!menus.data.offers[group].length)await call({op:'refresh',group});
          const id=menus.data.offers[group][0],d=menus.data.definitions.find(d=>d.id===id);
          await call({op:d.price>0?'recruit':'recruit_badge',commander:id,group});
        }
        if(!menus.data.offers[group].length)await call({op:'refresh',group});
        menus.recruiting=true;menus.rating=group;menus.offer=menus.data.definitions.find(d=>d.id===menus.data.offers[group][0]);menus.drawRoster();
      },group);
      const before=await page.evaluate(()=>JSON.stringify(menus.data));
      const currencies=await page.locator('#hq-buy-buttons button').count();
      for(let i=0;i<currencies;i++)for(let retry=0;retry<2;retry++){
        await page.evaluate(()=>{$('notice').hidden=true;});
        await page.locator('#hq-buy-buttons button').nth(i).click();
        await page.waitForFunction(()=>!menus.recruitPending&&!$('notice').hidden);
        const message=await noticeText();
        check(message==='该星级区将领已达上限，无法购买','exact capacity message, group '+group+' currency '+i+' retry '+retry+': '+message);
        check(await page.evaluate(()=>!!menus.offer)&&await page.locator('#hq-buy-buttons button:enabled').count()===currencies,'failed purchase retains selection and retry controls');
        check(await page.evaluate(()=>JSON.stringify(menus.data))===before,'failed purchase preserves currencies, roster, offers and revision');
      }
      let requests=0;
      const track=request=>{if(request.url().includes('/api/commander'))requests++;};page.on('request',track);
      await page.evaluate(()=>{for(const b of document.querySelectorAll('#hq-buy-buttons button'))b.click();});
      await page.waitForFunction(()=>!menus.recruitPending);page.off('request',track);
      check(requests===1,'simultaneous currency clicks submit once, group '+group);
      check(await noticeText()==='该星级区将领已达上限，无法购买','simultaneous clicks preserve localized message');
      if(group===0){
        await page.evaluate(()=>{
          menus.rating=1;menus.offer=menus.data.definitions.find(d=>d.id===menus.data.offers[1][0]);menus.drawRoster();
        });
        const owned=await page.evaluate(()=>menus.data.roster.length);
        await page.locator('#hq-buy-buttons button').first().click();await page.waitForFunction(()=>!menus.recruitPending&&menus.offer===null);
        check(await page.evaluate(()=>menus.data.roster.length)===owned+1,'another section can purchase after a failed full-section purchase');
        check(await page.locator('#hq-buy-buttons').count()===0,'successful purchase clears selection and buttons');
      }
    }
    for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844]]){
      await page.setViewportSize({width,height});
      await page.evaluate(()=>{menus.recruiting=true;menus.rating=2;menus.offer=menus.data.definitions.find(d=>d.id===menus.data.offers[2][0]);menus.drawRoster();});
      await page.locator('#hq-buy-buttons button').last().click();await page.waitForFunction(()=>!menus.recruitPending&&!$('notice').hidden);
      check(await page.locator('#notice').evaluate(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1&&el.scrollWidth<=el.clientWidth+1;}),'capacity notice fits '+name);
      await page.waitForFunction(()=>[...document.querySelectorAll('#hq-roster img')].every(i=>i.complete&&i.naturalWidth));
      await page.screenshot({path:path.join(folder,name+'-capacity.png')});report.screenshots.push(name+'-capacity.png');
    }
    await page.evaluate(()=>{menus.recruiting=false;menus.data.sectionCapacities=[0,7,25];});
    for(const [rating,slots,denominator] of [[null,60,32],[0,20,0],[1,20,7],[2,25,25]]){
      await page.evaluate(rating=>{menus.rosterRating=rating;menus.drawRoster();},rating);
      check(await page.locator('#hq-roster .commander-card').count()===slots,'configured slots preserve existing over-limit roster '+rating);
      check(await page.locator('#hq-count .original-text').getAttribute('aria-label')===(rating===null?60:20)+' / '+denominator,'counter uses configured capacity '+rating);
    }
    const status=await (await page.request.get(base+'/api/mods')).json(),mod=status.mods.find(m=>m.id==='commander-capacity-test');
    if(mod){
      const activate=async key=>{
        const response=await page.request.post(base+'/api/mods',{data:{op:'activate',key}}),result=await response.json();
        check(!result.error,'activate test content');await page.reload();await page.waitForFunction(()=>startupReady);await page.evaluate(()=>menus.headquarters());
      };
      try{
        await activate(mod.key);
        check(await page.evaluate(()=>JSON.stringify(menus.data.sectionCapacities))==='[30,25,40]','packed and imported Mod limits reach real API');
        for(const [rating,slots] of [[null,95],[0,30],[1,25],[2,40]]){
          await page.evaluate(rating=>{menus.rosterRating=rating;menus.drawRoster();},rating);
          check(await page.locator('#hq-roster .commander-card').count()===slots,'imported Mod renders configured slots '+rating);
        }
        await page.setViewportSize({width:1280,height:720});
        await page.screenshot({path:path.join(folder,'imported-mod-capacity.png')});report.screenshots.push('imported-mod-capacity.png');
      }finally{await activate(null);}
    }
    check(report.errors.length===0,'no script errors');check(report.missing.length===0,'no missing assets');
  }finally{await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
