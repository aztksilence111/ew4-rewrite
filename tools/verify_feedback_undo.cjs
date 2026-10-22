'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5393',folder=path.resolve(process.argv[3]||'build/feedback-undo-fix-20261022/browser');
const report={checks:0,errors:[],missing:[],screenshots:[],fixtures:'Dedicated profile only; rich briefing view fixture complements the actual configuration CLI test.'};
const check=(value,label)=>{assert.ok(value,label);report.checks++;};
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1136,height:640}});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  const ready=()=>page.waitForFunction(()=>startupReady,null,{timeout:60000});
  const shot=async name=>{await page.screenshot({path:path.join(folder,name+'.png')});report.screenshots.push(name);};
  try{
    await page.goto(base);await ready();
    await page.evaluate(()=>fetch('/api/mods?q='+encodeURIComponent(JSON.stringify({op:'activate',key:null})),{method:'POST'}));
    await page.reload();await ready();
    report.save=await page.evaluate(async()=>{
      await start('campaign1_01',null);await rpc('save',{slot:1,mode:1});
      const slots=await rpc('saves',{mode:1}),player=state.factions.find(f=>f.id===state.playerFaction);
      await menus.saveMenu(false,1);await Promise.all([...document.querySelectorAll('.save-country')].map(i=>i.decode()));
      return {country:player.country,slots,flags:[...document.querySelectorAll('.save-country')].map(i=>({src:i.src,loaded:i.naturalWidth>0}))};
    });
    check(report.save.slots.filter(s=>s.exists).every(s=>s.country===report.save.country),'campaign autosave and manual slot expose actual country');
    check(report.save.flags.length===report.save.slots.filter(s=>s.exists).length&&report.save.flags.every(f=>f.loaded),'every existing campaign slot renders its flag');await shot('campaign-save-flags');
    await page.evaluate(()=>load(1,1));
    check(await page.evaluate(()=>state.startCountry===null&&state.factions.find(f=>f.id===state.playerFaction).country==='fra'),'load preserves original campaign selection and actual player');
    report.rich=await page.evaluate(async()=>{
      menus.stageIntro({...state.stageIntro,commander:'Napoleon',presentation:{...state.stageIntro.presentation,content:[{type:'commander',commander:'Napoleon'}]}},()=>{});
      const portraits=[...document.querySelectorAll('.stage-intro img')].filter(i=>i.src.includes('Napoleon.png'));await Promise.all(portraits.map(i=>i.decode()));
      return portraits.map(i=>({src:i.src,loaded:i.naturalWidth>0}));
    });
    check(report.rich.length===2&&report.rich.every(i=>i.loaded&&i.src.includes('/commanders1_hd/')),'top-level and rich-block commanders use the same portrait atlas');await shot('rich-commander');
    report.undo=await page.evaluate(async()=>{
      await showMain();await start('conquest1','fra');settings.speed=5;
      for(const unit of state.units.filter(u=>u.owner===state.playerFaction)){
        await selectCell(unit.cell,true);const dest=selection.range.find(c=>c!==unit.cell&&!state.units.some(u=>u.cell===c)&&!state.cells[c].objectiveMarker);
        if(dest==null)continue;await act('move',{unit:unit.id,cell:dest});if(!selection?.undo)continue;
        const movedEnabled=!$('undo-move').disabled;
        const other=state.units.find(u=>u.owner===state.playerFaction&&u.id!==unit.id);await selectCell(other.cell,true);
        const otherDisabled=$('undo-move').disabled;
        const empty=state.cells.findIndex((c,i)=>c.active&&!state.units.some(u=>u.cell===i));await selectCell(empty,true);
        const emptyDisabled=$('undo-move').disabled,revision=state.revision;await $('undo-move').onclick();
        const noFallback=state.revision===revision&&state.units.find(u=>u.id===unit.id).cell===dest;
        await selectCell(dest,true);const reselectedEnabled=!$('undo-move').disabled;await $('undo-move').onclick();
        return {movedEnabled,otherDisabled,emptyDisabled,noFallback,reselectedEnabled,returned:state.units.find(u=>u.id===unit.id).cell===unit.cell};
      }throw Error('No reversible move fixture');
    });
    for(const [key,value] of Object.entries(report.undo))check(value,'selected-unit undo: '+key);
    report.notices=[];
    for(const [name,width,height] of [['desktop',1136,640],['landscape',844,390],['portrait',390,844]]){
      await page.setViewportSize({width,height});await page.evaluate(()=>notice('已保存'));await page.waitForTimeout(650);
      const saved=await page.evaluate(()=>{const el=$('notice'),c=el.querySelector('canvas'),r=el.getBoundingClientRect();return {text:el.querySelector('.original-text').getAttribute('aria-label'),canvasHeight:c.clientHeight,x:r.x,right:r.right,width:r.width};});
      check(saved.text==='已保存'&&saved.canvasHeight<25,name+' saved toast remains one bitmap line');
      check(saved.x>=0&&saved.right<=width,name+' saved toast stays inside viewport');await shot(name+'-saved');
      await page.evaluate(()=>notice('培训所需金币或粮食不足，请选择其他部队并检查当前资源。'.repeat(5)));await page.waitForTimeout(650);
      const long=await page.evaluate(()=>{const r=$('notice').getBoundingClientRect();return {x:r.x,right:r.right,height:r.height};});
      check(long.x>=0&&long.right<=width&&long.height<height,name+' long errors wrap within viewport');report.notices.push({name,saved,long});await shot(name+'-long-error');
    }
    check(report.errors.length===0&&report.missing.length===0,'no browser errors or missing resources');
  }catch(e){report.failure=e.message;process.exitCode=1;}
  finally{await browser.close();await fs.writeFile(path.join(folder,'browser.json'),JSON.stringify(report,null,2));}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,screenshots:report.screenshots.length,failure:report.failure}));
})();
