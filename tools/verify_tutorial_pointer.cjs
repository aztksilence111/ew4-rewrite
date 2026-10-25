'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const dependencies=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=dependencies('playwright'),{PNG}=dependencies('pngjs');
const base=process.argv[2]||'http://127.0.0.1:5551',folder=path.resolve(process.argv[3]||'build/tutorial-pointer-fix-20261025');
const report={checks:0,errors:[],missing:[],samples:[],screenshots:[]};
const check=(value,label)=>{assert.ok(value,label);report.checks++;};
async function snapshot(page,label){
  const result=await page.evaluate(()=>{
    positionTutorial();const focus=$('tutorial-focus');
    for(const animation of focus.getAnimations({subtree:true})){animation.pause();animation.currentTime=0;}
    const rect=focus.getBoundingClientRect(),style=getComputedStyle(focus,'::after');
    const hand={left:rect.left+parseFloat(style.left),top:rect.top+parseFloat(style.top),width:parseFloat(style.width),height:parseFloat(style.height)};
    return {hidden:focus.hidden,command:tutorialRect,wait:tutorial.wait,frame:{left:rect.left,top:rect.top,width:rect.width,height:rect.height},
      hand,flip:Number(focus.style.getPropertyValue('--tutorial-hand-flip')),travel:parseFloat(focus.style.getPropertyValue('--tutorial-hand-travel')),
      opacity:getComputedStyle(focus).opacity,frameImage:getComputedStyle(focus,'::before').borderImageSource};
  });
  const vp=page.viewportSize(),h=result.hand;
  check(!result.hidden,label+' visible');
  check(h.left>=0&&h.top>=0&&h.left+h.width<=vp.width+.1&&h.top+h.height<=vp.height+.1,label+' hand inside viewport');
  check(h.top+result.travel>=0&&h.top+h.height+result.travel<=vp.height+.1,label+' animation stays inside viewport');
  check(Math.abs(h.width/h.height-35/47)<.001,label+' hand aspect ratio');
  check(result.opacity==='1'&&result.frameImage.includes('framebox_bold.png'),label+' original frame and independent hand opacity');
  report.samples.push({label,...result});
  return result;
}
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const native=JSON.parse(await fs.readFile(path.join(folder,'native.json'),'utf8'));
  const {frame,pointer}=require('../rewrite/Ew4.Web/wwwroot/tutorial.js');
  for(const v of native.vectors){
    const [left,top,width,height]=v.rect;
    assert.deepEqual(Object.values(frame({left,top,width,height},{w:v.correction[0],h:v.correction[1]},v.scale)),v.expected.map((n,i)=>i<2?n:Math.max(1,n)));report.checks++;
  }
  for(const scale of [.5,1,1.5,2,3])for(const [width,height] of [[1280,720],[844,390],[390,844]])for(const [left,top] of [[0,0],[width-40,height-40],[width/2,height/2]]){
    const p=pointer({left,top,width:40,height:40},scale,width,height);
    check(p.left>=0&&p.left+p.width<=width&&p.top+Math.min(0,p.travel)>=0&&p.top+p.height+Math.max(0,p.travel)<=height,'pointer boundary matrix');
  }
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720},bypassCSP:true});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.evaluate(()=>start('tutorials1',null));
      await page.evaluate(()=>tutorial.notify({type:'touch'}));await page.waitForTimeout(120);
      const resources=await snapshot(page,'resources-'+width);check(resources.flip===-1,'top HUD flips pointer '+width);
      const file='resources-'+width+'.png';await page.screenshot({path:path.join(folder,file)});report.screenshots.push(file);
      // Isolate the DOM overlay so pixel differences cannot come from map animation.
      await page.addStyleTag({content:'canvas{visibility:hidden!important}'});
      const before=PNG.sync.read(await page.screenshot());
      const hidden=await page.addStyleTag({content:'#tutorial-focus::after{visibility:hidden!important}'});
      const after=PNG.sync.read(await page.screenshot());await hidden.evaluate(e=>e.remove());
      let pixels=0;const h=resources.hand;
      for(let y=Math.ceil(h.top);y<Math.floor(h.top+h.height);y++)for(let x=Math.ceil(h.left);x<Math.floor(h.left+h.width);x++){
        const i=(y*before.width+x)*4;if(before.data[i]!==after.data[i]||before.data[i+1]!==after.data[i+1]||before.data[i+2]!==after.data[i+2])pixels++;
      }
      check(pixels>100,'rendered hand pixels '+width);report.samples.at(-1).pixels=pixels;
      await page.locator('head>style').evaluateAll(elements=>elements.forEach(e=>e.remove()));
      await page.evaluate(async()=>{await tutorial.notify({type:'touch'});await selectCell(2783);});
      await snapshot(page,'terrain-'+width);
      await page.evaluate(()=>tutorial.notify({type:'touch'}));await snapshot(page,'map-'+width);
      await page.evaluate(()=>start('tutorials2',null));
      await page.evaluate(async()=>{
        await tutorial.notify({type:'touch'});await selectCell(2633);await tutorial.notify({type:'touch'});
        await selectCell(2713);await tutorial.notify({type:'touch'});await selectCell(2713);
      });
      await snapshot(page,'item-button-'+width);
      await page.locator('[data-tutorial-id="btn_item"]').click();
      await page.waitForFunction(()=>tutorialRect?.string==='lbox_item');await page.waitForTimeout(250);
      const item=await snapshot(page,'item-row2-'+width);
      const row=await page.locator('[data-tutorial-id="lbox_item"][data-tutorial-row="2"]').boundingBox();
      check(item.frame.left<=row.x&&item.frame.left+item.frame.width>=row.x+row.width,'correct item row '+width);
      const file2='items-'+width+'.png';await page.screenshot({path:path.join(folder,file2)});report.screenshots.push(file2);
      await page.evaluate(async()=>{await tutorial.notify({type:'touch'});await tutorial.notify({type:'touch'});});
      await snapshot(page,'item-row3-'+width);
      await page.evaluate(async()=>{await tutorial.notify({type:'touch'});await tutorial.notify({type:'touch'});});
      await snapshot(page,'close-button-'+width);
      await page.locator('#close-detail').click();
      check(await page.evaluate(()=>tutorial.wait?.name==='wait area'),'close control advances original tutorial '+width);
      // Project the source roster hint onto a real deployment page without assigning a commander.
      await page.evaluate(async()=>{await menus.deployGeneral(selected.id,state.revision);tutorialRect={name:'draw ui rect',string:'grid_general',row:'0'};});
      await snapshot(page,'deployment-row0-'+width);
      check(await page.evaluate(()=>$('play').hidden&&$('tutorial-focus').parentElement===document.body),'deployment hint survives hidden battlefield '+width);
      await page.locator('#hq-back').click();
    }
    // Exercise the shared native-window callback used by tutorial trading buttons.
    report.nativeAction=await page.evaluate(async()=>{
      const original=tutorial;let actions=0;
      const source={sha256:'native-action-check',commands:[{name:'wait ui',string:'btn_buy_1'},{name:'wait ui',string:'btn_buy_3'}]};
      try{
        tutorial=new Ew4Tutorial.Runner(source,()=>{});await tutorial.begin();
        const layout=menus.native('form_exchange');
        layout.action('btn_buy_1','test',async()=>{actions++;});
        await layout.get('btn_buy_1').onclick();const first=tutorial.wait.string;
        layout.action('btn_buy_3','test',async()=>{actions++;});
        await layout.get('btn_buy_3').onclick();
        return {actions,first,finished:tutorial.finished};
      }finally{tutorial=original;closeDetail();}
    });
    check(report.nativeAction.actions===2&&report.nativeAction.first==='btn_buy_3'&&report.nativeAction.finished,'native UI callbacks advance tutorial after successful action');
    const touch=await browser.newPage({viewport:{width:844,height:390},hasTouch:true,isMobile:true});
    touch.on('pageerror',e=>report.errors.push(e.message));touch.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
    await touch.goto(base);await touch.waitForFunction(()=>startupReady,null,{timeout:60000});
    await touch.evaluate(()=>start('tutorials1',null));await touch.touchscreen.tap(420,170);
    await touch.waitForFunction(()=>tutorialRect?.string==='group_res');
    await snapshot(touch,'touch-resources');await touch.close();
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing assets');
  }finally{
    await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();
  }
  console.log(JSON.stringify({checks:report.checks,errors:report.errors,missing:report.missing,samples:report.samples.length,screenshots:report.screenshots}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
