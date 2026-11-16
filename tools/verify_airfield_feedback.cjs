'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
(async()=>{
  const base=process.argv[2]||'http://127.0.0.1:57947',source=process.argv[3]==='source',home=path.resolve('颓垣饿鸠/art/airfield-v1');
  const fixture=JSON.parse(await fs.readFile(path.join(home,'package.json'),'utf8')),report={source,errors:[],missing:[],views:[]};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const [renderer,width,height] of [['pixi',1280,720],['canvas',1280,720],['pixi',390,844],['canvas',844,390]]){
      const context=await browser.newContext({viewport:{width,height},hasTouch:width<1000,recordVideo:!source&&width===1280&&renderer==='pixi'?{dir:path.join(home,'feedback-qa/video'),size:{width,height}}:undefined});
      const page=await context.newPage();page.setDefaultTimeout(90000);
      page.on('pageerror',e=>report.errors.push(renderer+'/'+width+': '+e.message));
      page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(source)await page.route(base+'/**',async route=>{if(route.request().resourceType()!=='document')return route.continue();const response=await route.fetch();await route.fulfill({response,body:(await response.text()).replace(/((?:src|href)=")compat\//g,'$1')});});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady&&!busy);
      const result={renderer,width,height};
      const focus=async(cell,zoom)=>page.evaluate(({cell,zoom})=>{const p=point(cell);view.x=p.x;view.y=p.y;view.zoom=zoom??Math.min(1.6,innerWidth/400,innerHeight/400);clampView();requestRender();},{cell,zoom});
      const click=async(cell,kind='cell',id)=>{
        const p=await page.evaluate(({cell,kind,id})=>{
          const u=id?state.units.find(u=>u.id===id):selected;
          const p=kind==='move'?Ew4Battlefield.movementPoint(state,u,cell):kind==='unit'?Ew4Battlefield.interactionPoint(state,u):point(cell),r=canvas.getBoundingClientRect();
          return {x:r.left+(p.x-view.x)*view.zoom+viewport.w/2,y:r.top+(p.y-view.y)*view.zoom+viewport.h/2};
        },{cell,kind,id});
        assert.ok(p.x>=0&&p.x<=width&&p.y>=55&&p.y<=height-55,'click visible '+JSON.stringify({cell,kind,p,width,height}));
        await page.mouse.click(p.x,p.y);await page.waitForTimeout(220);await page.waitForFunction(()=>!busy);
      };
      const shot=async name=>{if(!source&&renderer==='pixi'&&width===1280)await page.screenshot({path:path.join(home,'demo','feedback-'+name+'.png')});};
      const enter=async()=>{
        await page.evaluate(async()=>{await start('conquest8','fra');stageIntroOpen=false;$('detail-modal').hidden=true;settings.speed=1;for(const u of state.units.filter(u=>u.id.startsWith('tyej.')))await loadLibrary(visualProfile(u).library);requestRender();});
        await focus(fixture.center);
      };
      await enter();
      for(const expected of ['tyej.exercise.engineer','tyej.exercise.fighter',undefined]){
        await click(fixture.center);assert.equal(await page.evaluate(()=>selected?.id),expected);
      }
      await click(fixture.center,'unit','tyej.exercise.fighter');assert.equal(await page.evaluate(()=>selected?.id),'tyej.exercise.fighter');
      await click(fixture.center,'unit','tyej.exercise.fighter');assert.equal(await page.evaluate(()=>selection),undefined);
      const bare=await page.evaluate(center=>{const p=point(center);return state.cells.map((c,i)=>({c,i,p:point(i)})).find(o=>o.c.active&&!state.units.some(u=>u.cell===o.i)&&Math.hypot(o.p.x-p.x,o.p.y-p.y)<100)?.i;},fixture.center);
      assert.ok(Number.isInteger(bare));await click(bare);assert.equal(await page.evaluate(()=>selection.cell),bare);await click(bare);assert.equal(await page.evaluate(()=>selection),undefined);
      const engineerCell=fixture.units.find(u=>u.id==='tyej.exercise.engineer.second').cell;
      await click(engineerCell);assert.equal(await page.evaluate(()=>selected?.id),'tyej.exercise.engineer.second');
      await page.waitForTimeout(1200);
      result.ready=await page.evaluate(()=>{const p=battlefield.unitMotions.get(selected.id);return {action:p?.action,playing:p?.playing};});
      assert.deepEqual(result.ready,{action:'ready',playing:false});await click(engineerCell);assert.equal(await page.evaluate(()=>selection),undefined);
      result.selection='ground→air→none; direct air→none; single unit→none; land→none';
      // Actual renderer calls must use the same positions as pointer hit testing.
      await click(fixture.center,'unit','tyej.exercise.fighter');
      result.markers=await page.evaluate(()=>{
        const calls=[],sprite=battlefield.api.sprite;battlefield.api.sprite=(...args)=>{if(['move.png','attack.png'].includes(args[1]))calls.push(args.slice(1,4));return sprite(...args);};
        try{render(performance.now(),true);}finally{battlefield.api.sprite=sprite;}
        const expected=selection.range.filter(c=>c!==selection.cell).map(c=>Ew4Battlefield.movementPoint(state,selected,c));
        return {count:calls.filter(c=>c[0]==='move.png').length,aligned:expected.every(p=>calls.some(c=>c[0]==='move.png'&&c[1]===p.x&&c[2]===p.y)),budget:selected.remainingMovement};
      });assert.ok(result.markers.aligned&&result.markers.count>0);assert.equal(result.markers.budget,12);
      await shot('range');
      const move=await page.evaluate(()=>{
        const u=selected,t=state.units.find(u=>u.id==='tyej.target.air'),row=c=>Math.floor(c/state.width),q=c=>c%state.width-(row(c)-row(c)%2)/2;
        const d=(a,b)=>Math.max(Math.abs(q(a)-q(b)),Math.abs(row(a)-row(b)),Math.abs(q(a)-q(b)+row(a)-row(b)));
        if(selection.range.some(c=>d(c,u.cell)>4))throw Error('Aircraft range exceeds budget');
        const to=selection.range.find(c=>d(c,u.cell)===3&&d(c,t.cell)===1);
        if(to===undefined)throw Error('Missing three-step demonstration route');return {to,target:t.cell,before:t.health};
      });
      await focus(move.to);await click(move.to,'move');
      assert.equal(await page.evaluate(()=>selected.remainingMovement),0);assert.equal(await page.evaluate(()=>selected.cell),move.to);
      result.airTarget=await page.evaluate(()=>{
        const calls=[],sprite=battlefield.api.sprite;battlefield.api.sprite=(...args)=>{if(args[1]==='attack.png')calls.push(args.slice(2,4));return sprite(...args);};
        try{render(performance.now(),true);}finally{battlefield.api.sprite=sprite;}
        const u=state.units.find(u=>u.id==='tyej.target.air'),p=Ew4Battlefield.interactionPoint(state,u);
        return {aligned:calls.some(c=>c[0]===p.x&&c[1]===p.y),x:p.x,y:p.y};
      });assert.ok(result.airTarget.aligned);await shot('air-target');
      await click(move.target,'unit','tyej.target.air');await page.waitForTimeout(1000);
      result.fighter=await page.evaluate(before=>({acted:state.units.find(u=>u.id==='tyej.exercise.fighter').acted,before,after:state.units.find(u=>u.id==='tyej.target.air')?.health??0}),move.before);
      assert.ok(result.fighter.acted&&result.fighter.after<result.fighter.before);await shot('air-attack');
      // An unarmed aircraft exhausts movement without gaining an extra first step.
      const transport=fixture.units.find(u=>u.role==='transport_air');await focus(transport.cell);await click(transport.cell,'unit',transport.id);
      const end=await page.evaluate(()=>{const p=point(selected.cell);return selection.range.filter(c=>c>selected.cell&&Math.hypot(point(c).x-p.x,point(c).y-p.y)>120).sort((a,b)=>b-a).find(c=>!state.units.some(u=>u.cell===c&&u.layer==='air'));});
      assert.ok(end!==undefined);await focus(end);await click(end,'move');
      // Consume any remaining whole step through the same visible marker.
      for(let i=0;i<3&&await page.evaluate(()=>selected.remainingMovement>=3);i++){
        const c=await page.evaluate(()=>selection.range.find(c=>c!==selected.cell));assert.ok(c!==undefined);await focus(c);await click(c,'move');
      }
      result.exhausted=await page.evaluate(()=>({remaining:selected.remainingMovement,range:selection.range}));
      assert.ok(result.exhausted.remaining<3&&result.exhausted.range.every(c=>c===selected.cell));
      // Bomber must attack from exactly two hexes.
      const bomber=fixture.units.find(u=>u.role==='bomber');await focus(bomber.cell);await click(bomber.cell,'unit',bomber.id);
      const bombing=await page.evaluate(()=>{
        const t=state.units.find(u=>u.id==='tyej.target.ground'),row=c=>Math.floor(c/state.width),q=c=>c%state.width-(row(c)-row(c)%2)/2;
        const d=(a,b)=>Math.max(Math.abs(q(a)-q(b)),Math.abs(row(a)-row(b)),Math.abs(q(a)-q(b)+row(a)-row(b)));
        const to=selection.range.find(c=>c!==selected.cell&&d(c,t.cell)===2);if(to===undefined)throw Error('Missing two-hex bombing position');
        return {to,target:t.cell,before:t.health,range:manifest.profiles[selected.profile].stats.maxatkrange};
      });assert.equal(Number(bombing.range),2);await focus(bombing.to);await click(bombing.to,'move');
      assert.ok(await page.evaluate(()=>selection.targetUnits.includes('tyej.target.ground')));await shot('bomber-range-two');
      await click(bombing.target,'unit','tyej.target.ground');await page.waitForTimeout(1000);
      result.bomber=await page.evaluate(before=>({before,after:state.units.find(u=>u.id==='tyej.target.ground')?.health??0}),bombing.before);assert.ok(result.bomber.after<result.bomber.before);
      // Start a clean scenario for independent airport timing and upgrades.
      await enter();await click(engineerCell);await page.evaluate(()=>openDefense());await page.locator('.defense-cards>button').last().click();await page.locator('#detail-confirm').click();await page.waitForFunction(()=>!busy);
      assert.equal(await page.evaluate(c=>state.cells[c].buildingConstructionTurns,engineerCell),2);
      await shot('airport-no-construction-label');
      const turn=async()=>{await page.evaluate(async()=>{if(turnUi.open)closeDetail();turnUi.pending=null;await act('end');stageIntroOpen=false;});await page.waitForFunction(()=>!busy&&state.activeFaction===state.playerFaction);await page.evaluate(()=>{if(turnUi.open)closeDetail();turnUi.pending=null;});};
      await turn();await turn();assert.equal(await page.evaluate(c=>state.cells[c].buildingConstructionTurns,engineerCell),0);
      await focus(engineerCell);await page.evaluate(async c=>selectCell(c,true,true,'tyej.exercise.engineer.second'),engineerCell);
      result.recruitIcon=await page.locator('#actions button[aria-label="招募空军"] img').getAttribute('src');assert.ok(result.recruitIcon.endsWith('/button_city.png'));
      for(const level of [2,3]){
        await page.evaluate(async c=>{await act('upgrade',{cell:c});},engineerCell);await page.waitForFunction(()=>!busy);
        const timing=await page.evaluate(c=>({level:state.cells[c].buildingLevel,turns:state.cells[c].buildingConstructionTurns}),engineerCell);
        assert.deepEqual(timing,{level,turns:0});if(level===2)await turn();await page.evaluate(async c=>selectCell(c,true,true,'tyej.exercise.engineer.second'),engineerCell);
      }
      await page.locator('#actions button[aria-label="招募空军"]').click();await page.waitForTimeout(400);await shot('airport-recruitment');
      result.upgrades='level 2 and 3 complete immediately';await page.evaluate(()=>closeDetail());
      // Actual Mod commander portrait, using the 156px atlas and circular cache.
      // Deploy a real Mod commander before checking the map portrait. C15 has
      // no preassigned commander, so merely starting the scenario cannot test
      // the portrait renderer.
      await page.evaluate(async()=>{
        const h=await rpc('headquarters');
        if(!h.roster.length){
          const offer=h.offers[0]?.[0];
          if(!offer)throw Error('No medal commander offer available');
          await rpc('commander',{op:'recruit',revision:h.revision,commander:offer});
        }
        const fresh=await rpc('headquarters'),held=fresh.roster[0];
        await start('conquest7','c15yn');
        stageIntroOpen=false;$('detail-modal').hidden=true;
        const unit=state.units.find(u=>u.owner===state.playerFaction&&!u.commanderId);
        await act('deploy',{unit:unit.id,commander:held.id});
      });
      const hero=await page.evaluate(()=>state.units.find(u=>u.commanderImage&&manifest.atlases.commanders2_hd?.[u.commanderImage+'.png']&&Number(manifest.atlases.commanders2_hd[u.commanderImage+'.png'].w)===156));assert.ok(hero,'Mod commander portrait exists');
      await focus(hero.cell,Math.min(2.7,width/230,height/250));
      await page.waitForFunction(name=>battlefield.portraits?.has(name+'.png@hd'),hero.commanderImage);await page.waitForTimeout(250);await shot('commander-hd-round');
      result.portrait=await page.evaluate(name=>{const c=battlefield.portraits.get(name+'.png@hd'),d=c.getContext('2d').getImageData(0,0,c.width,c.height).data;return {name,width:c.width,height:c.height,cornerAlpha:d[3],centerAlpha:d[((c.height/2)*c.width+c.width/2)*4+3]};},hero.commanderImage);
      assert.equal(result.portrait.width,144);assert.equal(result.portrait.cornerAlpha,0);assert.ok(result.portrait.centerAlpha>0);
      report.views.push(result);const video=page.video();await context.close();if(video)report.video=await video.path();
    }
    assert.deepEqual(report.errors,[]);assert.deepEqual(report.missing,[]);
  }finally{await fs.writeFile(path.join(home,source?'feedback-preview-source.json':'feedback-preview.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e);process.exitCode=1;});
