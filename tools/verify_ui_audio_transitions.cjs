'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2]||'http://127.0.0.1:56142',out=path.resolve(process.argv[3]||'build/ui-audio-check-20261029/browser'),root=path.resolve(__dirname,'..');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],views:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio','--autoplay-policy=document-user-activation-required']});
  try{
    for(const [width,height,touch] of [[1280,720,false],[844,390,true],[390,844,true]]){
      const context=await browser.newContext({viewport:{width,height},hasTouch:touch,isMobile:touch}),page=await context.newPage();
      if(process.argv.includes('--source'))await page.route(/\/(?:compat\/)?(?:app|menus|native-layout|ui-audio)\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',path:path.join(root,'rewrite/Ew4.Web/wwwroot',new URL(route.request().url()).pathname.split('/').pop())}));
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      await page.goto(base);await page.waitForFunction(()=>startupReady&&!sessionTransition,null,{timeout:90000});
      await page.evaluate(()=>{
        window.uiSounds=[];const sound=effects.sound.bind(effects);effects.sound=file=>{uiSounds.push(file);sound(file);};
        window.uiPlays=[];const play=HTMLMediaElement.prototype.play;HTMLMediaElement.prototype.play=function(){if(this.src.includes('/sounds/'))uiPlays.push(this);return play.call(this);};
        settings.music=40;settings.sound=60;
      });
      const click=async selector=>page.locator(selector)[touch?'tap':'click']();
      await click('#main-settings');
      check(await page.evaluate(()=>uiSounds.join(',')==='sfx_click.wav,sfx_pop.wav'),'one click and popup on settings '+width);
      await page.waitForFunction(()=>music.file==='battle1.mp3'&&!music.audio.paused&&music.audio.currentTime>.05,null,{timeout:20000});
      check(await page.evaluate(()=>music.audio.loop&&music.audio.volume===.4),'original menu music playing '+width);
      await page.waitForFunction(()=>uiPlays.length===2&&uiPlays.every(a=>a.readyState>=2&&a.duration>0),null,{timeout:15000});
      check(await page.evaluate(()=>uiPlays.every(a=>!a.error)),'real WAV elements decoded '+width);
      await click('#confirm-settings');
      await page.evaluate(()=>{uiSounds.length=0;uiPlays.length=0;});
      await page.locator('[data-mode="1"]').focus();await page.keyboard.press('Enter');
      await page.waitForFunction(()=>menus.pageTransition);
      const samples=await page.evaluate(async()=>{
        const frames=[];while(menus.pageTransition){const el=document.querySelector('.menu-transition-curtain');if(el){const r=el.getBoundingClientRect();frames.push({opacity:Number(getComputedStyle(el).opacity),main:!$('main-menu').hidden,x:r.x,y:r.y,w:r.width,h:r.height});}await new Promise(requestAnimationFrame);}return frames;
      });
      check(samples.some(s=>s.main&&s.opacity>0&&s.opacity<.95)&&samples.some(s=>!s.main&&s.opacity>0&&s.opacity<.95),'fade out then fade in '+width);
      check(samples.every(s=>s.x===0&&s.y===0&&s.w===width&&s.h===height),'curtain covers viewport '+width);
      check(await page.evaluate(()=>uiSounds.join(',')==='sfx_click.wav'),'keyboard click once '+width);
      await page.evaluate(()=>{$('menu-back').disabled=true;uiSounds.length=0;$('menu-back').click();$('menu-back').disabled=false;});
      check(await page.evaluate(()=>!uiSounds.length),'disabled button silent '+width);
      await click('#menu-back');await page.waitForFunction(()=>!menus.pageTransition&&!$('main-menu').hidden);
      check(await page.evaluate(()=>uiSounds.join(',')==='sfx_cancel.wav'),'return sound once '+width);
      await click('[data-mode="2"]');await page.waitForFunction(()=>!menus.pageTransition&&!$('scenario-menu').hidden);
      await page.evaluate(()=>chooseScenario(catalog.scenarios.find(s=>s.id==='conquest1')));
      await page.waitForFunction(()=>!menus.pageTransition&&!$('countries').hidden);
      const country=page.locator('#country-list button').nth(1);
      await page.evaluate(()=>uiSounds.length=0);await country[touch?'tap':'click']();
      check(await page.evaluate(()=>uiSounds.join(',')==='sfx_select.wav'),'country selection once '+width);
      await page.evaluate(()=>{uiSounds.length=0;$('country-list').firstElementChild.click();});
      check(await page.evaluate(()=>!uiSounds.length),'automatic selection silent '+width);
      // Check exported native attributes, including an explicit empty sound.
      await page.evaluate(()=>{
        const b=document.createElement('button');b.id='audio-test-native';b.textContent='test';b.style.cssText='position:fixed;left:0;top:0;z-index:20000;width:90px;height:44px';
        b.nativeAttributes={sound:''};document.body.append(b);
      });
      await click('#audio-test-native');check(await page.evaluate(()=>!uiSounds.length),'explicit native silence '+width);
      await page.evaluate(()=>{$('audio-test-native').nativeAttributes.sound='sfx_select.wav';});
      await click('#audio-test-native');check(await page.evaluate(()=>uiSounds.join(',')==='sfx_select.wav'),'native sound override '+width);
      await page.evaluate(()=>{uiSounds.length=0;});
      const r=await page.locator('#audio-test-native').boundingBox();
      await page.mouse.move(r.x+5,r.y+10);await page.mouse.down();await page.mouse.move(r.x+40,r.y+10);await page.mouse.up();
      check(await page.evaluate(()=>!uiSounds.length),'drag is silent '+width);
      await page.evaluate(()=>{new Ew4NativeUI.Layout(menus.api,'form_getgeneraltips');});
      check(await page.evaluate(()=>uiSounds.join(',')==='sfx_lvup2.wav'),'native popup uses original sound '+width);
      await page.evaluate(()=>{closeDetail();uiSounds.length=0;new Ew4NativeUI.Layout(menus.api,'silent-test',null,{definition:{attributes:{w:'200',h:'100',sound:''},children:[]}});});
      check(await page.evaluate(()=>!uiSounds.length),'explicit silent popup '+width);
      await page.evaluate(()=>closeDetail());
      await page.evaluate(()=>{$('audio-test-native').remove();settings.sound=0;uiPlays.length=0;});
      await click('#cancel-country');await page.waitForFunction(()=>!menus.pageTransition);
      check(await page.evaluate(()=>!uiPlays.length),'sound mute prevents playback '+width);
      await click('#menu-back');await page.waitForFunction(()=>!menus.pageTransition);
      await click('#headquarters');await page.waitForFunction(()=>!menus.pageTransition&&!$('hq-screen').hidden);
      check(await page.locator('#hq-roster .commander-card').count()>0,'headquarters renders after fade '+width);
      const png=PNG.sync.read(await page.screenshot({path:path.join(out,'headquarters-'+width+'.png')})),colors=new Set();
      for(let i=0;i<png.data.length;i+=92)colors.add(png.data.subarray(i,i+3).toString('hex'));
      check(colors.size>100,'nonblank rendered pixels '+width);
      const back=await page.evaluate(()=>{const p=menus.hqPresentation;if(p){const key=Object.entries(p.screen.actions).find(([,a])=>['back','main'].includes(a.command))?.[0];if(key)return '[data-layout="headquarters-'+p.key+'"] [data-native-id="'+p.layout.get(key).dataset.nativeId+'"]';}return '#hq-back';});
      await click(back);await page.waitForFunction(()=>!menus.pageTransition&&!$('main-menu').hidden);
      if(!touch){
        for(const file of ['battle1.mp3','battle2.mp3','battle3.mp3','battle4.mp3','defeat_music.mp3','sfx_click.wav','sfx_cancel.wav','sfx_select.wav','sfx_pop.wav','sfx_lvup2.wav']){
          const musicFile=file.endsWith('.mp3'),response=await page.request.get(base+'/content/'+(musicFile?'music/':'sounds/')+file),raw=await response.body();
          check(response.ok(),'served '+file);const original=await fs.readFile(path.join(root,'apk_work/original/assets',file));
          check(crypto.createHash('sha256').update(raw).digest('hex')===crypto.createHash('sha256').update(original).digest('hex'),'original bytes '+file);
          const peak=await page.evaluate(async file=>{const audio=new AudioContext(),buffer=await audio.decodeAudioData(await (await fetch('/content/'+(file.endsWith('.mp3')?'music/':'sounds/')+file)).arrayBuffer());let peak=0;for(const value of buffer.getChannelData(0))peak=Math.max(peak,Math.abs(value));await audio.close();return peak;},file);
          check(peak>.01,'non-silent decoded PCM '+file);
        }
        await page.emulateMedia({reducedMotion:'reduce'});await click('[data-mode="1"]');await page.waitForFunction(()=>!menus.pageTransition&&!$('scenario-menu').hidden);
        check(await page.locator('.menu-transition-curtain').count()===0,'reduced motion leaves no curtain');
        await page.evaluate(()=>menus.transitionPage(()=>{throw new Error('expected transition failure');}).catch(()=>{}));
        check(await page.evaluate(()=>!menus.pageTransition&&!document.querySelector('.menu-transition-curtain')),'failed transition releases curtain and lock');
      }
      report.views.push({width,height,touch,samples:samples.length});await context.close();
    }
    check(!report.errors.length&&!report.missing.length,'no script or resource errors '+JSON.stringify({errors:report.errors,missing:report.missing}));
  }catch(e){report.failure=e.message;throw e;}
  finally{await fs.writeFile(path.join(out,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
