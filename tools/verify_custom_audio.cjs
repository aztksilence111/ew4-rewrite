'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56225',root=path.resolve(__dirname,'..');
let archive=process.argv[3];
(async()=>{
  if(!archive){
    const folder=path.join(root,'build/custom-audio-20261109/fixtures'),candidates=[];
    for(const entry of await fs.readdir(folder,{withFileTypes:true}))if(entry.isDirectory()){
      const file=path.join(folder,entry.name,'audio.ew4mod');try{candidates.push({file,time:(await fs.stat(file)).mtimeMs});}catch(error){if(error.code!=='ENOENT')throw error;}
    }
    candidates.sort((a,b)=>b.time-a.time);assert.ok(candidates.length,'run --custom-audio-only first');archive=candidates[0].file;
  }
  let checks=0;const errors=[],missing=[],check=(v,label)=>{assert.ok(v,label);checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio','--autoplay-policy=document-user-activation-required']});
  const request=await browser.newContext();
  const rpc=async body=>(await request.request.post(base+'/api/mods',{data:body})).json();
  try{
    const response=await request.request.post(base+'/api/mod-import',{multipart:{file:{name:'audio.ew4mod',mimeType:'application/zip',buffer:await fs.readFile(archive)},id:'custom-audio',name:'custom-audio'}});
    check(response.ok(),'package upload succeeds');let status;
    for(let attempt=0;attempt<120;attempt++){
      status=await rpc({});if(['complete','error'].includes(status.import.status))break;
      await new Promise(resolve=>setTimeout(resolve,500));
    }
    check(status.import.status==='complete','package imports: '+status.import.message);
    const key=status.import.key;check((await rpc({op:'activate',key})).active===key,'package activates');
    for(const source of [false,true])for(const [width,height,touch] of [[1280,720,false],[844,390,true],[390,844,true]]){
      const context=await browser.newContext({viewport:{width,height},hasTouch:touch,isMobile:touch}),page=await context.newPage();
      if(source)await page.route(/\/(?:compat\/)?(?:app|music|effects|ui-audio)\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',path:path.join(root,'rewrite/Ew4.Web/wwwroot',new URL(route.request().url()).pathname.split('/').pop())}));
      page.on('pageerror',e=>errors.push(e.message));page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))missing.push(r.url());});
      await page.goto(base);await page.waitForFunction(()=>startupReady&&!sessionTransition,null,{timeout:90000});
      check(await page.evaluate(()=>music.file==='新增配乐.mp3'&&music.configuration.playlistMode==='sequence'),'imported menu playlist loaded');
      check(await page.evaluate(()=>effects.audio.has('自定义点击.wav')&&effects.sounds.resolve('sfx_click.wav')==='自定义点击.wav'&&!effects.audio.has('sfx_pop.wav')),'replacement preloaded and muted popup skipped');
      await page.evaluate(()=>{settings.sound=60;window.audioStarted=[];const start=AudioBufferSourceNode.prototype.start;AudioBufferSourceNode.prototype.start=function(...args){audioStarted.push(this.buffer);return start.apply(this,args);};});
      await page.locator('#main-settings')[touch?'tap':'click']();
      check(await page.evaluate(()=>audioStarted.length===1&&audioStarted[0]===effects.audio.get('自定义点击.wav')),'trusted settings click plays mapped PCM and no popup');
      await page.locator('#music-volume').fill('50');
      await page.waitForFunction(()=>music.audio.currentTime>.05&&!music.audio.paused,null,{timeout:20000});
      check(await page.evaluate(()=>music.audio.duration>1&&!music.audio.loop&&music.audio.volume===.5),'custom unicode BGM actually decodes and plays');
      // Local WebView handlers do not guarantee byte ranges or a seekable stream.
      // Exercise the existing complete-blob path before seeking to a real ending.
      await page.evaluate(()=>music.loadBlob());
      await page.waitForFunction(()=>Number.isFinite(music.audio.duration)&&music.audio.duration>1&&music.audio.currentTime>.05&&!music.audio.paused,null,{timeout:20000});
      check(await page.evaluate(()=>music.audio.src.startsWith('blob:')),'custom track supports complete-blob recovery');
      await page.evaluate(()=>music.audio.currentTime=music.audio.duration-.15);
      await page.waitForFunction(()=>music.file==='battle1.mp3'&&music.audio.currentTime>.05&&!music.audio.paused,null,{timeout:20000});
      check(true,'natural track ending advances imported playlist');
      await page.evaluate(()=>music.sync({scenario:'conquest1'}));
      check(await page.evaluate(()=>['battle3.mp3','battle4.mp3'].includes(music.file)),'scenario playlist selected');
      await page.evaluate(()=>music.sync({scenario:'conquest1',battleResult:{id:1,outcome:'Victory'}}));
      await page.waitForFunction(()=>music.file==='battle2.mp3'&&music.audio.currentTime>.05&&!music.audio.paused,null,{timeout:20000});
      check(true,'scenario victory track actually plays');
      await page.evaluate(()=>music.sync({scenario:'conquest1',battleResult:{id:2,outcome:'Defeat'}}));
      check(await page.evaluate(()=>music.file===null&&!music.audio.getAttribute('src')&&music.audio.paused),'scenario defeat silence removes source');
      await page.evaluate(async()=>{await effects.preloadSounds(['mod.alert']);effects.sound('mod.alert');});
      check(await page.evaluate(()=>audioStarted.at(-1)===effects.audio.get('sfx_lvup2.wav')&&audioStarted.at(-1).duration>0),'new alias plays without effects whitelist edits');
      await page.evaluate(()=>{music.menu();ew4SetAppVisible(false);});
      check(await page.evaluate(()=>music.audio.paused&&effects.voices.size===0),'background stops BGM and effect voices');
      await page.evaluate(()=>ew4SetAppVisible(true));
      await page.waitForFunction(()=>!music.audio.paused,null,{timeout:20000});
      check(true,'foreground resumes custom BGM');await context.close();
    }
    check((await rpc({op:'activate',key:null})).active===null,'deactivation succeeds');
    const context=await browser.newContext(),page=await context.newPage();await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    check(await page.evaluate(()=>music.file==='battle1.mp3'&&Object.keys(effects.sounds.aliases).length===0&&effects.audio.has('sfx_click.wav')),'deactivation restores builtin music and click preload');
    await context.close();check(!errors.length&&!missing.length,'no browser exceptions or missing resources: '+JSON.stringify({errors,missing}));
    console.log(JSON.stringify({checks,views:6,errors,missing}));
  }finally{await request.close();await browser.close();}
})().catch(error=>{console.error(error.message);process.exitCode=1;});
