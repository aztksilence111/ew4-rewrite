'use strict';
const assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const url=process.argv[2]||'http://127.0.0.1:5587';
(async()=>{
  let checks=0,activePage;const errors=[],check=(value,label)=>{assert(value,label);checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    args:['--mute-audio','--autoplay-policy=document-user-activation-required']});
  try{
    for(const touch of [false,true]){
      const context=await browser.newContext({hasTouch:touch,isMobile:touch,viewport:touch?{width:844,height:390}:{width:1280,height:720}});
      const page=await context.newPage();activePage=page;page.on('pageerror',e=>errors.push(e.message));
      page.on('console',m=>{if(/violates.*Content Security Policy|Refused to load media/i.test(m.text()))errors.push(m.text());});
      await page.goto(url);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      check(await page.evaluate(()=>settings.music===0&&music.audio.paused),'preview starts muted');
      await page.locator('#main-settings')[touch?'tap':'click']();
      await page.locator('#music-volume').fill('50');
      await page.waitForFunction(()=>!music.audio.paused&&music.audio.currentTime>.05,null,{timeout:20000});
      check(await page.evaluate(()=>music.unlocked&&music.audio.volume===.5),'volume enables real audio');
      for(const file of ['battle1.mp3','battle2.mp3','battle3.mp3','battle4.mp3','defeat_music.mp3']){
        await page.evaluate(file=>music.select('test:'+file,file,true),file);
        await page.waitForFunction(()=>!music.audio.paused&&music.audio.currentTime>.05,null,{timeout:20000});
        check(await page.evaluate(()=>music.audio.duration>1&&music.audio.loop),'real track plays '+file);
      }
      let rejected=0,fallbacks=0;
      await page.route('**/music/battle1.mp3',async route=>{
        if(route.request().resourceType()==='media'){rejected++;await route.abort('failed');}
        else{fallbacks++;await route.continue();}
      });
      await page.evaluate(()=>music.select('failure-test','battle1.mp3',true));
      await page.waitForFunction(()=>music.audio.src.startsWith('blob:')&&!music.audio.paused&&music.audio.currentTime>.05,null,{timeout:20000});
      check(rejected>0&&fallbacks===1,'failed media request recovers with a complete fetch');
      check(await page.evaluate(()=>!music.failed&&!music.lastError),'recovered player clears failure');
      await page.locator('#music-volume').fill('0');check(await page.evaluate(()=>music.audio.paused),'mute pauses fallback');
      await page.locator('#music-volume').fill('37');await page.waitForFunction(()=>!music.audio.paused&&music.audio.volume===.37);
      await page.evaluate(()=>ew4SetAppVisible(false));check(await page.evaluate(()=>music.audio.paused),'background pauses fallback');
      await page.evaluate(()=>ew4SetAppVisible(true));await page.waitForFunction(()=>!music.audio.paused);
      check(await page.evaluate(()=>music.audio.volume===.37),'foreground restores fallback');
      await page.unroute('**/music/battle1.mp3');
      await page.evaluate(()=>music.sync({scenario:'exit-test',battleResult:{id:'exit',outcome:'Defeat'}}));
      await page.evaluate(()=>music.menu());await page.waitForFunction(()=>music.audio.src.endsWith('battle1.mp3')&&!music.audio.paused);
      check(await page.evaluate(()=>!music.objectUrl),'scene change releases fallback URL');
      await context.close();
    }
    check(errors.length===0,'no script or media CSP errors: '+errors.join(';'));
    console.log(JSON.stringify({checks,errors}));
  }catch(error){
    console.error(JSON.stringify({checks,errors,state:await activePage?.evaluate(()=>({file:music?.file,src:music?.audio.src,
      error:music?.lastError,failed:music?.failed,paused:music?.audio.paused,ready:music?.audio.readyState,time:music?.audio.currentTime,
      volume:music?.audio.volume,loading:!!music?.loading,pending:!!music?.pending,retries:music?.retries,
      policy:document.querySelector('meta[http-equiv="Content-Security-Policy"]')?.content}))}));throw error;
  }finally{await browser.close();}
})().catch(error=>{console.error(error.message);process.exitCode=1;});
