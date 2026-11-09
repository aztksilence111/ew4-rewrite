'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56209',out=path.resolve(process.argv[3]||'build/regroup-scroll-20261109/browser');
const override=process.argv[4];
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try{
    for(const compat of [false,true]){
      const context=await browser.newContext({hasTouch:true}),page=await context.newPage();
      // Route only frontend assets; accounts live in the caller's isolated server profile.
      await page.route(/\/(?:compat\/)?(?:menus\.js|commander-workflows\.js|original-ui\.css)(?:\?|$)/,route=>{
        const name=new URL(route.request().url()).pathname.split('/').pop();
        const folder=override||path.resolve('rewrite/Ew4.Web/wwwroot',compat?'compat':'');
        return route.fulfill({path:path.join(folder,name)});
      });
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base);await page.waitForFunction(()=>typeof startupReady!=='undefined'&&startupReady&&typeof menus!=='undefined'&&menus&&document.documentElement.dataset.fontsReady==='true');
      await page.evaluate(async()=>{
        await menus.headquarters();
        if(!menus.data.roster.length)await menus.updateAccount({op:'claim',offer:menus.data.princesses.find(p=>!p.claimed).id});
        const held=structuredClone(menus.data.roster[0]),rpc=menus.api.rpc;
        window.regroupFixture={held,rpc,account:JSON.stringify(menus.data),calls:0};
        held.locked=false;
        menus.api.rpc=async(kind,args)=>{
          if(kind==='commander'&&args.op==='preview_inherit'){
            regroupFixture.calls++;return {price:0,learning:structuredClone(held.learning)};
          }
          return rpc(kind,args);
        };
        menus.data.roster=[held,...Array.from({length:18},(_,i)=>({...structuredClone(held),id:'scroll.'+i,locked:i===5}))];
      });
      const selector='[data-layout=form_regroup] [data-native-id=lbox_general]',list=page.locator(selector);
      for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844]]){
        await page.setViewportSize({width,height});
        await page.evaluate(()=>menus.regroup(regroupFixture.held.id));
        await page.evaluate(async()=>{await Promise.all(document.getAnimations().map(a=>a.finished.catch(()=>{})));});
        check(await list.locator('.commander-card').count()===17,'excludes receiver and locked donors');
        const geometry=await list.evaluate(e=>{
          const r=e.getBoundingClientRect();return {x:r.left+r.width*.72,y:r.top+r.height*.5,scale:r.width/e.offsetWidth,
            fits:r.left>=-1&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1,overflow:e.scrollWidth>e.clientWidth};
        });
        check(geometry.fits&&geometry.overflow,name+' list fits and overflows horizontally');
        const {x,y,scale}=geometry,before=await page.evaluate(()=>regroupFixture.calls);
        const drag=async(from,to)=>{await page.mouse.move(from,y);await page.mouse.down();await page.mouse.move(to,y,{steps:12});await page.mouse.up();};
        await drag(x,x-180*scale);
        check(await list.evaluate(e=>e.scrollLeft>150),name+' mouse drag scrolls left');
        check(await page.evaluate(n=>regroupFixture.calls===n,before),name+' mouse drag never selects donor');
        await drag(x-180*scale,x);
        check(await list.evaluate(e=>e.scrollLeft<2),name+' mouse drag scrolls right');
        const cdp=await context.newCDPSession(page);
        for(const direction of [-1,1]){
          const start=direction<0?x:x-160*scale;
          await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:start,y}]});
          for(let i=1;i<=12;i++)await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:start+direction*i*160*scale/12,y}]});
          await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
          check(await list.evaluate((e,d)=>d<0?e.scrollLeft>120:e.scrollLeft<2,direction),name+' touch swipe '+direction);
        }
        check(await page.evaluate(n=>regroupFixture.calls===n,before),name+' touch swipe never selects donor');
        await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y}]});
        await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:x-80*scale,y}]});
        await cdp.send('Input.dispatchTouchEvent',{type:'touchCancel',touchPoints:[]});
        check(await list.evaluate(e=>!e.classList.contains('dragging')),name+' cancelled touch releases drag');
        await cdp.detach();
        await list.evaluate(e=>{e.scrollLeft=e.scrollWidth;});
        check(await list.evaluate(e=>{const r=e.getBoundingClientRect(),last=e.lastElementChild.getBoundingClientRect();return last.left>=r.left-1&&last.right<=r.right+1;}),name+' last donor fully reachable');
        const position=await list.evaluate(e=>e.scrollLeft);
        await list.locator('.commander-open').last().click();
        await page.waitForFunction(()=>document.querySelector('[data-native-id=lbox_general] .commander-card:last-child')?.getAttribute('aria-pressed')==='true');
        check(await list.evaluate((e,x)=>Math.abs(e.scrollLeft-x)<2,position),name+' selecting donor preserves scroll');
        check(await page.evaluate(n=>regroupFixture.calls===n+1,before),name+' click selects once');
        await page.locator('[data-native-id=btn_regroup]').click();
        await page.locator('#close-detail').click();
        check(await list.evaluate((e,x)=>Math.abs(e.scrollLeft-x)<2,position),name+' cancelling confirmation preserves scroll');
        await page.evaluate(async()=>{await Promise.all(document.getAnimations().map(a=>a.finished.catch(()=>{})));});
        await page.screenshot({path:path.join(out,(compat?'compat':'source')+'-'+name+'.png')});
      }
      await page.evaluate(async()=>{menus.data.roster=menus.data.roster.slice(0,2);await menus.regroup(regroupFixture.held.id);});
      check(await list.evaluate(e=>e.scrollWidth===e.clientWidth),'short list has no overflow');
      await page.evaluate(async()=>{menus.data.roster=menus.data.roster.slice(0,1);await menus.regroup(regroupFixture.held.id);});
      check(await list.locator('.commander-card').count()===0&&await page.locator('[data-native-id=btn_regroup]').isDisabled(),'empty list disables regroup');
      check(await page.evaluate(async()=>{menus.api.rpc=regroupFixture.rpc;return JSON.stringify(await menus.api.rpc('headquarters',{}))===regroupFixture.account;}),'scroll and preview leave account unchanged');
      await context.close();
    }
    check(report.errors.length===0,'no browser errors');check(report.missing.length===0,'no missing assets');
  }catch(e){report.failure=e.message;throw e;}
  finally{await browser.close();await fs.writeFile(path.join(out,'results.json'),JSON.stringify(report,null,2));}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
