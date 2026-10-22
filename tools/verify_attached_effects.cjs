'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5401',folder=path.resolve(process.argv[3]||'build/attached-effects-20261022/browser');
const report={checks:0,errors:[],missing:[],screenshots:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
(async()=>{
  await fs.mkdir(folder,{recursive:true});const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'}),page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});await page.evaluate(()=>start('campaign1_02b',null));
    report.cues=await page.evaluate(async()=>{
      const unit={...state.units.find(u=>u.owner===state.playerFaction),health:60,veterancy:'0',commanderId:'fixture.general'},previous={...state,units:[unit]},results={};
      const sound=effects.sound,effect=effects.effect,timeline=effects.timeline,calls=[];
      effects.sound=n=>calls.push(n);effects.effect=n=>calls.push(n);effects.timeline=n=>calls.push(n);
      const run=(name,patch,notices,extra={})=>{calls.length=0;playAttachedEffects(previous,{...previous,units:[{...unit,...patch}],notices,...extra});results[name]=[...calls];};
      try{
        run('advance',{veterancy:'1',health:80},[{kind:'battle.experience',subject:unit.id},{kind:'battle.experience',subject:unit.id},{kind:'unit.healed',subject:unit.id}]);
        run('xpOnly',{},[{kind:'battle.experience',subject:unit.id}]);
        run('train',{veterancy:'1'},[{kind:'unit.trained',subject:unit.id}]);
        run('gold',{veterancy:'1'},[{kind:'unit.item_used',subject:unit.id}]);
        run('rank',{},[{kind:'commander.rank_advanced',subject:unit.commanderId},{kind:'commander.rank_advanced',subject:unit.commanderId},{kind:'commander.title_advanced',subject:unit.commanderId}]);
        run('healed',{health:80},[{kind:'unit.healed',subject:unit.id},{kind:'unit.healed',subject:unit.id}],{suppliedUnit:unit.id});
        run('notHealed',{},[{kind:'unit.healed',subject:unit.id}],{suppliedUnit:unit.id});
        run('plainHealth',{health:80},[]);
        run('dead',{},[{kind:'commander.rank_advanced',subject:unit.commanderId},{kind:'unit.healed',subject:unit.id}],{units:[]});
        const original=state,next={...state,revision:state.revision+1,combat:null,movements:[],recruited:[],suppliedUnit:null,units:state.units.map(u=>u.id===unit.id?{...u,health:Math.max(1,u.health-20)}:u),notices:[]};
        await installState(next,true);const healed={...next,revision:next.revision+1,units:original.units,notices:[{kind:'unit.healed',subject:unit.id}]};
        calls.length=0;await installState(healed);results.install=[...calls];calls.length=0;await installState(healed);results.repeat=[...calls];
        calls.length=0;await installState({...healed,revision:healed.revision+1},true);results.reload=[...calls];
        await installState({...next,revision:state.revision+1},true);
        skipAi=true;calls.length=0;try{await installState({...healed,revision:state.revision+1});results.skip=[...calls];}finally{skipAi=false;}
        await installState(original,true);
      }finally{effects.sound=sound;effects.effect=effect;effects.timeline=timeline;effects.clear();}
      return results;
    });
    for(const [key,expected] of Object.entries({advance:['effect_upgrade.xml','sfx_lvup.wav'],xpOnly:[],train:['effect_upgrade.xml'],gold:['effect_upgrade.xml'],rank:['upgraderank','upgradenobility'],healed:['sfx_supply.wav','effect_recover.xml'],notHealed:[],plainHealth:[],dead:[],install:['sfx_supply.wav','effect_recover.xml'],repeat:[],reload:[],skip:[]}))check(JSON.stringify(report.cues[key])===JSON.stringify(expected),key+' cues');
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});report['pixels'+width]=await page.evaluate(()=>{
        effects.clear();const unit=state.units.find(u=>u.owner===state.playerFaction),p=point(unit.cell),now=performance.now();view.x=p.x;view.y=p.y;view.zoom=1;clampView();
        effects.effect('effect_upgrade.xml',p.x,p.y,{started:now-150});renderDirty=true;render(now);const a=ctx.getImageData(0,0,canvas.width,canvas.height).data;
        effects.clear();renderDirty=true;render(now);const b=ctx.getImageData(0,0,canvas.width,canvas.height).data;let changed=0;for(let i=0;i<a.length;i+=4)if(a[i]!==b[i]||a[i+1]!==b[i+1]||a[i+2]!==b[i+2])changed++;
        effects.effect('effect_upgrade.xml',p.x,p.y,{started:now-150});renderDirty=true;render(now);return changed;
      });check(report['pixels'+width]>20,'upgrade particles visible '+width);const name='upgrade-'+width+'.png';await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);
    }
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing resources');
  }finally{await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
