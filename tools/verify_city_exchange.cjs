'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module'),{execFileSync}=require('node:child_process');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56163',out=path.resolve(process.argv[3]||'build/r-city-exchange-tests-20261101');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],runs:[]},check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const version of ['before','source','after']){
      const page=await browser.newPage({viewport:{width:1280,height:720}});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(version==='before')await page.route(/\/(?:compat\/)?commander-workflows\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',body:execFileSync('git',['show','HEAD:rewrite/Ew4.Web/wwwroot/commander-workflows.js'],{encoding:'utf8'})}));
      if(version==='source')await page.route(/\/(?:compat\/)?(?:app|battlefield|commander-workflows)\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',path:path.join(__dirname,'../rewrite/Ew4.Web/wwwroot',new URL(route.request().url()).pathname.split('/').at(-1))}));
      await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:120000});
      await page.evaluate(async()=>{await start('conquest1','rus');stageIntroOpen=false;closeDetail();await selectCell(1799,true);await menus.exchange(1799);});
      const run=await page.evaluate(async version=>{
        const originalRpc=menus.api.rpc,originalRefresh=menus.api.refresh,originalNative=menus.native;
        const stats={version,requests:0,refreshes:0,rebuilds:0,times:[],controls:[]};
        menus.api.rpc=async(...args)=>{stats.requests++;return originalRpc(...args);};
        menus.api.refresh=async(...args)=>{stats.refreshes++;return originalRefresh(...args);};
        menus.native=function(...args){stats.rebuilds++;return originalNative.apply(this,args);};
        window.exchangeTest={stats,originalRpc,originalRefresh,originalNative};
        // Use the cheapest trades and alternate directions when funds allow.
        for(let i=0;i<12;i++){
          const l=$('detail-content').nativeLayout,first=l.get(i<4?['btn_sell_3','btn_buy_3','btn_buy_1','btn_sell_1'][i]:i%2?'btn_sell_1':'btn_buy_1');
          const button=first.disabled?[...l.nodes.values()].find(e=>/^btn_(buy|sell)_/.test(e.dataset.nativeId)&&!e.disabled):first;
          if(!button)break;
          stats.controls.push(button.dataset.nativeId);const time=performance.now();await button.onclick();stats.times.push(performance.now()-time);
        }
        return stats;
      },version);
      check(run.times.length===12,'twelve actual trades '+version);
      check(['btn_sell_3','btn_buy_3','btn_buy_1','btn_sell_1'].every(id=>run.controls.includes(id)),'both resources bought and sold '+version);
      if(version==='before'){check(run.refreshes===12&&run.rebuilds===12,'baseline reproduces full refresh and dialog recreation');}
      else{
        check(run.requests===12&&run.refreshes===0&&run.rebuilds===0,'one exchange request and stable dialog per trade');
        const resources=await page.evaluate(async()=>{
          const quote=await exchangeTest.originalRpc('exchange',{cell:1799});
          return {local:state.factions.find(f=>f.id===state.playerFaction).resources,remote:quote.resources,
            buttons:quote.offers.every((o,i)=>$('detail-content').nativeLayout.get('btn_buy_'+(i+1)).disabled===!o.canBuy&&$('detail-content').nativeLayout.get('btn_sell_'+(i+1)).disabled===!o.canSell)};
        });
        assert.deepEqual(resources.local,resources.remote);report.checks++;check(resources.buttons,'all eight button states match authoritative balances');
        await page.evaluate(()=>{
          exchangeTest.body=$('detail-content').nativeLayout.body;
          exchangeTest.quote=exchangeTest.body.querySelector('.exchange-quote');
          exchangeTest.delayed=0;menus.api.rpc=async(...args)=>{exchangeTest.delayed++;await new Promise(r=>setTimeout(r,100));return exchangeTest.originalRpc(...args);};
          const l=$('detail-content').nativeLayout,buttons=[...l.nodes.values()].filter(e=>/^btn_(buy|sell)_/.test(e.dataset.nativeId)&&!e.disabled);
          exchangeTest.pending=buttons[0].onclick();exchangeTest.allDisabled=buttons.every(e=>e.disabled);
          exchangeTest.duplicates=Promise.all(buttons.map(b=>b.onclick()));
        });
        check(await page.evaluate(()=>exchangeTest.allDisabled),'all trade buttons disabled while pending');
        await page.evaluate(async()=>{await exchangeTest.pending;await exchangeTest.duplicates;});
        check(await page.evaluate(()=>exchangeTest.delayed===1&&exchangeTest.body===$('detail-content').nativeLayout.body&&exchangeTest.quote===exchangeTest.body.querySelector('.exchange-quote')),'rapid cross-button clicks commit once and preserve nodes');
        await page.evaluate(async()=>{
          const notice=menus.api.notice;menus.api.notice=message=>{exchangeTest.failure=message;notice(message);};
          menus.api.rpc=async()=>{throw Error('exchange-test-failure');};
          const b=[...$('detail-content').nativeLayout.nodes.values()].find(e=>/^btn_(buy|sell)_/.test(e.dataset.nativeId)&&!e.disabled);
          await b.onclick();exchangeTest.recovered=!b.disabled;menus.api.rpc=exchangeTest.originalRpc;menus.api.notice=notice;
        });
        check(await page.evaluate(()=>exchangeTest.recovered&&exchangeTest.failure==='exchange-test-failure'),'failed request restores controls');
        await page.evaluate(()=>{$('notice').hidden=true;});
        for(const [name,width,height] of [['desktop',1280,720],['phone',844,390],['portrait',390,844]]){
          await page.setViewportSize({width,height});await page.waitForTimeout(150);
          check(await page.locator('.exchange-quote').evaluateAll(es=>es.length===8&&es.every(e=>{const r=e.getBoundingClientRect(),p=e.parentElement.getBoundingClientRect();return r.width>0&&r.left>=p.left-1&&r.right<=p.right+1&&r.top>=p.top-1&&r.bottom<=p.bottom+1;})),name+' quotes fit');
          await page.screenshot({path:path.join(out,'exchange-'+version+'-'+name+'.png')});
        }
        await page.evaluate(async()=>{closeDetail();await new Promise(r=>setTimeout(r,1000));});
        check(await page.evaluate(async()=>{const quote=await exchangeTest.originalRpc('exchange',{cell:1799});return quote.revision===state.revision;}),'closing synchronizes full revision');
        // The visual frame must cover VI and VII, with V still unframed.
        await page.setViewportSize({width:1280,height:720});
        const frames=await page.evaluate(async()=>{
          const cell=state.cells.findIndex(c=>c.active&&c.buildingKind==='city'),original=state.cells[cell];
          const result=[];for(const level of [5,6,7]){
            state.cells[cell]={...original,buildingLevel:level};view={...point(cell),zoom:2};clampView();
            battlefield.state=null;render(performance.now(),true);
            const calls=[],sprite=battlefield.api.sprite;
            battlefield.api.sprite=(...args)=>{calls.push(args);return sprite(...args);};
            try{battlefield.visibleCells=[{site:state.cells[cell],p:point(cell)}];battlefield.facilities(false);}finally{battlefield.api.sprite=sprite;}
            result.push({level,gold:calls.some(c=>c[1]==='buildinglevel_max.png')});
            await new Promise(requestAnimationFrame);
          }
          state.cells[cell]=original;return result;
        });
        assert.deepEqual(frames,[{level:5,gold:false},{level:6,gold:true},{level:7,gold:true}]);report.checks++;
        await page.evaluate(()=>{exchangeTest.cityCell=state.cells.findIndex((c,i)=>c.active&&c.buildingKind==='city'&&!state.units.some(u=>u.cell===i));exchangeTest.city=state.cells[exchangeTest.cityCell];});
        for(const level of [5,6,7]){
          await page.evaluate(level=>{const cell=exchangeTest.cityCell;state={...state,cells:state.cells.map((c,i)=>i===cell?{...exchangeTest.city,buildingLevel:level}:c)};view={...point(cell),zoom:2};clampView();battlefield.invalidateGround();render(performance.now(),true);},level);
          await page.screenshot({path:path.join(out,'city-'+version+'-'+level+'.png')});
        }
        await page.evaluate(()=>{state={...state,cells:state.cells.map((c,i)=>i===exchangeTest.cityCell?exchangeTest.city:c)};battlefield.invalidateGround();render(performance.now(),true);});
        await page.evaluate(async()=>{
          await menus.exchange(1799);const original=menus.api.rpc;exchangeTest.updates=0;
          const update=menus.api.updateResources;menus.api.updateResources=(...args)=>{exchangeTest.updates++;update(...args);};
          menus.api.rpc=async(...args)=>{await new Promise(r=>setTimeout(r,150));return original(...args);};
          const b=[...$('detail-content').nativeLayout.nodes.values()].find(e=>/^btn_(buy|sell)_/.test(e.dataset.nativeId)&&!e.disabled);
          exchangeTest.pending=b.onclick();closeDetail();await exchangeTest.pending;
          menus.api.rpc=original;menus.api.updateResources=update;await new Promise(r=>setTimeout(r,500));
        });
        check(await page.evaluate(()=>$('detail-modal').hidden&&exchangeTest.updates===0),'closing pending transaction does not resurrect dialog or patch stale HUD');
        check(await page.evaluate(async()=>{const quote=await exchangeTest.originalRpc('exchange',{cell:1799});return quote.revision===state.revision&&JSON.stringify(quote.resources)===JSON.stringify(state.factions.find(f=>f.id===state.playerFaction).resources);}),
          'close waits for in-flight trade before reconciling revision and balances');
      }
      const sorted=[...run.times].sort((a,b)=>a-b);run.medianMs=sorted[Math.floor(sorted.length/2)];run.maxMs=Math.max(...run.times);report.runs.push(run);
      await page.close();
    }
    check(!report.errors.length,'no script errors');check(!report.missing.length,'no missing assets');
  }finally{await fs.writeFile(path.join(out,'browser.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
