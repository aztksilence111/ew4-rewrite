'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]),baseline=process.argv.includes('--baseline');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={views:[],turns:[],errors:[],checks:0};
  const check=(v,m)=>{assert.ok(v,m);report.checks++;};
  try{
    const page=await browser.newPage({viewport:{width:913,height:540}});
    page.on('pageerror',e=>report.errors.push(e.message));
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await page.evaluate(()=>start('conquest1','fra'));await page.waitForFunction(()=>!busy&&!stageIntroOpen);
    for(const viewport of [{width:913,height:540},{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(viewport);
      await page.evaluate(()=>{
        closeDetail();turnUi.observe(state,true);
        const round=3,summary={round,income:{money:166,industry:38,food:195},foodConsumption:226};
        state={...state,round,activeFaction:state.playerFaction,factions:state.factions.map(f=>f.id===state.playerFaction?{...f,turnSummary:summary}:f)};
        turnUi.seen.clear();turnUi.observe(state);turnUi.tick();
      });
      await page.waitForTimeout(400);
      const geometry=await page.evaluate(()=>{
        const rect=el=>el.getBoundingClientRect().toJSON(),layout=$('detail-content').nativeLayout;
        const dialog=rect($('detail-modal').firstElementChild),body=rect(layout.body),title=rect(layout.get('text_round')),number=rect(layout.get('text_roundnum'));
        const glyph=rect(layout.get('text_roundnum').firstElementChild),scale=body.width/320;
        return {viewport:[innerWidth,innerHeight],dialog,body,title,number,glyph,scale,empty:document.querySelectorAll('.round-commander.empty').length,
          nameplates:document.querySelectorAll('.round-commander-name').length,expectedCenter:body.x+160*scale,
          commanderCount:(state.carriedCommanderIds||[]).length};
      });
      check(geometry.dialog.left>=0&&geometry.dialog.right<=viewport.width+1,'dialog fits');
      if(!baseline){
        check(Math.abs(geometry.dialog.x+geometry.dialog.width/2-viewport.width/2)<1,'dialog centered horizontally');
        check(Math.abs(geometry.dialog.y+geometry.dialog.height/2-viewport.height/2)<1,'dialog centered vertically');
        check(Math.abs(geometry.title.x+geometry.title.width/2-geometry.expectedCenter)<geometry.scale,'round title centered');
        check(Math.abs(geometry.number.x+geometry.number.width/2-geometry.expectedCenter)<geometry.scale,'round number centered');
        check(geometry.empty===Math.max(0,6-geometry.commanderCount)&&geometry.nameplates===Math.max(6,geometry.commanderCount),'battle commanders and empty slots retain nameboards');
      }
      report.views.push(geometry);await page.screenshot({path:path.join(out,'round-'+viewport.width+'.png')});
    }
    await page.setViewportSize({width:1280,height:720});
    for(let i=0;i<2;i++){
      await page.evaluate(async()=>{closeDetail();await start('conquest1','fra');settings.autoTurnFast=true;});
      await page.waitForFunction(()=>!busy&&!stageIntroOpen);
      const result=await page.evaluate(async()=>{
        const original=rpc,calls=[];rpc=async function(route,args){const begin=performance.now();const result=await original(route,args);
          if(route==='action')calls.push({op:args.op,ms:performance.now()-begin,bytes:JSON.stringify(result).length,steps:result.steps,progressOnly:result.progressOnly});return result;};
        const begin=performance.now();try{await act('end');}finally{rpc=original;}
        return {ms:performance.now()-begin,calls,round:state.round,active:state.activeFaction,player:state.playerFaction,record:performanceLog.records.at(-1)};
      });
      check(result.active===result.player&&result.round===2,'complete enemy turn returns to player');report.turns.push(result);
    }
    check(report.errors.length===0,'no script errors');
  }finally{await fs.writeFile(path.join(out,'results.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,turnMs:report.turns.map(t=>t.ms),bytes:report.turns.map(t=>t.calls.reduce((n,c)=>n+c.bytes,0)),errors:report.errors}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
