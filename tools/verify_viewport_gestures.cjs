'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5606',folder=path.resolve(process.argv[3]||'build/ui-viewport-20261029/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,errors:[],missing:[],views:[],gestures:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const context=await browser.newContext({viewport:{width:844,height:390},hasTouch:true,isMobile:true,deviceScaleFactor:2});
  const page=await context.newPage(),cdp=await context.newCDPSession(page);
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition,null,{timeout:90000});
  async function pinch(x,y,distance=160){
    const points=d=>[{id:1,x:x-d/2,y},{id:2,x:x+d/2,y}];
    await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:points(distance)});
    for(let i=1;i<=12;i++){
      await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:points(distance+i*12)});
      await page.waitForTimeout(20);
    }
    await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
    await page.waitForTimeout(150);
  }
  async function fixed(label,x=422,y=180){
    const before=await page.evaluate(()=>({width:innerWidth,height:innerHeight,scale:visualViewport.scale}));
    await pinch(x,y);
    const after=await page.evaluate(()=>({width:innerWidth,height:innerHeight,scale:visualViewport.scale,x:visualViewport.offsetLeft,y:visualViewport.offsetTop,scrollX,scrollY}));
    report.gestures.push({label,before,after});
    check(after.scale===1&&before.width===after.width&&before.height===after.height&&!after.x&&!after.y&&!after.scrollX&&!after.scrollY,label+' keeps page fixed');
  }
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await fixed('main menu');
    await page.locator('[data-mode="2"]').click();
    await fixed('conquest selection');
    await page.evaluate(()=>start('conquest1','fra'));await idle();
    if(await page.evaluate(()=>stageIntroOpen)){await page.locator('#detail-confirm').click();await idle();}
    for(let i=0;i<12;i++){
      const pending=await page.evaluate(()=>({story:!!state.story,dialogue:!!state.dialogue}));
      if(!pending.story&&!pending.dialogue)break;
      if(pending.story){await page.waitForFunction(()=>!storyPlayer.preparing&&!storyPlayer.commandPending);await page.locator('#story-player button[aria-label="跳过剧情"]').click();}
      else await page.evaluate(()=>act('dialogue'));
      await idle();
    }
    for(const [width,height] of [[1280,720],[844,390],[667,375],[390,844],[320,568]]){
      await page.setViewportSize({width,height});
      for(const screen of [80,100,150]){
        await page.evaluate(value=>{settings.screen=value;sizeUi();},screen);await page.waitForTimeout(120);
        const geometry=await page.evaluate(()=>{
          const face=diplomacyUi.entryFace.getBoundingClientRect(),undo=$('undo-move').getBoundingClientRect(),hit=diplomacyUi.entry.getBoundingClientRect();
          const crop=getComputedStyle(diplomacyUi.entryFace,'::before'),reference=getComputedStyle($('undo-move'),'::before');
          return {face:face.toJSON(),undo:undo.toJSON(),hit:hit.toJSON(),undoScale:undo.width/$('undo-move').offsetWidth,crop:[crop.left,crop.top,crop.width,crop.height],reference:[reference.left,reference.top,reference.width,reference.height]};
        });
        report.views.push({width,height,screen,...geometry});
        check(Math.abs(geometry.face.width-geometry.undo.width)<.1&&Math.abs(geometry.face.height-geometry.undo.height)<.1,'visible faces equal '+width+'x'+height+' at '+screen+'%');
        check(Math.abs(geometry.face.left-geometry.undo.left)<.1&&geometry.hit.bottom<=geometry.undo.top-4,'aligned and separated '+width+'x'+height+' at '+screen+'%');
        check(geometry.hit.width>=44&&geometry.hit.height>=44,'entry touch target retained');
        check(geometry.crop.every((n,i)=>Math.abs(parseFloat(n)-parseFloat(geometry.reference[i])*geometry.undoScale)<.1),'same sprite crop');
      }
      await page.screenshot({path:path.join(folder,'entry-'+width+'x'+height+'.png')});
    }
    await page.setViewportSize({width:844,height:390});
    await page.evaluate(()=>{settings.screen=100;sizeUi();});
    const before=await page.evaluate(()=>view.zoom);await pinch(422,180);
    check(await page.evaluate(value=>view.zoom>value*1.05,before),'map pinch still zooms game camera');
    check(await page.evaluate(()=>visualViewport.scale===1),'map gesture preserves page scale');
    await page.locator('#open-diplomacy').tap();await page.waitForFunction(()=>diplomacyUi.snapshot);
    await fixed('diplomacy dialog');
    const scroll=await page.locator('.diplomacy-list').boundingBox();
    check(await page.locator('.diplomacy-list').evaluate(el=>el.scrollHeight>el.clientHeight),'real faction list overflows');
    const x=scroll.x+scroll.width*.6,y=scroll.y+scroll.height*.8;
    await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{id:1,x,y}]});
    for(let i=1;i<=10;i++)await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{id:1,x,y:y-i*12}]});
    await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await page.waitForTimeout(200);
    check(await page.locator('.diplomacy-list').evaluate(el=>el.scrollTop>20),'single finger scroll remains usable');
    await page.locator('.diplomacy-dialog .close-icon').tap();
    await page.evaluate(()=>{const original=diplomacyUi.presentation;diplomacyUi.style({...original,entrySize:54});diplomacyUi.fit();window.viewportDefaultPresentation=original;});
    check(await page.evaluate(()=>Math.abs(diplomacyUi.entryFace.getBoundingClientRect().width-2*$('undo-move').getBoundingClientRect().width)<.1),'explicit Mod entry size remains proportional');
    await page.evaluate(()=>{diplomacyUi.style(window.viewportDefaultPresentation);diplomacyUi.fit();});
    await page.screenshot({path:path.join(folder,'final-mobile.png')});
    check(report.errors.length===0,'no browser errors '+JSON.stringify(report.errors));
    check(report.missing.length===0,'no missing resources '+JSON.stringify(report.missing));
  }finally{await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,views:report.views.length,gestures:report.gestures.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
