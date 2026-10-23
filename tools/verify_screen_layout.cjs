'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),vm=require('node:vm'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5478',folder=path.resolve(process.argv[3]||'build/screen-layout-20261023/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],layouts:[],screenshots:[],scope:'CSS viewport and touch emulation, no physical Android phone or tablet execution'};
  const check=(pass,label)=>{assert.ok(pass,label);report.checks++;};
  const scope={};vm.runInNewContext(await fs.readFile('rewrite/Ew4.Web/wwwroot/screen-layout.js','utf8'),scope);
  for(const width of [320,336,390,544,600,768,800,1024,1280,1600,2560])for(const height of [248,390,582,768,1024,1600])for(const screen of [80,100,150]){
    const input={width,height,screen,touch:true},a=scope.Ew4ScreenLayout.measure(input),b=scope.Ew4ScreenLayout.measure({...input,insets:{left:32,right:24,top:12,bottom:24}});
    check(a.cornerSize>=(a.profile==='tablet'?48:40)&&a.cornerSize===b.cornerSize,'safe insets preserve button size '+[width,height,screen]);
    check(a.cornerInset===2&&b.cornerInset===2,'only two CSS pixels of extra edge clearance '+[width,height,screen]);
    check(a.width===width&&b.width===width&&a.height===height&&b.height===height,'safe insets never shrink the map viewport '+[width,height,screen]);
    check(b.safeWidth===width-56&&b.safeHeight===height-36&&a.actionWidth===b.actionWidth,'safe content is independent of edge toolbar width '+[width,height,screen]);
    check(a.uiScale*568<=a.width+.01&&a.uiScale*320<=a.height+.01,'menu fits '+[width,height,screen]);
    check(a.compactScale*33>=(a.profile==='tablet'?48:40)-.01,'toolbar keeps the compact size floor '+[width,height,screen]);
  }
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:844,height:390},hasTouch:true});
  let stage='startup';
  page.on('pageerror',e=>report.errors.push({stage,message:e.message,stack:e.stack}));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  const shot=async name=>{await page.screenshot({path:path.join(folder,name+'.png')});report.screenshots.push(name+'.png');};
  const fits=async selector=>page.locator(selector).evaluate(el=>{const r=el.getBoundingClientRect();return r.left>=-.1&&r.top>=-.1&&r.right<=innerWidth+.1&&r.bottom<=innerHeight+.1;});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    await page.evaluate(()=>start('conquest1','fra'));await page.waitForFunction(()=>!busy);
    await page.evaluate(async()=>{const u=state.units.find(u=>u.owner===state.playerFaction&&u.canNavigateMove);await selectCell(u.cell,true,true);});
    for(const [name,width,height] of [['phone-portrait',390,844],['phone-small-portrait',336,700],['phone-landscape',844,390],['device',1215,582],['device-image-content',1823,873],
      ['tablet-compact',600,960],['tablet-portrait',768,1024],['tablet-landscape',1024,768],['tablet-tall',800,1280],['tablet-wide',1280,800],['tablet-hd',2560,1600]]){
      await page.setViewportSize({width,height});
      for(const screen of [80,100,150]){
        stage=name+' '+screen;
        await page.evaluate(screen=>{settings.screen=screen;sizeUi();requestRender();},screen);await page.waitForTimeout(80);
        const layout=await page.evaluate(()=>{
          const ids=['menu-button','end-turn','next-unit','undo-move','actions'],rects=Object.fromEntries(ids.map(id=>{const r=$(id).getBoundingClientRect();return [id,{x:r.x,y:r.y,right:r.right,bottom:r.bottom,width:r.width,height:r.height}];}));
          return {profile:document.documentElement.dataset.screenProfile,rects,canvas:[canvas.clientWidth,canvas.clientHeight],corner:screenLayout.cornerSize,
            actionSizes:[...$('actions').children].map(el=>el.getBoundingClientRect().width),revision:state.revision};
        });
        const floor=layout.profile==='tablet'?48:40;
        for(const [id,r] of Object.entries(layout.rects)){
          check(r.x>=-.1&&r.y>=-.1&&r.right<=width+.1&&r.bottom<=height+.1,name+' '+screen+' '+id+' fits');
          if(id!=='actions')check(r.width>=floor-.1&&Math.abs(r.width-layout.corner)<.1,name+' '+id+' preserves touch size');
        }
        const {actions,'undo-move':undo,'end-turn':end,'next-unit':next}=layout.rects;
        check(Math.abs(width-end.right-2)<.1&&Math.abs(height-end.bottom-2)<.1&&Math.abs(undo.x-2)<.1,name+' buttons sit two pixels from edges');
        check(await page.locator('#end-turn').evaluate(el=>getComputedStyle(el).overflow==='hidden'),name+' directional shadow is cropped');
        check(actions.x>=undo.right+7.9&&actions.right<=end.x-7.9,name+' toolbar clears side buttons');
        check(next.bottom<end.y&&Math.abs(next.right-end.right)<.1,name+' right buttons align and separate');
        check(layout.actionSizes.length>0&&layout.actionSizes.every(s=>s>=floor-.1),name+' action icons remain touch sized');
        check(layout.canvas[0]===width&&layout.canvas[1]===height,name+' map fills the viewport');
        for(const id of ['menu-button','end-turn','next-unit','undo-move'])check(await page.locator('#'+id).evaluate(el=>{const r=el.getBoundingClientRect();return document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest('button')===el;}),name+' '+id+' is reachable');
        await page.locator('#menu-button').tap();check(await fits('.pause-dialog'),name+' pause fits at '+screen);
        await page.locator('#pause-settings').tap();check(await fits('.settings-dialog'),name+' settings fits at '+screen);
        await page.locator('#close-settings').tap();await page.locator('#resume').tap();
        report.layouts.push({name,width,height,screen,...layout});
        if(screen===100)await shot(name);
      }
    }
    await page.setViewportSize({width:390,height:844});
    stage='toolbar swipe';
    await page.evaluate(()=>{
      settings.screen=100;sizeUi();const host=$('actions'),sample=host.firstElementChild;
      for(let i=0;i<12;i++){const el=sample.cloneNode(true);el.dataset.layoutProbe='true';host.append(el);}
    });
    const overflow=await page.locator('#actions').evaluate(el=>{
      const size=el.firstElementChild.getBoundingClientRect().width;el.scrollLeft=el.scrollWidth;
      const last=el.lastElementChild.getBoundingClientRect(),r=el.getBoundingClientRect();
      return {scroll:el.scrollLeft,size,reached:last.right<=r.right+1};
    });
    check(overflow.scroll>0&&overflow.size>=39.9&&overflow.reached,'overflow scrolls without shrinking any action');
    report.overflow=overflow;
    const beforeSwipe=await page.evaluate(()=>{const el=$('actions');el.scrollLeft=0;return {revision:state.revision,view:JSON.stringify(view)};});
    const toolbar=await page.locator('#actions').boundingBox(),cdp=await page.context().newCDPSession(page);
    const swipeY=toolbar.y+toolbar.height/2,swipeStart=toolbar.x+toolbar.width-12,swipeEnd=toolbar.x+12;
    await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:swipeStart,y:swipeY}]});
    for(let step=1;step<=10;step++){
      await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:swipeStart+(swipeEnd-swipeStart)*step/10,y:swipeY}]});
      await page.waitForTimeout(20);
    }
    await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await page.waitForTimeout(150);
    check(await page.evaluate(before=>$('actions').scrollLeft>0&&state.revision===before.revision&&JSON.stringify(view)===before.view,beforeSwipe),'real touch swipe scrolls toolbar without panning map or executing actions');
    await cdp.detach();
    await shot('phone-scroll');await page.evaluate(()=>document.querySelectorAll('[data-layout-probe]').forEach(el=>el.remove()));
    stage='shop';await page.evaluate(()=>menus.shop());await page.waitForTimeout(250);
    check(await fits('#detail-modal>.dialog'),'real original shop fits portrait');
    await page.setViewportSize({width:1280,height:800});await page.waitForTimeout(250);
    check(await fits('#detail-modal>.dialog'),'open original shop adapts on rotation');await shot('tablet-shop');
    await page.evaluate(()=>new Promise(resolve=>{
      const refresh=menus.api.refresh;menus.api.refresh=async(...args)=>{try{return await refresh(...args);}finally{menus.api.refresh=refresh;resolve();}};
      closeDetail();
    }));
    stage='campaign dialogue';await page.evaluate(()=>start('campaign1_01'));await page.waitForFunction(()=>!busy);
    await page.locator('#close-detail').tap();
    for(const [name,width,height] of [['phone-dialogue',390,844],['tablet-dialogue',768,1024],['tablet-dialogue-landscape',1024,768]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(150);
      check(await page.locator('#dialogue').isVisible(),name+' real dialogue is visible');
      check(await fits('#dialogue-ok'),name+' dialogue fits');await shot(name);
    }
    stage='main menu';await page.evaluate(()=>showMain());
    for(const [name,width,height] of [['phone-menu',390,844],['tablet-menu',768,1024],['tablet-menu-landscape',1024,768]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(150);
      check(await fits('#menu-stage'),name+' menu stage fits');
      check(await fits('#main-menu .game-title'),name+' full game title is visible');
      for(const el of await page.locator('#main-menu .main-entry').all())check(await el.evaluate(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+.1&&r.bottom<=innerHeight+.1&&r.height>=43.9;}),name+' commands fit with readable touch size');
      await shot(name);
    }
    stage='safe-area reload';await page.route('**/screen-layout.css?*',async route=>{
      const response=await route.fetch(),css=await response.text();
      await route.fulfill({response,body:css.replaceAll('env(safe-area-inset-left,0px)','32px').replaceAll('env(safe-area-inset-right,0px)','24px')
        .replaceAll('env(safe-area-inset-top,0px)','12px').replaceAll('env(safe-area-inset-bottom,0px)','24px')});
    });
    await page.setViewportSize({width:768,height:1024});await page.reload();await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    await page.evaluate(()=>start('conquest1','fra'));
    const safe=await page.evaluate(()=>{
      const r=$('end-turn').getBoundingClientRect();return {size:r.width,right:innerWidth-r.right,bottom:innerHeight-r.bottom,profile:screenLayout.profile};
    });
    check(safe.size>=47.9&&Math.abs(safe.right-2)<.1&&Math.abs(safe.bottom-2)<.1,'tablet HUD hugs viewport edges even when floating content has safe insets');
    check(await fits('#end-turn')&&await fits('#menu-button'),'tablet safe controls fit');report.safe=safe;await shot('tablet-safe-area');
    check(report.errors.length===0&&report.missing.length===0,'no browser exceptions or missing resources');
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,layouts:report.layouts.length,screenshots:report.screenshots.length,errors:report.errors.length,missing:report.missing.length}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
