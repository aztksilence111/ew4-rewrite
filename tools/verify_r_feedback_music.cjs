'use strict';
const fs=require('node:fs/promises'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const url=process.argv[2]||'http://127.0.0.1:5496';
(async()=>{
  const report={checks:0,errors:[]},check=(v,s)=>{assert(v,s);report.checks++;};
  const browser=await chromium.launch({headless:true,args:['--mute-audio','--autoplay-policy=document-user-activation-required'],executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try{
    const context=await browser.newContext({hasTouch:true,isMobile:true,viewport:{width:844,height:390}});
    const page=await context.newPage();page.on('pageerror',e=>report.errors.push(e.message));
    await page.goto(url);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    check(await page.evaluate(()=>settings.music===0&&music.audio.paused),'local preview stays muted');
    // A real tap on a control stopping bubbling must still unlock music.
    await page.evaluate(()=>{
      settings.music=50;
      const button=document.createElement('button');button.id='touch-music-test';button.textContent='Music';
      button.style='position:fixed;inset:20px auto auto 20px;z-index:999999;width:120px;height:60px';
      for(const name of ['pointerdown','pointerup','click'])button.addEventListener(name,e=>e.stopPropagation());
      document.body.append(button);
    });
    await page.locator('#touch-music-test').tap();
    await page.waitForFunction(()=>!music.audio.paused&&music.audio.currentTime>.05,null,{timeout:20000});
    check(await page.evaluate(()=>music.unlocked&&music.audio.src.endsWith('battle1.mp3')),'touch menu plays original MP3');
    await page.evaluate(()=>music.sync({scenario:'music-test'},true));
    await page.waitForFunction(()=>!music.audio.paused&&music.audio.currentTime>.05);
    check(await page.evaluate(()=>/battle[1-4]\.mp3$/.test(music.audio.src)),'asynchronous battle change plays');
    await page.evaluate(()=>music.sync({scenario:'music-test',battleResult:{outcome:'Defeat',id:1}}));
    await page.waitForFunction(()=>!music.audio.paused&&music.audio.currentTime>.05);
    check(await page.evaluate(()=>music.audio.src.endsWith('defeat_music.mp3')),'defeat track plays');
    await page.evaluate(()=>ew4SetAppVisible(false));check(await page.evaluate(()=>music.audio.paused),'background pauses');
    await page.evaluate(()=>ew4SetAppVisible(true));await page.waitForFunction(()=>!music.audio.paused);
    check(await page.evaluate(()=>music.audio.volume===.5),'foreground resumes volume');
    await page.evaluate(()=>{settings.music=0;music.resume();});check(await page.evaluate(()=>music.audio.paused),'volume zero pauses');
    // Deterministic rejection models WebView's early touch gate, independently of
    // desktop Chromium's activation policy. The later gesture must retry.
    check(await page.evaluate(async()=>{
      const player=new Ew4Music.Player({menu:'battle1.mp3',battle:[],defeat:null},()=>50);
      let calls=0;player.file='battle1.mp3';
      player.audio.play=()=>{calls++;return calls===1?Promise.reject(new DOMException('gesture required','NotAllowedError')):Promise.resolve();};
      document.dispatchEvent(new Event('pointerdown'));await new Promise(resolve=>setTimeout(resolve,0));
      document.dispatchEvent(new Event('pointerup'));await new Promise(resolve=>setTimeout(resolve,0));
      const retried=calls===2;player.dispose();document.dispatchEvent(new Event('click'));
      return retried&&calls===2;
    }),'early rejection retries at touch completion and dispose removes listeners');
    check(report.errors.length===0,'no browser script errors');
  } finally {await browser.close();}
  await fs.mkdir('build/r-feedback-20261023',{recursive:true});
  await fs.writeFile('build/r-feedback-20261023/music-checks.json',JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
