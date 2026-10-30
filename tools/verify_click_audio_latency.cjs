'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright');
const base=process.argv[2]||'http://127.0.0.1:56153',out=path.resolve(process.argv[3]||'build/click-audio-check-20261030/latency');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],runs:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio','--autoplay-policy=document-user-activation-required']});
  try{
    for(const touch of [false,true]){
      const context=await browser.newContext({viewport:touch?{width:390,height:844}:{width:1280,height:720},hasTouch:touch,isMobile:touch}),page=await context.newPage();
      if(!touch)await page.route(/\/(?:compat\/)?(?:effects|app|ui-audio)\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',path:path.resolve('rewrite/Ew4.Web/wwwroot',new URL(route.request().url()).pathname.split('/').pop())}));
      page.on('pageerror',error=>report.errors.push(error.message));
      await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      const prepared=await page.evaluate(async()=>{
        settings.sound=60;settings.music=0;
        const audio=effects.sounds,ctx=audio.context;
        await ctx.suspend();
        window.audioProbe={calls:[],starts:[],pcm:[],gains:[],sources:new Set(),requests:0};
        const fetch=window.fetch;window.fetch=(...args)=>{if(String(args[0]).includes('/content/sounds/'))audioProbe.requests++;return fetch(...args);};
        const processor=ctx.createScriptProcessor(256,1,1);processor.connect(ctx.destination);
        processor.onaudioprocess=event=>{let peak=0;for(const v of event.inputBuffer.getChannelData(0))peak=Math.max(peak,Math.abs(v));if(peak>.01)audioProbe.pcm.push(performance.now());};
        const createGain=ctx.createGain.bind(ctx);ctx.createGain=()=>{const gain=createGain();gain.connect(processor);audioProbe.gains.push(gain);return gain;};
        const start=AudioBufferSourceNode.prototype.start;AudioBufferSourceNode.prototype.start=function(...args){audioProbe.starts.push({at:performance.now(),offset:args[1]||0,duration:this.buffer.duration});audioProbe.sources.add(this);return start.apply(this,args);};
        const sound=effects.sound.bind(effects);effects.sound=file=>{audioProbe.calls.push({file,at:performance.now()});sound(file);};
        const button=document.createElement('button');button.id='latency-button';button.textContent='test';button.style.cssText='position:fixed;top:0;left:0;width:100px;height:50px;z-index:30000';document.body.append(button);
        return {state:ctx.state,count:audio.buffers.size,latencyHint:ctx.baseLatency,onsets:[...audio.buffers].map(([file,buffer])=>{let first=buffer.length;for(let channel=0;channel<buffer.numberOfChannels;channel++){const pcm=buffer.getChannelData(channel);const i=pcm.findIndex(v=>Math.abs(v)>.01);if(i>=0)first=Math.min(first,i);}return {file,ms:first/buffer.sampleRate*1000};})};
      });
      check(prepared.count===13,'all presentation sounds decoded before first click');
      check(prepared.state==='suspended','first click must wake a suspended context');
      const click=()=>page.locator('#latency-button')[touch?'tap':'click']();
      await click();await page.waitForFunction(()=>audioProbe.pcm.length>0,null,{timeout:5000});
      const first=await page.evaluate(()=>({scheduled:audioProbe.starts[0].at-audioProbe.calls[0].at,pcm:audioProbe.pcm[0]-audioProbe.calls[0].at,state:effects.sounds.context.state}));
      check(first.state==='running'&&first.scheduled<50,'first activation schedules within 50ms');
      check(first.pcm<150,'first click produces real PCM within 150ms');
      for(let i=0;i<12;i++)await click();
      await page.locator('#latency-button').focus();await page.keyboard.press('Enter');
      const rapid=await page.evaluate(()=>({calls:audioProbe.calls.length,starts:audioProbe.starts.length,unique:audioProbe.sources.size,requests:audioProbe.requests,
        delays:audioProbe.starts.map((v,i)=>v.at-audioProbe.calls[i].at),offsets:audioProbe.starts.map(v=>v.offset),gains:audioProbe.gains.map(v=>v.gain.value)}));
      check(rapid.calls===14&&rapid.starts===14&&rapid.unique===14,'first, repeated and keyboard clicks each start a fresh source once');
      check(rapid.requests===0,'clicks need no fetch or decoder');check(rapid.offsets.every(v=>v===0),'every click starts at sample zero');
      check(rapid.delays.every(v=>v<50),'every click schedules within 50ms');check(rapid.gains.every(v=>Math.abs(v-.6)<.0001),'sound volume applied independently');
      await page.evaluate(()=>{settings.sound=0;});await click();
      check(await page.evaluate(()=>audioProbe.starts.length===14),'muted click starts no source');
      await page.evaluate(()=>{settings.sound=60;ew4SetAppVisible(false);effects.sound('sfx_click.wav');});
      check(await page.evaluate(()=>audioProbe.starts.length===14&&effects.voices.size===0),'background clears voices and prevents playback');
      await page.evaluate(()=>ew4SetAppVisible(true));await click();
      await page.waitForFunction(()=>audioProbe.starts.length===15);
      check(await page.evaluate(()=>effects.sounds.context.state==='running'),'foreground resumes short audio');
      await page.waitForTimeout(700);
      check(await page.evaluate(()=>effects.voices.size===0),'ended nodes released');
      // Compare the old media-element path on the same browser and same WAV.
      const baseline=await page.evaluate(async()=>{
        const pool=new Audio('/content/sounds/sfx_click.wav');pool.preload='auto';
        await new Promise((resolve,reject)=>{pool.oncanplaythrough=resolve;pool.onerror=reject;pool.load();});
        const play=audio=>new Promise((resolve,reject)=>{const at=performance.now();audio.onplaying=()=>resolve(performance.now()-at);audio.play().catch(reject);});
        const first=await play(pool);await new Promise(resolve=>{pool.onended=resolve;});
        const replayPosition=pool.currentTime;await play(pool);pool.pause();
        const clone=pool.cloneNode();const cloned=await play(clone);clone.pause();return {first,cloned,replayPosition,duration:pool.duration};
      });
      check(baseline.replayPosition>=baseline.duration-.01,'old pool retained ended playback position');
      await page.evaluate(()=>$('latency-button').remove());
      await page.screenshot({path:path.join(out,touch?'mobile.png':'desktop.png')});
      report.runs.push({touch,prepared,first,rapid,baseline});await context.close();
    }
    check(!report.errors.length,'no browser script errors');
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(path.join(out,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors,runs:report.runs.map(run=>({touch:run.touch,first:run.first,
    repeatMaxMs:Math.max(...run.rapid.delays.slice(1)),clicks:run.rapid.starts,requests:run.rapid.requests,
    clickOnsetMs:run.prepared.onsets.find(sound=>sound.file==='sfx_click.wav').ms}))}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
