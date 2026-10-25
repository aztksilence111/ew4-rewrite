'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const runtime=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=runtime('playwright'),{PNG}=runtime('pngjs');
const base=process.argv[2]||'http://127.0.0.1:5510',folder=path.resolve(process.argv[3]||'build/device-plan-20261024/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,cases:[],errors:[],missing:[]};
  const check=(ok,label)=>{assert.ok(ok,label);report.checks++;};
  try{
    for(const [name,width,height] of [['desktop',1280,720],['phone',844,390]])for(const backend of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1,hasTouch:true});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      const starts=[];page.on('request',r=>{if(r.url().includes('/api/start'))starts.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      check(await page.evaluate(()=>/^[a-f0-9]{12}\/[a-f0-9]{8}\/(web-jit|android-jit|android-aot)$/.test(performanceLog.build)),'automatic source/build identifier');
      const loadChecks=await page.evaluate(async()=>{
        const original=fetch;let requests=0;
        window.fetch=async(...args)=>{if(String(args[0]).includes('/animations/choose.json'))requests++;return original(...args);};
        libraries.delete('choose');
        const [a,b]=await Promise.all([loadLibrary('choose'),loadLibrary('choose')]);
        window.fetch=original;
        return {requests,same:a===b};
      });check(loadChecks.requests===1&&loadChecks.same,'in-flight library load deduplicates');
      const retry=await page.evaluate(async()=>{
        const original=fetch;let requests=0;
        libraries.delete('choose');
        window.fetch=async(...args)=>{if(String(args[0]).includes('/animations/choose.json')&&++requests===1)throw Error('injected fetch failure');return original(...args);};
        try{await loadLibrary('choose');}catch(_){}
        await loadLibrary('choose');window.fetch=original;
        return requests===2&&!libraryLoads.has('choose');
      });check(retry,'failed library load retries');
      check(await page.evaluate(async()=>{
        const original=loadImageCore;let calls=0;
        loadImageCore=(...args)=>{calls++;return original(...args);};
        const [a,b]=await Promise.all([loadImage('images/tiles.png'),loadImage('images/tiles.png')]);
        loadImageCore=original;return calls===1&&a===b&&!imageLoads.size;
      }),'in-flight image decoding deduplicates');
      await page.evaluate(()=>chooseScenario(catalog.scenarios.find(s=>s.id==='conquest1')));
      await page.waitForTimeout(500);
      check(starts.length===0,'country prefetch never starts a session');
      const cancelled=await page.evaluate(()=>{const before=preloadVersion;$('cancel-country').click();return preloadVersion>before&&$('countries').hidden;});
      check(cancelled,'leaving country page cancels remaining queue');
      await page.evaluate(()=>chooseScenario(catalog.scenarios.find(s=>s.id==='conquest1')));
      await page.evaluate(()=>{settings.autoTurnFast=true;return start('conquest1','fra');});
      check(starts.length===1,'immediate start creates exactly one session');
      await page.waitForFunction(()=>!busy&&state&&$('loading-screen').hidden);
      const entry=await page.evaluate(()=>performanceLog.records.filter(r=>r.kind==='entry').at(-1));
      check(entry.status==='ready'&&['backend','libraries','images','templates','firstFrame'].every(k=>entry.stages[k]>=0),'entry stage measurements');
      await page.evaluate(()=>{stageIntroOpen=false;$('detail-modal').hidden=true;$('tutorial-talk').hidden=true;requestRender();});
      await page.waitForTimeout(500);
      const file=path.join(folder,name+'-'+backend+'.png');
      const png=PNG.sync.read(await page.screenshot({path:file})),colors=new Set();
      for(let y=Math.floor(height*.25);y<height*.7;y+=7)for(let x=Math.floor(width*.2);x<width*.7;x+=7){const i=(y*png.width+x)*4;colors.add([png.data[i]>>3,png.data[i+1]>>3,png.data[i+2]>>3].join(','));}
      check(colors.size>30,'nonblank battlefield pixels');
      const stateChecks=await page.evaluate(async()=>{
        const first=performanceLog.records.length;
        await act('end');
        const turn=performanceLog.records.slice(first).find(r=>r.kind==='turn');
        $('menu-button').click();
        const caption=$('performance-caption'),box=caption.getBoundingClientRect(),parent=caption.parentElement.getBoundingClientRect();
        const result={turn,caption:caption.textContent,fits:box.left>=parent.left&&box.right<=parent.right+1,world:state.activeFaction,ai:aiRunning};
        return result;
      });
      check(stateChecks.turn?.status==='returned'&&stateChecks.turn.elapsedMs>0&&!stateChecks.ai,'whole turn automatically timed through control return');
      check(stateChecks.fits&&stateChecks.caption.includes(performanceLogBuild(entry)),'timing/build caption fits pause');
      await page.waitForTimeout(60);
      await page.screenshot({path:path.join(folder,name+'-'+backend+'-pause.png')});
      check(await page.evaluate(()=>{const caption=$('performance-caption');return caption.scrollWidth<=caption.clientWidth+1;}),'rendered timing caption does not overflow');
      await page.evaluate(()=>$('resume').click());
      await page.evaluate(()=>{facings.set('obsolete',-1);return showMain();});
      check(await page.evaluate(()=>facings.size===0),'battle-facing cache cleared on exit');
      check(await page.evaluate(()=>{for(let i=0;i<40;i++)performanceLog.save({kind:'test',elapsedMs:i});return performanceLog.records.length===32&&JSON.parse(localStorage.getItem('ew4.performance')).length===32;}),'automatic timing history stays bounded');
      report.cases.push({name,backend,colors:colors.size,entry,turn:stateChecks.turn});
      await page.close();
    }
    check(report.errors.length===0,'no page exceptions');check(report.missing.length===0,'no missing assets');
  }finally{await fs.writeFile(path.join(folder,'result.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,cases:report.cases.length}));
})().catch(e=>{console.error(e);process.exitCode=1;});
function performanceLogBuild(entry){return entry.build;}
