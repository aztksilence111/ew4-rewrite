'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5584';
const folder=path.resolve(process.argv[3]||'build/feedback-audit-20261027');
const report={scope:'Current frontend, isolated host/profile; no Android device measurement',errors:[],missing:[],observations:{}};
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    const context=await browser.newContext({viewport:{width:844,height:390},hasTouch:true,isMobile:true});
    const page=await context.newPage();page.on('pageerror',e=>report.errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400)report.missing.push({status:r.status(),url:r.url()});});
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    report.observations.musicInitial=await page.evaluate(()=>({volume:settings.music,paused:music.audio.paused,configuration:music.configuration}));
    await page.locator('#main-settings').tap();await page.locator('#music-volume').fill('50');
    await page.waitForFunction(()=>!music.audio.paused&&music.audio.currentTime>.05,null,{timeout:20000});
    report.observations.musicDecoded=await page.evaluate(async()=>{
      const context=new AudioContext();
      try{return await Promise.all([...new Set([music.configuration.menu,...music.configuration.battle,music.configuration.defeat])].map(async file=>{
        const response=await fetch('/content/music/'+file),audio=await context.decodeAudioData(await response.arrayBuffer());
        const channel=audio.getChannelData(0);let peak=0;for(let i=0;i<channel.length;i+=101)peak=Math.max(peak,Math.abs(channel[i]));
        return {file,duration:audio.duration,peak};
      }));}finally{await context.close();}
    });
    await page.evaluate(()=>ew4SetAppVisible(false));
    report.observations.musicBackgroundPaused=await page.evaluate(()=>music.audio.paused);
    await page.evaluate(()=>ew4SetAppVisible(true));await page.waitForFunction(()=>!music.audio.paused);
    report.observations.musicForegroundResumed=true;
    await page.locator('#confirm-settings').tap();
    await page.evaluate(()=>{settings.music=0;music.resume();return start('conquest1','fra');});
    await page.waitForFunction(()=>state&&!busy&&!aiRunning&&!sessionTransition&&!state.dialogue,null,{timeout:90000});
    report.observations.selection=await page.evaluate(async()=>{
      const cells=state.cells.map((s,i)=>({s,i})),unit=state.units.find(u=>u.owner===state.playerFaction);
      const empty=({s,i})=>s.active&&!state.units.some(u=>u.cell===i);
      const sites=[['unit',unit.cell],['building',cells.find(c=>empty(c)&&c.s.buildingKind&&c.s.buildingKind!=='farmland').i],
        ['farm',cells.find(c=>empty(c)&&c.s.buildingKind==='farmland').i],
        ['terrain',cells.find(c=>empty(c)&&!c.s.buildingKind&&Ew4Battlefield.terrainRows(c.s).length).i]];
      const resources=[...document.querySelectorAll('#resources .resource-value')].map(el=>el.firstElementChild);
      const result=[];
      for(const [kind,cell] of sites){
        selected=undefined;selection=undefined;await selectCell(cell);const first={cell:selection?.cell,hidden:$('selection').hidden,unit:selected?.id};
        const terrain=$('terrain-info').firstElementChild;await selectCell(cell);
        result.push({kind,cell,first,second:{cell:selection?.cell,hidden:$('selection').hidden,unit:selected?.id},terrainRows:Ew4Battlefield.terrainRows(state.cells[cell]),terrainNodeReused:terrain===$('terrain-info').firstElementChild});
      }
      return {cases:result,resourceNodesRetained:resources.every((el,i)=>el===document.querySelectorAll('#resources .resource-value')[i].firstElementChild)};
    });
    report.observations.buttonFilters={};
    await page.mouse.move(400,190);
    report.observations.buttonFilters.rest=await page.locator('#menu-button').evaluate(el=>getComputedStyle(el).filter);
    await page.locator('#menu-button').hover();
    report.observations.buttonFilters.hover=await page.locator('#menu-button').evaluate(el=>getComputedStyle(el).filter);
    const button=await page.locator('#menu-button').boundingBox();await page.mouse.move(button.x+button.width/2,button.y+button.height/2);await page.mouse.down();
    report.observations.buttonFilters.pressed=await page.locator('#menu-button').evaluate(el=>getComputedStyle(el).filter);await page.mouse.up();
    await page.waitForFunction(()=>$('pause').classList.contains('pause-objectives-active'),null,{timeout:20000});
    await page.waitForTimeout(350);
    report.observations.pauseLayouts=[];
    for(const [width,height] of [[844,390],[1280,720]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(100);
      const geometry=await page.evaluate(()=>{
        const bar=document.querySelector('.pause-dialog>.menu-bar'),title=bar.querySelector('h2'),b=bar.getBoundingClientRect(),t=title.getBoundingClientRect();
        const before=getComputedStyle(bar,'::before'),after=getComputedStyle(bar,'::after');
        return {viewport:[innerWidth,innerHeight],bar:{x:b.x,width:b.width},title:{x:t.x,width:t.width},
          leftPattern:{left:before.left,width:before.width},rightPattern:{right:after.right,width:after.width},closeWidth:bar.querySelector('button').getBoundingClientRect().width,
          titleText:title.getAttribute('aria-label'),titleCanvas:title.querySelector('canvas')?.getBoundingClientRect().toJSON()};
      });report.observations.pauseLayouts.push(geometry);
      await page.screenshot({path:path.join(folder,'pause-'+width+'.png')});
    }
    await page.locator('#resume').click();
    report.observations.mapFocus=await page.evaluate(()=>{
      mapViews.setOpen(true);mapViews.options.firstElementChild.click();
      const el=mapViews.main,css=getComputedStyle(el);return {active:document.activeElement.id,focusVisible:el.matches(':focus-visible'),outline:css.outline,mode:mapViews.mode};
    });
    await page.evaluate(()=>mapViews.select('normal'));await page.waitForTimeout(300);
    await page.locator('#map-mode').tap();
    report.observations.mapTapFocus=await page.locator('#map-mode').evaluate(el=>({active:document.activeElement.id,focusVisible:el.matches(':focus-visible'),outline:getComputedStyle(el).outline}));
    await page.evaluate(()=>mapViews.close());
    // Inject only the presentation state to isolate a cold portrait from combat logic.
    await page.route('**/content/ui/commanders1_hd/**',async route=>{await delay(250);await route.continue();});
    await page.route('**/content/ui/commanders2_hd/**',async route=>{await delay(250);await route.continue();});
    report.observations.dialogue=await page.evaluate(async()=>{
      const portrait=Object.keys(manifest.atlases.commanders2_hd||manifest.atlases.commanders1_hd)[0];
      state={...state,dialogue:'将军，请下达命令。',dialoguePresentation:{name:'拿破仑',portrait,left:true}};refreshHud();
      const img=$('dialogue-portrait'),text=$('dialogue-text').firstElementChild;
      const first={portrait,imgComplete:img.complete,imgWidth:img.naturalWidth,textAttached:!!text,dialogueVisible:!$('dialogue').hidden};
      await img.decode();refreshHud();
      return {first,portraitNodeRetained:img===$('dialogue-portrait'),textNodeRetained:text===$('dialogue-text').firstElementChild};
    });
    await page.screenshot({path:path.join(folder,'dialogue.png')});
    await page.evaluate(()=>{state={...state,dialogue:null,dialoguePresentation:null};refreshHud();return menus.headquarters();});
    await page.evaluate(()=>{menus.recruiting=true;menus.rating=0;menus.drawRoster();});await page.waitForTimeout(300);
    report.observations.headquartersRefresh=await page.evaluate(async()=>{
      const ids=['hq-roster','hq-recruit-band','hq-refresh','hq-filters'],before=new Map(ids.map(id=>[id,{host:$(id),child:$(id).firstElementChild}]));
      const heading=document.querySelector('#hq-screen h1'),headingChild=heading.firstElementChild;
      await menus.transact({op:'refresh',group:menus.rating});
      return {parts:ids.map(id=>({id,hostRetained:before.get(id).host===$(id),firstChildRetained:before.get(id).child===$(id).firstElementChild})),headingHostRetained:heading===document.querySelector('#hq-screen h1'),headingTextRetained:headingChild===heading.firstElementChild};
    });
    await page.waitForTimeout(300);await page.screenshot({path:path.join(folder,'recruit.png')});
  }finally{await browser.close();}
  await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify(report,null,2));
})().catch(async error=>{report.errors.push(error.message);await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2)+'\n');console.error(error.message);process.exitCode=1;});
