'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const {PNG}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('pngjs');
const base=process.argv[2],output=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const page=await browser.newPage({viewport:{width:1280,height:720}}),report={checks:0,errors:[],missing:[],views:[]};
  page.setDefaultTimeout(30000);
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await page.evaluate(()=>load(2,1));
    await page.waitForSelector('#dialogue:not([hidden])');
    check(await page.evaluate(()=>state.phase==='Finished'&&state.outcome==='Victory'&&!state.battleResult),'actual exported save waits on terminal dialogue');
    const medals=await page.evaluate(()=>state.medals);
    await page.locator('#dialogue-ok').click();
    await page.waitForSelector('[data-layout="form_victory"]:visible');
    const receipt=await page.evaluate(()=>({id:state.battleResult.id,medals:state.medals}));
    check(await page.evaluate(()=>state.round===8&&state.battleResult.stars===5&&state.battleResult.scenario==='campaign1_06'),'actual Marengo save reaches five-star round-eight victory');
    check(receipt.medals===medals+150||receipt.medals===medals,'victory paid at most once');
    for(const [name,width,height] of [['desktop',1280,720],['phone',844,390],['portrait',390,844]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(250);
      const view=await page.evaluate(()=>{
        const layout=document.querySelector('[data-layout="form_victory"]'),r=layout.getBoundingClientRect();
        return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,broken:[...layout.querySelectorAll('img')].filter(i=>!i.complete||!i.naturalWidth).length};
      });
      const pixels=PNG.sync.read(await page.locator('#map').screenshot()).data,colors=new Set();
      for(let i=0;i<pixels.length;i+=400)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);view.colors=colors.size;
      check(view.left>=0&&view.right<=width&&view.top>=0&&view.bottom<=height,name+' result is inside viewport');
      await page.screenshot({path:path.join(output,name+'.png')});report.views.push({name,...view});
      check(view.colors>20&&view.broken===0,name+' battlefield and result artwork render '+JSON.stringify(view));
    }
    await page.evaluate(()=>{closeDetail();refreshHud();});
    check(await page.locator('#detail-modal').isHidden(),'dismissed result stays closed');
    await page.evaluate(async()=>{await showMain();await load(0,1);});await page.waitForSelector('[data-layout="form_victory"]:visible');
    check(await page.evaluate(r=>!state.dialogue&&state.medals===r.medals&&state.battleResult.id===r.id,receipt),'autosaved result reloads without dialogue or duplicate payout');
    await page.evaluate(async()=>{await showMain();await load(1,1);});await page.waitForSelector('#dialogue:not([hidden])');
    await page.locator('#dialogue-ok').click();
    await page.waitForFunction(()=>state.localBattleDialogue===true);
    await page.locator('#dialogue-ok').click();
    await page.waitForSelector('[data-native-id="btn_restart"]:visible');
    check(await page.evaluate(()=>state.battleResult.outcome==='Defeat'&&state.battleResult.awardMedals===0),'defeat dialogue also reaches settlement');
    await page.screenshot({path:path.join(output,'defeat.png')});
    await page.locator('[data-native-id="btn_exit"]').click();
    await page.waitForSelector('#main-menu:visible');
    check(await page.locator('#main-menu').isVisible(),'settlement exit returns to main menu');
    check(report.errors.length===0,'no browser exceptions');check(report.missing.length===0,'no missing resources');
    await fs.writeFile(path.join(output,'checks.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
