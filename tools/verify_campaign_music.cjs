'use strict';
const fs=require('node:fs/promises'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root='E:/Workplace/game/o4mod',folder=root+'/build/campaign-music-20261023/browser',url=process.argv[2]||'http://127.0.0.1:5431';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],screenshots:[]},check=(value,label)=>{assert(value,label);report.checks++;};
  const browser=await chromium.launch({headless:true,args:['--mute-audio'],executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',error=>report.errors.push(error.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  try{
    await page.goto(url);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    check(await page.evaluate(()=>music.audio.paused&&!music.unlocked),'music waits for initial gesture');
    await page.locator('#main-settings').click();
    check(await page.evaluate(()=>settings.music===0&&settings.sound===0&&music.audio.paused),'local Web preview starts silent after a gesture');
    await page.locator('#music-volume').fill('50');
    await page.waitForFunction(()=>!music.audio.paused&&music.audio.currentTime>0,null,{timeout:20000});
    check(await page.evaluate(()=>music.audio.src.endsWith('/music/battle1.mp3')&&music.audio.loop),'original main menu loop plays');
    await page.locator('#music-volume').fill('0');check(await page.evaluate(()=>music.audio.paused&&music.audio.volume===0),'music zero pauses');
    await page.locator('#music-volume').fill('37');await page.waitForFunction(()=>!music.audio.paused);
    check(await page.evaluate(()=>music.audio.volume===.37),'music slider controls live playback');
    await page.evaluate(()=>ew4SetAppVisible(false));check(await page.evaluate(()=>music.audio.paused),'native background pauses');
    await page.evaluate(()=>ew4SetAppVisible(true));await page.waitForFunction(()=>!music.audio.paused);
    await page.locator('#confirm-settings').click();
    const decoded=await page.evaluate(async()=>{
      const files=[...new Set([music.configuration.menu,...music.configuration.battle,music.configuration.defeat])];
      const context=new AudioContext();
      try{return await Promise.all(files.map(async file=>{const response=await fetch('/content/music/'+file),buffer=await context.decodeAudioData(await response.arrayBuffer());
        const samples=buffer.getChannelData(0);let peak=0;for(let i=0;i<samples.length;i+=101)peak=Math.max(peak,Math.abs(samples[i]));return {file,duration:buffer.duration,peak};}));}
      finally{await context.close();}
    });
    check(decoded.length===5&&decoded.every(track=>track.duration>1&&track.peak>.01),'all five original MP3s decode to non-silent audio');report.audio=decoded;
    await page.locator('[data-mode="1"]').click();
    let entries=0;
    for(let chapter=1;chapter<=6;chapter++){
      await page.evaluate(async id=>{await menus.openChapter(catalog.chapters.find(c=>c.id===id));},chapter);
      const count=await page.locator('#campaign-battles>[data-campaign]').count();entries+=count;
      check(count===[18,17,15,13,11,10][chapter-1],'all initial campaign entries in chapter '+chapter);
      if(chapter===5)check(await page.locator('[data-campaign="campaign5_05"]').count()===1&&await page.locator('[data-campaign="campaign5_09"]').count()===1,'USA hidden stages initially visible');
    }
    check(entries===84,'84 primary campaign entries with two country branches');
    await page.evaluate(()=>{window.branchSelections=[];menus.api.start=async id=>window.branchSelections.push(id);});
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});
      for(const id of ['campaign3_08','campaign4_10']){
        await page.evaluate(async id=>menus.confirmBattle(catalog.presentations[id]),id);
        const layout=page.locator('[data-layout="form_selcountry"]');await layout.waitFor();
        check(await layout.locator('[data-native-id="group_select"]').count()===1,'original selected frame '+id+' '+width);
        for(const index of [0,1]){
          const button=layout.locator('[data-native-id="'+(index?'btn_right':'btn_left')+'"]');await button.click();
          check(await button.getAttribute('aria-pressed')==='true','original country selection '+id+' '+index+' '+width);
          const fits=await page.evaluate(()=>{
            const dialog=document.querySelector('#detail-modal>.dialog').getBoundingClientRect(),body=document.querySelector('[data-layout="form_selcountry"]');
            return [...body.querySelectorAll('[data-native-id="btn_left"],[data-native-id="btn_right"],[data-native-id="group_select"]')].every(el=>{
              const r=el.getBoundingClientRect();return r.width>0&&r.left>=dialog.left-1&&r.right<=dialog.right+1&&r.top>=dialog.top&&r.bottom<=dialog.bottom+1;});
          });check(fits,'country controls fit dialog '+width);
        }
        const path=folder+'/'+id+'-'+width+'.png';await page.screenshot({path});report.screenshots.push(path);
        await page.locator('#detail-confirm').click();check(await page.evaluate(()=>document.getElementById('detail-modal').hidden),'confirm closes selector');
        await page.evaluate(async id=>menus.confirmBattle(catalog.presentations[id]),id);
        await page.locator('#detail-confirm').click();
      }
    }
    const selections=await page.evaluate(()=>window.branchSelections);
    check(selections.every((id,i)=>id===['campaign3_08b','campaign3_08','campaign4_10b','campaign4_10'][i%4]),'both selections route to the corresponding BTLs');
    for(const [id,country] of [['campaign3_08','pru'],['campaign3_08b','aus'],['campaign4_10','tur'],['campaign4_10b','rus']]){
      const result=await page.evaluate(async id=>rpc('start',{scenario:id}),id);
      check(result.scenario===id&&result.factions.find(f=>f.id===result.playerFaction).country===country,'real backend branch '+id+' '+country);
    }
    const tracks=await page.evaluate(()=>{const chosen=[];const random=Math.random;try{for(let i=0;i<4;i++){Math.random=()=>i/4;music.sync({scenario:'campaign1_01'},true);chosen.push(music.file);}}finally{Math.random=random;}return chosen;});
    check(tracks.join(',')==='battle1.mp3,battle2.mp3,battle3.mp3,battle4.mp3','all four random battle tracks selectable');
    await page.evaluate(()=>music.sync({scenario:'campaign1_01',battleResult:{id:'audio-test',outcome:'Defeat'}}));
    await page.waitForFunction(()=>music.file==='defeat_music.mp3'&&!music.audio.paused);
    check(await page.evaluate(()=>music.audio.loop),'defeat loop plays');
    await page.evaluate(()=>music.menu());await page.waitForFunction(()=>music.file==='battle1.mp3'&&!music.audio.paused);
    const before=await page.evaluate(()=>music.audio.currentTime);await page.waitForTimeout(200);await page.evaluate(()=>music.menu());
    check(await page.evaluate(before=>music.audio.currentTime>=before,before),'same menu scene does not restart audio');
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing resources');
  }finally{await fs.writeFile(folder+'/checks.json',JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,screenshots:report.screenshots.length}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
