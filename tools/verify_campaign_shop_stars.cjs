'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright');
const base=process.argv[2]||'http://127.0.0.1:57942';
const folder=path.resolve(process.argv[3]||'build/campaign-shop-stars-20261101');
(async()=>{
  const account=JSON.parse(await fs.readFile(path.join(folder,'checks/shop/account.json'),'utf8'));
  Object.assign(account,{revision:0,technologyStars:0,campaignStars:{},campaignTechnology:{},inventory:{},medals:100,
    headquartersStock:{id:'headquarters',offers:[{id:'headquarters.0',itemId:'ew4.item.53',remaining:30}]}});
  await fs.mkdir(path.join(folder,'browser-profile'),{recursive:true});
  await fs.writeFile(path.join(folder,'browser-profile/account.json'),JSON.stringify(account));
  const report={checks:0,errors:[],missing:[]};
  const check=(pass,label)=>{assert.ok(pass,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try{
    for(const compatible of [false,true]){
      const page=await browser.newPage();
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(!compatible)await page.route(base+'/',async route=>{
        const response=await route.fetch();await route.fulfill({response,body:(await response.text()).replace(/((?:src|href)=")compat\//g,'$1')});
      });
      await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:120000});
      const cost=compatible?12:6;
      const bought=await page.evaluate(async count=>{
        let shop=await rpc('commerce',{cell:-1});
        for(let i=0;i<count;i++)shop=await rpc('commerce',{cell:-1,op:'buy',offer:'headquarters.0',revision:shop.revision});
        return rpc('technology',{group:0});
      },cost);
      check(bought.stars===cost&&bought.entries[0].canUpgrade,'shop purchase enables technology '+compatible);
      await page.locator('.main-entry[data-mode="1"]').click();
      await page.waitForFunction(()=>!menus.pageTransition&&document.querySelectorAll('.chapter-entry').length>0);
      await page.evaluate(()=>menus.openChapter(catalog.chapters[0]));
      await page.locator('#campaign-upgrade').click();
      const body=page.locator('.campaign-technology');await body.waitFor();
      for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
        await page.setViewportSize(viewport);
        await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
        check(await body.getAttribute('data-stars')===String(cost),'correct visible shop medal balance '+viewport.width);
        check(await body.locator('[data-technology-upgrade]').isEnabled(),'upgrade enabled '+viewport.width);
        const geometry=await body.evaluate(async host=>{
          await Promise.all([...host.querySelectorAll('img')].map(img=>img.decode()));
          const r=host.parentElement.getBoundingClientRect();
          return {rect:{left:r.left,top:r.top,right:r.right,bottom:r.bottom},fits:r.left>=-1&&r.top>=-1&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1,
            images:[...host.querySelectorAll('img')].every(img=>img.naturalWidth>0)};
        });
        check(geometry.fits&&geometry.images,'dialog fits and assets load '+viewport.width+' '+JSON.stringify(geometry));
        await page.screenshot({path:path.join(folder,(compatible?'compat':'source')+'-'+viewport.width+'.png')});
      }
      await body.locator('[data-technology-upgrade]').click();
      await page.waitForFunction(()=>document.querySelector('.campaign-technology').dataset.stars==='0');
      const after=await page.evaluate(()=>rpc('technology',{group:0}));
      check(after.stars===0&&after.entries[0].level===bought.entries[0].level+1,'real UI pays exact shop medals '+compatible);
      await page.reload();await page.waitForFunction(()=>startupReady,null,{timeout:120000});
      check((await page.evaluate(()=>rpc('technology',{group:0}))).stars===0,'reload retains payment '+compatible);
      check((await page.evaluate(()=>rpc('commerce',{cell:-1}))).items.find(i=>i.id==='ew4.item.53').quantity===0,'inventory payment persists '+compatible);
      await page.close();
    }
    check(!report.errors.length&&!report.missing.length,'no browser or asset errors');
    console.log(JSON.stringify(report));
  }finally{await fs.writeFile(path.join(folder,'browser.json'),JSON.stringify(report,null,2));await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
