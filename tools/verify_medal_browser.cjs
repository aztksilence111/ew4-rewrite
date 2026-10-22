'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5402',folder=path.resolve(process.argv[3]||'build/medal-drops-20261022/browser');
const report={checks:0,errors:[],missing:[],screenshots:[]};
const check=(v,label)=>{assert.ok(v,label);report.checks++;};
(async()=>{
  await fs.mkdir(folder,{recursive:true});const fixture=JSON.parse(await fs.readFile(path.join(folder,'../browser-fixture.json'),'utf8'));
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    report.attack=await page.evaluate(async fixture=>{
      await load(1,0);if(!state)throw Error($('notice').textContent);settings.speed=5;const sounds=[],timelines=[],sound=effects.sound,timeline=effects.timeline;
      effects.sound=function(name){sounds.push(name);};effects.timeline=function(name,...args){timelines.push(name);return timeline.call(this,name,...args);};
      try{await act('attack',{unit:fixture.attacker,target:fixture.defender});}
      finally{effects.sound=sound;effects.timeline=timeline;}
      return {medals:state.medals,sounds,timelines,notices:state.notices};
    },fixture);
    check(report.attack.medals===fixture.medals+1,'actual legal campaign attack immediately credits one medal');
    check(report.attack.notices.filter(n=>n.kind==='battle.medal_dropped').length===1,'one successful drop notice');
    check(report.attack.sounds.filter(n=>n==='sfx_lvup.wav').length===1&&report.attack.timelines.filter(n=>n==='getmedal').length===1,'original medal timeline and sound triggered once');
    report.repeat=await page.evaluate(async()=>{
      const sounds=[],timelines=[],sound=effects.sound,timeline=effects.timeline;
      effects.sound=n=>sounds.push(n);effects.timeline=n=>timelines.push(n);
      try{await installState(state);await rpc('save',{slot:2});await load(2,0);await load(1,0);}
      finally{effects.sound=sound;effects.timeline=timeline;}
      return {medals:state.medals,sounds,timelines};
    });
    check(report.repeat.medals===fixture.medals+1&&!report.repeat.timelines.includes('getmedal')&&!report.repeat.sounds.includes('sfx_lvup.wav'),'refresh/reload/earlier slot preserve balance and suppress cue replay');
    await page.evaluate(async fixture=>{await act('attack',{unit:fixture.attacker,target:fixture.defender});},fixture);
    check(await page.evaluate(()=>state.medals)===fixture.medals+1,'replaying the same pre-drop slot does not pay again');
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});
      report['pixels'+width]=await page.evaluate(fixture=>{
        effects.clear();const p=point(state.units.find(u=>u.id===fixture.attacker).cell);view.x=p.x;view.y=p.y;view.zoom=1;clampView();
        const time=performance.now(),fx=effects.timeline('getmedal',p.x,p.y,{started:time-180,speed:1});renderDirty=true;render(time);
        const before=ctx.getImageData(0,0,canvas.width,canvas.height).data;effects.clear();renderDirty=true;render(time);
        const after=ctx.getImageData(0,0,canvas.width,canvas.height).data;let changed=0;for(let i=0;i<before.length;i+=4)if(before[i]!==after[i]||before[i+1]!==after[i+1]||before[i+2]!==after[i+2])changed++;
        effects.timeline('getmedal',p.x,p.y,{started:time-180,speed:1});renderDirty=true;render(time);return changed;
      },fixture);
      check(report['pixels'+width]>20,'medal animation renders visible pixels '+width);
      const name='medal-'+width+'.png';await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);
    }
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing resources');
  }finally{await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
