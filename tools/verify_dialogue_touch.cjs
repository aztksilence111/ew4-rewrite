'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5226';
const folder=path.resolve(__dirname,'../rewrite/reports/dialogue-touch-20261021');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],screenshots:[]};
  const check=(pass,label)=>{assert.ok(pass,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const context=await browser.newContext({viewport:{width:1280,height:720},hasTouch:true});
  const page=await context.newPage();
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  const reset=async()=>{
    await page.evaluate(async()=>{await start('campaign1_01');if(stageIntroOpen)closeDetail();if(dialoguePresentationTask)await dialoguePresentationTask;window.dialogueCalls=0;});
    check(await page.locator('#dialogue').isVisible(),'real campaign dialogue visible');
  };
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady);
    await page.evaluate(()=>{
      const original=rpc;
      rpc=async function(route,args){if(route==='action'&&args.op==='dialogue')window.dialogueCalls++;return original(route,args);};
    });
    for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844]]){
      await page.setViewportSize({width,height});
      for(const [x,y] of [[1,1],[width/2,height/2],[1,height-1],[width-1,height-1],[width-2,90]]){
        await reset();
        if(name==='desktop')await page.mouse.click(x,y);else await page.touchscreen.tap(x,y);
        await page.waitForFunction(()=>!busy&&window.dialogueCalls===1);
        check(await page.evaluate(()=>state.revision===1&&!selected),'one full-screen tap advances once without selecting map');
      }
      await reset();
      if(name==='desktop')await page.locator('#dialogue-ok').click();else await page.locator('#dialogue-text').tap();
      await page.waitForFunction(()=>!busy&&window.dialogueCalls===1);
      check(await page.evaluate(()=>state.revision===1),'dialogue child tap bubbles exactly once');
      await reset();
      await page.locator('#map').evaluate(e=>e.style.visibility='hidden');
      const display=await page.locator('#dialogue-ok').screenshot();
      await page.evaluate(()=>{
        const sheet=[...document.styleSheets].find(s=>s.href?.includes('original-ui.css'));
        window.dialogueRules=[];
        for(let i=sheet.cssRules.length-1;i>=0;i--)if(sheet.cssRules[i].selectorText?.includes(':has(')&&sheet.cssRules[i].selectorText.includes('#dialogue')){
          dialogueRules.push([i,sheet.cssRules[i].cssText]);sheet.deleteRule(i);
        }
      });
      const originalDisplay=await page.locator('#dialogue-ok').screenshot();
      if(!display.equals(originalDisplay)){
        await fs.writeFile(path.join(folder,name+'-changed.png'),display);
        await fs.writeFile(path.join(folder,name+'-original.png'),originalDisplay);
      }
      check(display.equals(originalDisplay),'dialogue appearance matches original CSS pixel for pixel');
      await page.evaluate(()=>{
        const sheet=[...document.styleSheets].find(s=>s.href?.includes('original-ui.css'));
        for(const [i,rule] of dialogueRules.reverse())sheet.insertRule(rule,i);
        $('map').style.removeProperty('visibility');
      });
      await page.screenshot({path:path.join(folder,name+'.png')});report.screenshots.push(name+'.png');
      await page.locator('#menu-button').tap();
      check(await page.locator('#pause').isVisible(),'pause button remains reachable');
      await page.touchscreen.tap(width/2,2);
      check(await page.evaluate(()=>state.revision===0&&dialogueCalls===0),'pause backdrop never advances dialogue');
      await page.locator('#pause-settings').tap();
      check(await page.locator('#settings').isVisible(),'settings remains reachable over dialogue and pause');
      await page.locator('#close-settings').tap();await page.locator('#resume').tap();
      check(await page.evaluate(()=>state.revision===0&&dialogueCalls===0),'pause and settings controls preserve dialogue');
      await page.evaluate(()=>busy=true);
      await page.touchscreen.tap(width/2,height/2);
      check(await page.evaluate(()=>dialogueCalls===0),'busy guard blocks repeated advance');
      await page.evaluate(()=>busy=false);
    }
    await reset();await page.locator('#dialogue-ok').focus();await page.keyboard.press('Enter');
    await page.waitForFunction(()=>!busy&&dialogueCalls===1);
    check(await page.evaluate(()=>state.revision===1),'keyboard button activation still advances exactly once');
    check(report.errors.length===0,'no browser exceptions');check(report.missing.length===0,'no missing assets');
  }finally{await fs.writeFile(path.join(folder,'web.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
