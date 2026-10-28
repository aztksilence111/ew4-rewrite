'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,frames:0,errors:[],missing:[],views:[]};
  const check=(ok,label)=>{assert.ok(ok,label);report.checks++;};
  const observe=page=>{
    page.on('pageerror',e=>report.errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  };
  const open=async(page,chapter=0)=>{
    await page.evaluate(chapter=>{mode=1;return menus.openChapter(catalog.chapters[chapter]);},chapter);
    await page.locator('#campaign-battles').evaluate(el=>Promise.all(el.getAnimations({subtree:true}).map(a=>a.finished.catch(()=>{}))));
  };
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720}});observe(page);
    await page.route(/\/content\/ui\/(commanders\d_hd|princesses_hd)\//,async route=>{
      await new Promise(r=>setTimeout(r,120));await route.continue();
    });
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    for(let chapter=0;chapter<6;chapter++){
      await open(page,chapter);
      const result=await page.evaluate(async()=>{
        const failures=[];let frames=0,retained=0;
        const commander=$('campaign-commander'),description=$('campaign-description');
        const sample=()=>{
          frames++;
          const image=commander.querySelector('img'),canvas=description.querySelector('canvas');
          if(menus.battle.commander&&(!image?.complete||!image.naturalWidth))failures.push({id:menus.battle.id,type:'blank portrait'});
          const alpha=canvas?.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
          if(!alpha?.some((value,index)=>index%4===3&&value))failures.push({id:menus.battle.id,type:'blank prose'});
          if(commander.getAnimations({subtree:true}).length||description.getAnimations({subtree:true}).length)failures.push({id:menus.battle.id,type:'animated intro'});
        };
        const rows=[...$('campaign-battles').children];
        for(const row of rows){
          const previous=commander.firstElementChild,id=menus.battle.commander;
          row.click();
          if(id===menus.battle.commander){if(previous!==commander.firstElementChild)failures.push({id:menus.battle.id,type:'replaced unchanged portrait'});retained++;}
          sample();
          await new Promise(resolve=>requestAnimationFrame(()=>{sample();resolve();}));
        }
        for(const row of [rows.at(-1),rows[0],rows[1],rows.at(-1)])row.click();
        sample();
        await new Promise(resolve=>requestAnimationFrame(()=>{sample();resolve();}));
        return {failures,frames,retained,final:menus.battle.id,expected:rows.at(-1).dataset.campaign};
      });
      check(!result.failures.length,'no blank frame, animation or unchanged portrait replacement '+JSON.stringify(result));
      check(result.final===result.expected,'rapid switches keep last selection '+JSON.stringify(result));report.frames+=result.frames;
    }
    for(const viewport of [{width:2376,height:1080},{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(viewport);await open(page);
      const sidebar=page.locator('#campaign-battles'),bounds=await sidebar.boundingBox();
      const x=bounds.x+bounds.width*.6,y=bounds.y+bounds.height*.8,dy=bounds.height*.55;
      const before=await page.evaluate(()=>({id:menus.battle.id,scroll:$('campaign-battles').scrollTop}));
      const overflow=await sidebar.evaluate(el=>el.scrollHeight>el.clientHeight);
      await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x,y-dy,{steps:12});await page.mouse.up();
      const after=await page.evaluate(()=>({id:menus.battle.id,scroll:$('campaign-battles').scrollTop,dragging:$('campaign-battles').classList.contains('dragging')}));
      check(overflow?after.scroll>before.scroll+20:after.scroll===0,'mouse drag scrolls when overflowing '+viewport.width+' '+JSON.stringify(after));
      check(after.id===before.id&&!after.dragging,'drag release does not select '+viewport.width);
      await page.mouse.move(x,bounds.y+bounds.height*.3);await page.mouse.down();
      await page.mouse.move(x,bounds.y+bounds.height*.85,{steps:12});await page.mouse.up();
      check(overflow?await sidebar.evaluate(el=>el.scrollTop)<after.scroll:await sidebar.evaluate(el=>el.scrollTop)===0,'reverse drag scrolls when overflowing '+viewport.width);
      await sidebar.evaluate(el=>{el.scrollTop=0;});
      await page.mouse.move(x,y);await page.mouse.wheel(0,280);await page.waitForTimeout(180);
      check(overflow?await sidebar.evaluate(el=>el.scrollTop)>0:await sidebar.evaluate(el=>el.scrollTop)===0,'wheel still scrolls when overflowing '+viewport.width);
      await sidebar.evaluate(el=>{el.scrollTop=0;});
      await page.mouse.move(x,y);await page.mouse.down();
      await page.mouse.move(bounds.x-30,bounds.y-30,{steps:12});await page.mouse.up();
      check(await sidebar.evaluate(el=>!el.classList.contains('dragging')),'release outside clears drag '+viewport.width);
      await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x,y-dy,{steps:8});
      await sidebar.dispatchEvent('pointercancel',{pointerId:1,pointerType:'mouse'});await page.mouse.up();
      check(await sidebar.evaluate(el=>!el.classList.contains('dragging')&&!el.hasPointerCapture(1)),'cancel clears drag and capture '+viewport.width);
      await sidebar.evaluate(el=>{el.scrollTop=0;});
      await sidebar.locator('button').nth(1).click();
      check(await sidebar.locator('button').nth(1).getAttribute('aria-pressed')==='true','click after drag selects '+viewport.width);
      await page.keyboard.press('Tab');await page.keyboard.press('Enter');
      check(await page.evaluate(()=>menus.battle.id===$('campaign-battles').children[2].dataset.campaign),'keyboard selection after drag '+viewport.width);
      await page.screenshot({path:path.join(out,'campaign-interaction-'+viewport.width+'.png')});
      report.views.push({viewport,before,after});
    }
    const context=await browser.newContext({viewport:{width:844,height:390},hasTouch:true,isMobile:true});
    const touch=await context.newPage();observe(touch);
    await touch.goto(base);await touch.waitForFunction(()=>startupReady,null,{timeout:90000});await open(touch);
    const cdp=await context.newCDPSession(touch),bounds=await touch.locator('#campaign-battles').boundingBox();
    const x=bounds.x+bounds.width*.65,y=bounds.y+bounds.height*.8;
    const before=await touch.evaluate(()=>menus.battle.id);
    await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y}]});
    for(let step=1;step<=12;step++){
      await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x,y:y-bounds.height*.55*step/12}]});
      await touch.waitForTimeout(18);
    }
    await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await touch.waitForTimeout(300);
    check(await touch.locator('#campaign-battles').evaluate(el=>el.scrollTop)>20,'native touch swipe scrolls');
    check(await touch.evaluate(()=>menus.battle.id)===before,'touch swipe does not select');
    await touch.locator('#campaign-battles').evaluate(el=>{el.scrollTop=0;});await touch.waitForTimeout(100);
    await touch.locator('#campaign-battles button').nth(1).tap();
    check(await touch.locator('#campaign-battles button').nth(1).getAttribute('aria-pressed')==='true','touch tap selects');
    await context.close();
    check(!report.errors.length,'no browser errors '+JSON.stringify(report.errors));
    check(!report.missing.length,'no missing assets '+JSON.stringify(report.missing));
    await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify({checks:report.checks,frames:report.frames,views:report.views.length,errors:report.errors.length,missing:report.missing.length}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
