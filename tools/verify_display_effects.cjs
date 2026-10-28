'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]);
const report={checks:0,errors:[],missing:[],views:[],endCommands:0};
const check=(value,label)=>{assert.ok(value,label);report.checks++;};
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720}});
    page.on('pageerror',e=>report.errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400)report.missing.push(r.status()+' '+r.url());});
    page.on('request',r=>{if(r.url().includes('/api/action?')&&JSON.parse(new URL(r.url()).searchParams.get('q')).op==='end')report.endCommands++;});
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    for(const viewport of [{width:2376,height:1080},{width:1280,height:720},{width:844,height:390}]){
      await page.setViewportSize(viewport);
      await page.evaluate(()=>{mode=1;return menus.openChapter(catalog.chapters[5]);});
      await page.waitForTimeout(650);
      check(await page.locator('#campaign-title .original-text').getAttribute('aria-label')==='选择战役','campaign title');
      check(await page.evaluate(()=>menus.year.getAttribute('aria-label')==='1775'),'British opening year');
      const geometry=await page.evaluate(()=>{
        const row=$('campaign-battles').firstElementChild,other=row.nextElementSibling,list=$('campaign-battles'),plate=document.querySelector('#campaign-screen>.selection-backplate');
        const a=row.getBoundingClientRect(),b=other.getBoundingClientRect(),p=plate.getBoundingClientRect(),screen=$('campaign-screen').getBoundingClientRect(),confirm=$('campaign-confirm').getBoundingClientRect();
        const draw=$('campaign-map').getContext('2d'),data=draw.getImageData(0,0,$('campaign-map').width,$('campaign-map').height).data;
        return {same:row.offsetWidth===other.offsetWidth&&row.offsetHeight===other.offsetHeight,shift:b.left-a.left,
          right:Math.abs(a.right-innerWidth),bottom:Math.abs(p.bottom-screen.bottom),tip:p.left>a.left&&p.left<b.right,
          behind:p.left<=confirm.left&&p.bottom>=confirm.bottom,background:getComputedStyle(list).backgroundImage,
          pixels:data.some((v,i)=>i%4===3&&v>0),plate:p.toJSON()};
      });
      check(geometry.same&&geometry.shift>5&&geometry.right<1,'fixed-size selected tab shifts and covers edge');
      check(geometry.bottom<1&&geometry.tip&&geometry.behind&&geometry.background==='none','independent full-height backplate');
      check(geometry.pixels,'nonblank strategic canvas');
      await page.locator('#campaign-battles').evaluate(el=>{el.scrollTop=el.scrollHeight;});
      check(await page.evaluate(plate=>Math.abs(document.querySelector('#campaign-screen>.selection-backplate').getBoundingClientRect().top-plate.top)<1,geometry.plate),'scroll does not move plate');
      await page.locator('#campaign-battles').evaluate(el=>{el.scrollTop=0;});
      const samples=await page.evaluate(async()=>{
        const rows=$('campaign-battles').children,row=rows[1],before=row.getBoundingClientRect().left,w=row.offsetWidth,h=row.offsetHeight;
        row.click();await new Promise(r=>setTimeout(r,65));
        const during=row.getBoundingClientRect().left,mid={...menus.camera.value},target={...menus.camera.target};
        await new Promise(r=>setTimeout(r,230));
        return {before,during,after:row.getBoundingClientRect().left,w,h,afterW:row.offsetWidth,afterH:row.offsetHeight,
          pan:Math.abs(mid.x-target.x)+Math.abs(mid.y-target.y),title:menus.battle.id};
      });
      check(samples.before>samples.during&&samples.during>samples.after&&samples.w===samples.afterW&&samples.h===samples.afterH,'sampled tab motion retains dimensions '+JSON.stringify(samples));
      check(samples.pan>0,'camera is moving during the switch');
      await page.evaluate(()=>{const rows=$('campaign-battles').children;rows[0].click();rows[2].click();rows[1].click();});
      await page.waitForTimeout(260);
      check(await page.evaluate(()=>menus.battle.id===$('campaign-battles').children[1].dataset.campaign&&menus.camera.key===menus.battle.id&&menus.year.getAttribute('aria-label')===manifest.strategic[menus.battle.id].age),'rapid clicks settle on final selection');
      await page.screenshot({path:path.join(out,'campaign-'+viewport.width+'.png')});
      report.views.push({viewport,geometry,samples});
      await page.evaluate(()=>menus.transitionPage(()=>{$('campaign-screen').hidden=true;}));
      await page.evaluate(()=>chooseScenario(catalog.scenarios.find(s=>s.id==='conquest1')));
      await page.waitForTimeout(650);
      check(await page.evaluate(()=>{const p=document.querySelector('#countries>.selection-backplate').getBoundingClientRect(),b=$('confirm-country').getBoundingClientRect();return p.bottom>=b.bottom&&p.left<=b.left;}),'conquest backplate covers confirmation');
      await page.evaluate(()=>menus.transitionPage(()=>{$('countries').hidden=true;}));
    }
    await page.setViewportSize({width:1280,height:720});
    await page.evaluate(()=>start('conquest1','fra'));
    await page.waitForFunction(()=>!busy&&!stageIntroOpen);
    const revision=await page.evaluate(()=>state.revision);
    await page.locator('#end-turn').click();
    await page.locator('[data-layout="form_messagebox"]').waitFor();
    await page.locator('[data-native-id="btn_cancel"]').click();
    check(await page.evaluate(revision=>state.revision===revision&&$('detail-modal').hidden,revision),'cancel keeps turn unchanged');
    await page.locator('#end-turn').click();
    await page.locator('[data-native-id="btn_confirm"]').evaluate(el=>{el.click();el.click();});
    await page.waitForFunction(()=>!busy&&!aiRunning&&!aiTask,null,{timeout:180000});
    await page.locator('[data-layout="form_roundturn"]').waitFor({timeout:15000});
    check(report.endCommands===1,'rapid confirmation submits exactly one end command');
    check(await page.locator('.round-commander').count()===6,'six empty commander slots');
    const summary=await page.evaluate(()=>state.factions.find(f=>f.id===state.playerFaction).turnSummary);
    check(summary.round===await page.evaluate(()=>state.round),'summary belongs to arrived player turn');
    for(const [id,value] of Object.entries({text_money:summary.income.money,text_indus:summary.income.industry,text_addfood:summary.income.food,text_delfood:summary.foodConsumption}))
      check(await page.locator('[data-native-id="'+id+'"]>.original-text').getAttribute('aria-label')===String(value),'bound summary '+id);
    await page.screenshot({path:path.join(out,'round.png')});
    for(const viewport of [{width:2376,height:1080},{width:844,height:390}]){
      await page.setViewportSize(viewport);
      await page.waitForTimeout(200);
      const bounds=await page.locator('#detail-modal>.dialog').evaluate(el=>{const r=el.getBoundingClientRect();return {...r.toJSON(),viewport:[innerWidth,innerHeight]};});
      check(bounds.left>=0&&bounds.top>=0&&bounds.right<=viewport.width&&bounds.bottom<=viewport.height,'turn window fits '+JSON.stringify(bounds));
    }
    await page.locator('#close-detail').click();
    check(await page.evaluate(()=>!turnUi.open),'close releases turn');
    await page.evaluate(()=>installState({...state}));await page.waitForTimeout(150);
    check(await page.locator('#detail-modal').isHidden(),'same turn does not repeat summary');
    await page.evaluate(()=>rpc('save',{slot:4,mode:2}));
    await page.evaluate(()=>load(4,2));await page.waitForTimeout(250);
    check(await page.locator('#detail-modal').isHidden(),'load does not replay settled turn');
    check(await page.evaluate(()=>{
      const snapshot={...state,round:state.round+1,playerAi:{enabled:true,autoBattle:true},
        factions:state.factions.map(f=>f.id===state.playerFaction?{...f,turnSummary:{...f.turnSummary,round:state.round+1}}:f)};
      return !aiCanContinue(snapshot);
    }),'automatic player AI yields at new player-turn summary');
    await page.emulateMedia({reducedMotion:'reduce'});
    await page.evaluate(async()=>{
      $('play').hidden=true;$('menu').hidden=false;await menus.openChapter(catalog.chapters[0]);
      $('campaign-battles').children[1].click();
    });
    check(await page.evaluate(()=>$('campaign-intro').getAnimations().length===0&&!document.querySelector('.menu-transition-curtain')),'reduced motion completes without lingering transitions');
    check(report.errors.length===0,'no browser errors '+JSON.stringify(report.errors));
    check(report.missing.length===0,'no missing content '+JSON.stringify(report.missing));
    await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify({checks:report.checks,errors:report.errors,missing:report.missing,views:report.views.length}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
