'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root=path.resolve(__dirname,'..'),folder=path.join(root,'rewrite/reports/local-multiplayer-20261021');
const url=process.argv[2]||'http://127.0.0.1:5217';
(async()=>{
  const report={checks:0,errors:[],missing:[],screenshots:[]};
  const check=(pass,label)=>{assert(pass,label);report.checks++;};
  const fixture=JSON.parse(await fs.readFile(path.join(folder,'backend.json'),'utf8'));
  const runtime=path.resolve(process.argv[3]||path.join(root,'build/local-neutral-fix-20261021/runtime'));
  await fs.copyFile(path.join(fixture.folder,'finished/slot-1.json'),path.join(runtime,'slot-5.json'));
  await fs.copyFile(path.join(fixture.folder,'ordinary/slot-1.json'),path.join(runtime,'slot-6.json'));
  await fs.copyFile(path.join(fixture.folder,'legacy-neutral/slot-1.json'),path.join(runtime,'slot-4.json'));
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1136,height:640}});page.setDefaultTimeout(20000);
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  let starts=0;page.on('request',r=>{if(new URL(r.url()).pathname==='/api/start')starts++;});
  const shot=async name=>{await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);};
  const ready=()=>page.waitForFunction(()=>catalog&&startupReady&&document.documentElement.dataset.fontsReady==='true',null,{timeout:90000});
  const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning,null,{timeout:90000});
  const choose=async(scenario,country)=>{
    await page.evaluate(({scenario,country})=>{void start(scenario,country).catch(e=>notice(e.message));},{scenario,country});
    await page.locator('.local-selection-toolbar').waitFor();
  };
  const fit=async name=>{
    const geometry=await page.evaluate(()=>{
      const panel=document.querySelector('.local-multiplayer-panel'),dialog=panel.closest('.dialog').getBoundingClientRect();
      const list=panel.querySelector('.local-faction-list').getBoundingClientRect();
      return {x:dialog.x,y:dialog.y,right:dialog.right,bottom:dialog.bottom,listHeight:list.height,
        broken:[...panel.querySelectorAll('img')].filter(i=>!i.complete||!i.naturalWidth).length,
        overflow:panel.scrollHeight>panel.clientHeight+1};
    });
    const size=page.viewportSize();
    check(geometry.x>=0&&geometry.y>=0&&geometry.right<=size.width+1&&geometry.bottom<=size.height+1&&geometry.listHeight>35&&!geometry.overflow,'dialog fits '+name);
    check(!geometry.broken,'flags loaded '+name);await shot(name+'.png');
  };
  try{
    await page.goto(url);await ready();
    await page.locator('#main-settings').click();
    check(await page.locator('#local-multiplayer-mode').isEnabled(),'menu mode switch enabled');
    await page.locator('#local-multiplayer-mode').check();await page.locator('#confirm-settings').click();
    await page.reload();await ready();
    check(await page.evaluate(()=>settings.localMultiplayer),'pre-game preference persists');
    await choose('conquest1','fra');
    const selectable=await page.evaluate(()=>rpc('countries',{scenario:'conquest1'}));
    check(await page.locator('.local-faction-row').count()===selectable.length,'conquest selection excludes nonplayable neutral countries');
    const locked=page.locator('.local-faction-row input:disabled');
    check(await locked.count()===1&&await locked.isChecked(),'original country mandatory');
    await page.getByRole('button',{name:'全选',exact:true}).click();
    check(await page.locator('.local-faction-row input:not(:checked)').count()===0,'select all');
    await page.getByRole('button',{name:'全不选',exact:true}).click();
    check(await page.locator('.local-faction-row input:checked').count()===1,'clear all retains original');
    for(const [width,height,name]of[[1136,640,'selection-desktop'],[844,390,'selection-landscape'],[390,844,'selection-portrait']]){
      await page.setViewportSize({width,height});await page.waitForTimeout(200);await fit(name);
    }
    await page.locator('#close-detail').click();await idle();
    check(starts===0&&await page.evaluate(()=>state==null),'cancel creates no game');
    await page.setViewportSize({width:1136,height:640});
    await choose('campaign1_01');
    check(await page.locator('.local-faction-row input:disabled').count()===1,'campaign primary locked');
    await page.locator('#close-detail').click();await idle();
    await choose('campaign1_01');
    const campaignSelectable=await page.evaluate(()=>rpc('local-players',{scenario:'campaign1_01'}));
    check(await page.locator('.local-faction-row').count()===campaignSelectable.length,'campaign selection includes neutral countries');
    await page.getByRole('button',{name:'全选',exact:true}).click();
    await page.locator('#detail-confirm').click();await idle();
    check(await page.evaluate(()=>state.factions.some(f=>f.camp==='ew4.camp.4')&&state.factions.filter(f=>f.camp==='ew4.camp.4').every(f=>f.human)), 'campaign select all includes neutral human players');
    await page.evaluate(()=>showMain());
    await choose('conquest1','fra');await page.getByRole('button',{name:'全选',exact:true}).click();
    await page.locator('#detail-confirm').click();await idle();
    const ids=await page.evaluate(()=>state.localMultiplayer.humanFactions);
    const first=ids[0];
    await page.evaluate(()=>selectCell(state.units.find(u=>u.owner===state.playerFaction).cell));
    await page.locator('#end-turn').click();await idle();
    check(await page.evaluate(id=>state.playerFaction===id&&state.activeFaction===id,ids[1]),'next turn hands control to next human');
    check(await page.evaluate(()=>document.getElementById('player-flag').title===state.factions.find(f=>f.id===state.playerFaction).name),'HUD flag follows current player');
    check(await page.evaluate(()=>selected==null&&selection==null),'selection cleared on handoff');
    await page.evaluate(()=>openSettings());
    check(await page.locator('#local-multiplayer-mode').isDisabled()&&await page.locator('#local-multiplayer-mode').isChecked(),'mode locked inside local match');
    await page.locator('#confirm-settings').click();
    await page.evaluate(async()=>{await rpc('save',{slot:1,mode:2});await showMain();openSettings();});
    await page.locator('#local-multiplayer-mode').uncheck();await page.locator('#confirm-settings').click();
    await page.evaluate(()=>load(1,2));await idle();
    check(await page.evaluate(id=>!settings.localMultiplayer&&state.localMultiplayer.humanFactions.length>1&&state.playerFaction===id,ids[1]),'save restores local mode and seat independently of preference');
    check(await page.evaluate(()=>{
      if(ctx.gpu)ctx.end();
      const d=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data,colors=new Set();
      for(let i=0;i<d.length;i+=400)colors.add(d[i]+','+d[i+1]+','+d[i+2]);return colors.size>20;
    }),'battlefield canvas nonblank');
    await shot('handoff.png');
    await page.evaluate(()=>load(6,0));await idle();
    const mixedIds=await page.evaluate(()=>state.localMultiplayer.humanFactions);
    await page.locator('#end-turn').click();await idle();
    check(await page.evaluate(id=>state.playerFaction===id&&state.activeFaction===id,mixedIds[1]),'unselected AI returns control to next human');
    await page.evaluate(()=>load(5,0));await idle();await page.locator('.local-result-actions').waitFor();
    check(await page.locator('.local-won').count()===1&&await page.locator('.local-lost').count()===await page.evaluate(()=>state.factions.length-1),'sole winner with all other factions defeated');
    for(const [width,height,name]of[[1136,640,'result-desktop'],[844,390,'result-landscape'],[390,844,'result-portrait']]){
      await page.setViewportSize({width,height});await page.waitForTimeout(200);await fit(name);
    }
    await page.getByRole('button',{name:'重新开始',exact:true}).click();await idle();
    check(await page.evaluate(()=>state.phase!=='Finished'&&state.localMultiplayer.humanFactions.length===2),'result restart preserves seats');
    await page.evaluate(()=>load(5,0));await idle();await page.locator('.local-result-actions').waitFor();
    await page.getByRole('button',{name:'退出',exact:true}).click();
    await page.locator('#main-menu').waitFor({state:'visible'});
    check(await page.locator('#main-menu').isVisible(),'result exit returns to menu');
    await page.evaluate(()=>load(4,0));await idle();
    check(await page.evaluate(()=>state.localMultiplayer.humanFactions.length===state.factions.length),'old save with neutral seats remains readable');
    await page.evaluate(()=>start(state.scenario,state.startCountry,state.localMultiplayer.humanFactions));await idle();
    check(await page.evaluate(count=>state.localMultiplayer.humanFactions.length===count,selectable.length),'restarting an old save drops only nonplayable neutral seats');
    await page.evaluate(()=>start('conquest1','fra',null));await idle();
    check(await page.evaluate(()=>!state.localMultiplayer&&state.playerFaction===state.activeFaction),'single player start unchanged');
    await page.evaluate(()=>openSettings());
    check(await page.locator('#local-multiplayer-mode').isDisabled()&&!await page.locator('#local-multiplayer-mode').isChecked(),'single player cannot enable local mode in game');
    await page.locator('#confirm-settings').click();
    check(first===ids[0]&&report.errors.length===0&&report.missing.length===0,'no browser errors or missing assets');
    await fs.writeFile(path.join(folder,'web.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report));
  }catch(e){await shot('failure').catch(()=>{});throw e;}finally{await browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
