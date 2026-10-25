'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5555',folder=path.resolve(process.argv[3]||'build/hq-objectives-20261025/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],screenshots:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const browser=await chromium.launch({headless:true,ignoreDefaultArgs:['--hide-scrollbars'],executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720},hasTouch:true});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  const shot=async name=>{
    await page.evaluate(async()=>{await Promise.all([...document.querySelectorAll('.popup-opening')].flatMap(e=>e.getAnimations()).map(a=>a.finished.catch(()=>{})));});
    await page.waitForFunction(()=>[...document.querySelectorAll('#hq-screen img,#detail-modal:not([hidden]) img')].every(i=>i.complete&&i.naturalWidth));
    await page.screenshot({path:path.join(folder,name+'.png')});report.screenshots.push(name);
  };
  const settled=()=>page.waitForFunction(()=>document.getElementById('hq-filter-shield').hidden);
  const close=async()=>{await page.locator('#close-detail').click();await page.waitForFunction(()=>document.getElementById('detail-modal').hidden);};
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady);await page.locator('#headquarters').click();await page.waitForSelector('#hq-filter-main');
    check((await page.evaluate(()=>rpc('objectives'))).entries.length===0,'R version has no task or achievement definitions');
    for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844],['short-landscape',667,375]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(90);
      check(await page.locator('#hq-filter-main').getAttribute('aria-label')==='筛选',name+' fixed filter label');
      check(await page.locator('#hq-filter-options').isHidden(),name+' starts collapsed');
      check(await page.locator('#hq-filters').evaluate(host=>{
        const buttons=[...host.querySelectorAll(':scope>button')].map(b=>b.getBoundingClientRect()),academy=document.getElementById('hq-recruit').getBoundingClientRect();
        return buttons.length===3&&buttons.every((r,i)=>r.left>=0&&r.bottom<=innerHeight&&r.right<academy.left&&(!i||r.left>=buttons[i-1].right));
      }),name+' filter task achievement order fits before academy');
      await shot(name+'-collapsed');
      await page.locator('#hq-filter-main').click();await page.waitForTimeout(330);
      check(await page.locator('#hq-filter-options').evaluate(e=>{
        const main=document.getElementById('hq-filter-main').getBoundingClientRect(),academy=document.getElementById('hq-recruit').getBoundingClientRect();
        const boxes=[...e.children].map(b=>b.getBoundingClientRect());
        return boxes.every((b,i)=>b.left>=main.right&&b.right<=academy.left-1&&b.top>=main.top-1&&b.bottom<=main.bottom+1&&(!i||b.left>=boxes[i-1].right));
      }),name+' all four expanded options stay in row before academy');
      check(await page.locator('#hq-filter-options button').count()===4,name+' four stable options');
      check(await page.locator('#hq-tasks').isDisabled()&&await page.locator('#hq-achievements').isDisabled(),name+' covered commands also disable keyboard activation');
      check(await page.locator('#hq-filter-options').evaluate(e=>{
        const r=e.firstElementChild.getBoundingClientRect(),hit=document.elementFromPoint((r.left+r.right)/2,(r.top+r.bottom)/2);return e.contains(hit);
      }),name+' options intercept lower task button');
      await shot(name+'-expanded');
      for(const label of ['一星区','二星区','三星区','全选']){
        if(await page.locator('#hq-filter-options').isHidden())await page.locator('#hq-filter-main').click();
        await page.getByRole('menuitemradio',{name:label,exact:true}).click();await settled();
        check(await page.locator('#hq-filter-main').getAttribute('aria-label')==='筛选',name+' '+label+' keeps main unchanged');
        check(await page.getByRole('menuitemradio',{name:label,exact:true,includeHidden:true}).getAttribute('aria-checked')==='true',name+' '+label+' remembers selection');
        check(await page.locator('#hq-roster .commander-card').count()===(label==='全选'?60:20),name+' '+label+' roster slots');
      }
      await page.locator('#hq-filter-main').click();await page.waitForTimeout(300);
      const academy=await page.locator('#hq-recruit').boundingBox();
      await page.mouse.move(academy.x+academy.width/2,academy.y+academy.height/2);await page.mouse.down();await page.waitForTimeout(400);
      check(await page.evaluate(()=>!menus.recruiting&&!document.getElementById('hq-filter-shield').hidden),name+' long outside press retains click shield');
      await page.mouse.up();await settled();
      check(await page.evaluate(()=>!menus.recruiting),name+' outside click only closes');
      await page.locator('#hq-recruit').click();check(await page.evaluate(()=>menus.recruiting),name+' following click enters academy');
      await page.locator('#hq-back').click();await page.waitForSelector('#hq-filter-main');
      for(const [id,kind,label] of [['hq-tasks','task','暂无任务'],['hq-achievements','achievement','暂无成就']]){
        await page.locator('#'+id).click();await page.waitForFunction(k=>document.querySelector('.hq-objectives')?.dataset.kind===k&&document.querySelector('.hq-objective-empty .original-text')?.getAttribute('aria-label')!== '加载中',kind);
        check(await page.locator('.hq-objective-empty .original-text').getAttribute('aria-label')===label,name+' '+kind+' empty panel');
        check(await page.locator('.hq-objective-list').evaluate(e=>getComputedStyle(e).overflowY==='scroll'&&e.offsetWidth>e.clientWidth),name+' '+kind+' reserves visible vertical scrollbar');
        check(await page.locator('#detail-modal>.dialog').evaluate(e=>{const r=e.getBoundingClientRect();return r.left>=-1&&r.right<=innerWidth+1&&r.top>=-1&&r.bottom<=innerHeight+1;}),name+' '+kind+' dialog fits viewport');
        await shot(name+'-'+kind);await close();
      }
    }
    await page.setViewportSize({width:1280,height:720});
    await page.locator('#hq-filter-main').focus();await page.keyboard.press('ArrowRight');
    check(await page.getByRole('menuitemradio',{name:'全选',exact:true}).evaluate(e=>e===document.activeElement),'right arrow opens and focuses first choice');
    await page.keyboard.press('End');check(await page.getByRole('menuitemradio',{name:'三星区',exact:true}).evaluate(e=>e===document.activeElement),'End focuses last choice');
    await page.keyboard.press('Escape');await settled();check(await page.locator('#hq-filter-main').evaluate(e=>e===document.activeElement),'Escape restores main focus');
    await page.evaluate(()=>{const b=document.getElementById('hq-filter-main');for(let i=0;i<15;i++)b.click();});
    check(await page.locator('#hq-filter-main').getAttribute('aria-expanded')==='true','rapid toggles finish open');
    await page.locator('#hq-filter-main').click();await settled();check(await page.locator('#hq-filter-options').isHidden(),'rapid close removes options from hit testing');
    await page.emulateMedia({reducedMotion:'reduce'});await page.locator('#hq-filter-main').click();
    check(await page.getByRole('menuitemradio',{name:'三星区',exact:true}).evaluate(e=>getComputedStyle(e).transitionDuration==='0s'),'reduced motion');
    await page.locator('#hq-filter-main').click();await settled();
    await page.emulateMedia({reducedMotion:'no-preference'});
    const rewards=await page.evaluate(()=>({items:{[menus.data.items[0].id]:2},commanders:[menus.data.definitions.find(d=>!d.recruitable).id]}));
    let claimed=false,claimRequests=0,longList=false;
    await page.route('**/api/objectives?*',async route=>{
      const payload=route.request().postDataJSON()||JSON.parse(new URL(route.request().url()).searchParams.get('command')||new URL(route.request().url()).searchParams.get('q')||'{}');if(payload.op==='claim'){claimRequests++;await new Promise(resolve=>setTimeout(resolve,100));claimed=true;}
      const entries=[{id:'fixture',kind:'achievement',name:'测试将领与装备及征服占城组合成就',description:'将领、装备和隐藏关卡条件组合；征服占领指定地点、城市数与消灭部队数。',reward:{medals:25,badges:3,...rewards},completed:true,claimed,conditions:[]}];
      if(longList)for(const kind of ['task','achievement'])for(let i=0;i<18;i++)entries.push({id:kind+i,kind,name:(kind==='task'?'测试任务':'测试成就')+(i+1),description:'占领城市与消灭部队数的条件示例',reward:{medals:10,badges:1},completed:false,claimed:false,conditions:[{current:i,required:20}]});
      await route.fulfill({json:{ok:true,data:{revision:claimed?2:1,medals:25,badges:3,entries}}});
    });
    for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844]]){
      await page.setViewportSize({width,height});await page.locator('#hq-achievements').click();await page.waitForSelector('[data-objective=fixture]');
      check(await page.locator('.hq-objective-resource[data-item]').count()===1&&await page.locator('.hq-objective-resource[data-commander]').count()===1,name+' item and nonrecruitable commander reward visible');
      check(await page.locator('.hq-objective-row').evaluate(e=>{
        const row=e.getBoundingClientRect(),button=e.querySelector('button').getBoundingClientRect(),desc=e.querySelector('.hq-objective-description').getBoundingClientRect();
        return desc.right<button.left&&button.right<=row.right+1&&button.bottom<=row.bottom+1&&e.scrollWidth<=e.clientWidth;
      }),name+' configured entry text rewards and button fit');await shot(name+'-configured');await close();
    }
    await page.locator('#hq-achievements').click();await page.waitForSelector('[data-objective=fixture]');await page.getByRole('button',{name:'领取 测试将领与装备及征服占城组合成就',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('.hq-objective-row button')?.getAttribute('aria-label')?.startsWith('已领取'));
    check(claimRequests===1&&await page.locator('.hq-objective-row button').isDisabled(),'claim renders once and disables claimed command');
    await close();longList=true;
    for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844]])for(const [id,kind] of [['hq-tasks','task'],['hq-achievements','achievement']]){
      await page.setViewportSize({width,height});await page.locator('#'+id).click();await page.waitForSelector('[data-objective='+kind+'17]');
      await page.evaluate(async()=>{await Promise.all([...document.querySelectorAll('.popup-opening')].flatMap(e=>e.getAnimations()).map(a=>a.finished.catch(()=>{})));});
      const list=page.locator('.hq-objective-list');
      check(await list.evaluate(e=>e.scrollHeight>e.clientHeight&&e.offsetWidth>e.clientWidth&&getComputedStyle(e).overflowY==='scroll'),name+' '+kind+' long list has visible scrollbar');
      const thumb=await list.evaluate(e=>{const r=e.getBoundingClientRect(),scale=r.height/e.offsetHeight;return {x:r.right-(e.offsetWidth-e.clientWidth)*scale/2,y:r.top+Math.max(18,e.clientHeight*e.clientHeight/e.scrollHeight)*scale/2,end:r.top+r.height*.7};});
      await page.mouse.move(thumb.x,thumb.y);await page.mouse.down();await page.mouse.move(thumb.x,thumb.end,{steps:8});await page.mouse.up();
      check(await list.evaluate(e=>e.scrollTop>0),name+' '+kind+' scrollbar thumb drag scrolls content');
      await list.evaluate(e=>{e.scrollTop=0;});
      await list.hover();await page.mouse.wheel(0,480);await page.waitForTimeout(120);
      check(await list.evaluate(e=>e.scrollTop>0),name+' '+kind+' wheel scrolls vertically');
      await list.focus();await page.keyboard.press('End');await page.waitForTimeout(180);
      check(await list.evaluate(e=>e.scrollTop+e.clientHeight>=e.scrollHeight-1),name+' '+kind+' End reaches final entry');
      check(await page.locator('[data-objective='+kind+'17]').evaluate(e=>{const r=e.getBoundingClientRect(),p=e.parentElement.getBoundingClientRect();return r.bottom<=p.bottom+1&&r.top>=p.top-1;}),name+' '+kind+' final entry fits inside scroll area');
      await shot(name+'-'+kind+'-scroll-bottom');
      await page.keyboard.press('Home');await page.waitForTimeout(180);check(await list.evaluate(e=>e.scrollTop<1),name+' '+kind+' Home returns to top');
      if(name!=='desktop'&&kind==='achievement'){
        const r=await list.boundingBox(),cdp=await page.context().newCDPSession(page),x=r.x+r.width*.5,start=r.y+r.height*.8,end=r.y+r.height*.2;
        await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y:start}]});
        for(let step=1;step<=10;step++)await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x,y:start+(end-start)*step/10}]});
        await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await page.waitForTimeout(120);await cdp.detach();
        check(await list.evaluate(e=>e.scrollTop>0),name+' achievement touch scrolls vertically');
      }
      await close();
    }
    await page.unroute('**/api/objectives?*');
    check(report.errors.length===0,'no browser errors');check(report.missing.length===0,'no missing resources');
  }catch(error){report.failure=error.message;process.exitCode=1;await page.screenshot({path:path.join(folder,'failure.png')});}
  finally{await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
