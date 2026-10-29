'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,errors:[],views:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  try{
    for(const renderer of ['canvas','pixi']){
      const page=await browser.newPage({viewport:{width:1400,height:636}});
      page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));
      await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      const ownedIds=await page.evaluate(async()=>{
        let hq=await rpc('headquarters');
        while(hq.roster.length<2){
          const reward=hq.princesses.find(p=>p.unlocked&&!hq.roster.some(c=>c.definitionId===p.definitionId));
          if(!reward)throw Error('Missing claimable commander fixture');
          hq=await rpc('commander',{op:'claim',revision:hq.revision,offer:reward.id});
        }
        return hq.roster.slice(0,2).map(c=>c.id);
      });
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>!busy&&!stageIntroOpen);
      check(await page.evaluate(()=>!!ctx.gpu)===(renderer==='pixi'),'actual '+renderer+' renderer');
      check(await page.evaluate(()=>state.carriedCommanderIds.length===0&&state.units.some(u=>u.owner===state.playerFaction&&u.commanderId)),
        'scenario starts with native generals but no carried commanders');
      const openSummary=async()=>{
        await page.evaluate(()=>{
          closeDetail();const round=3;
          state={...state,round,activeFaction:state.playerFaction,factions:state.factions.map(f=>f.id===state.playerFaction?
            {...f,turnSummary:{round,income:{money:261,industry:48,food:195},foodConsumption:218}}:f)};
          turnUi.observe(state,true);turnUi.seen.clear();turnUi.observe(state);turnUi.tick();
        });
        await page.waitForTimeout(250);
        return page.evaluate(()=>[...document.querySelectorAll('.round-commander:not(.empty)')].map(c=>c.dataset.commanderId));
      };
      check((await openSummary()).length===0,'neither native nor undeployed account generals appear');
      await page.evaluate(async commander=>{
        closeDetail();const unit=state.units.find(u=>u.owner===state.playerFaction&&!u.commanderId&&state.cells[u.cell].owner===state.playerFaction);
        await act('deploy',{unit:unit.id,commander});
      },ownedIds[0]);
      check(await page.evaluate(id=>state.carriedCommanderIds.length===1&&state.carriedCommanderIds[0]===id,ownedIds[0]),'snapshot exposes actual deployment history');
      check(JSON.stringify(await openSummary())===JSON.stringify([ownedIds[0]]),'only commander carried from headquarters appears');
      check(await page.locator('.round-commander.empty').count()===5,'five empty slots retained');
      await page.evaluate(async()=>{closeDetail();await rpc('save',{slot:1,mode:2});});
      await page.evaluate(async()=>{await load(1,2);});
      await page.waitForFunction(()=>!busy&&!stageIntroOpen);
      check(await page.evaluate(id=>state.carriedCommanderIds.length===1&&state.carriedCommanderIds[0]===id,ownedIds[0]),'carried roster survives save and load');
      check(JSON.stringify(await openSummary())===JSON.stringify([ownedIds[0]]),'reloaded turn summary retains correct carried roster');
      await page.waitForFunction(()=>[...document.querySelectorAll('.round-commander:not(.empty) img')].every(i=>i.complete&&i.naturalWidth>0));
      await page.screenshot({path:path.join(out,renderer+'-roster.png')});
      await page.evaluate(()=>closeDetail());
      for(const [width,height] of [[1400,636],[1920,873],[844,390],[390,844],[320,568],[1024,768]]){
        await page.setViewportSize({width,height});
        for(const [round,value] of [[3,266],[15,9999],[20,10000],[30,30151],[31,99],[32,999999],[33,30151]]){
          const bounds=await page.evaluate(({round,value})=>{
            state={...state,round,factions:state.factions.map(f=>f.id===state.playerFaction?{...f,resources:{money:value,industry:value,food:value},unitCount:62,unitLimit:337}:f)};
            refreshHud();
            const bar=$('army-capacity').getBoundingClientRect(),resources=document.querySelector('.battle-resources').getBoundingClientRect(),menu=$('menu-button').getBoundingClientRect();
            return {top:bar.top,left:bar.left,right:bar.right,center:(bar.left+bar.right)/2,resourceRight:resources.right,menuLeft:menu.left,
              round:$('army-capacity-round').ew4HudText,count:$('army-capacity-value').ew4HudText,
              fontSize:$('army-capacity-round').ew4HudSize,countSize:$('army-capacity-value').ew4HudSize,
              fontHeight:$('army-capacity-round').firstElementChild.getBoundingClientRect().height,
              resourceFontHeight:resourceHud[0].value.firstElementChild.getBoundingClientRect().height};
          },{round,value});
          check(Math.abs(bounds.top)<.5,'top pinned '+renderer+'/'+width+'/'+round);
          check(Math.abs(bounds.center-width/2)<1,'bar centered');
          check(bounds.left>=bounds.resourceRight+7&&bounds.right<=bounds.menuLeft-7,'top row avoids overlap');
          check(bounds.round===String(round)&&bounds.count==='62/337','round and count update');
          check(bounds.fontSize===undefined&&bounds.countSize===undefined&&Math.abs(bounds.fontHeight-bounds.resourceFontHeight)<.1,'count and round use resource font size');
          report.views.push({renderer,width,height,round,value,...bounds});
        }
        await page.screenshot({path:path.join(out,renderer+'-hud-'+width+'.png')});
      }
      await page.close();
    }
    check(report.errors.length===0,'no script errors');
  }finally{await fs.writeFile(path.join(out,'results.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,views:report.views.length,errors:report.errors}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
