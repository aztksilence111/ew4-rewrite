'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:57946',home=path.resolve('颓垣饿鸠/art/airfield-v1');
(async()=>{
  const fixture=JSON.parse(await fs.readFile(path.join(home,'package.json'),'utf8')),out=path.join(home,'qa/browser');await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],views:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const [source,renderer,width,height] of [[true,'canvas',1280,720],[false,'pixi',1280,720],[false,'canvas',390,844],[false,'pixi',844,390]]){
      const label=[source?'source':'compat',renderer,width].join('-'),context=await browser.newContext({viewport:{width,height},hasTouch:width<1000,isMobile:width<1000}),page=await context.newPage();
      page.setDefaultTimeout(90000);
      page.on('pageerror',e=>report.errors.push(label+': '+e.message));page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(source)await page.route(base+'/',async route=>{const response=await route.fetch();await route.fulfill({response,body:(await response.text()).replace(/((?:src|href)=")compat\//g,'$1')});});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      await page.evaluate(async fixture=>{
        await start('conquest8','fra');stageIntroOpen=false;$('detail-modal').hidden=true;
        settings.speed=5;
        const p=point(fixture.center);view.x=p.x;view.y=p.y;view.zoom=1.6;clampView();
        for(const u of state.units.filter(u=>u.id.startsWith('tyej.exercise.')))await loadLibrary(visualProfile(u).library);
        await selectCell(fixture.center,true,true);requestRender();
      },fixture);
      check(await page.evaluate(()=>state.unitLayers),'air layers enabled '+label);
      check(await page.evaluate(()=>state.units.filter(u=>u.id.startsWith('tyej.exercise.')).length===9),'nine demo units '+label);
      check(await page.evaluate(()=>selected.id==='tyej.exercise.engineer'),'ground selection '+label);
      await page.evaluate(()=>openDefense());
      check(await page.locator('.defense-cards>button').count()===4,'first-turn engineer menu contains airport '+label);
      check(await page.locator('.defense-cards').evaluate(el=>el.scrollWidth<=el.clientWidth+1),'all four construction cards visible without horizontal scroll '+label);
      await page.locator('.defense-cards>button').last().click();
      check(await page.locator('#detail-confirm').isDisabled(),'spent reinforcement cannot build yet '+label);
      check((await page.locator('.unit-description').textContent()).includes('下一所属方回合可施工'),'disabled airport explains action requirement '+label);
      await page.screenshot({path:path.join(out,label+'-first-turn-menu.png')});
      await page.evaluate(()=>closeDetail());
      await page.evaluate(async cell=>selectCell(cell),fixture.center);
      check(await page.evaluate(()=>selected.id==='tyej.exercise.fighter'),'air selection cycle '+label);
      await page.evaluate(()=>openUnitInfo(selected));await page.evaluate(async()=>{await Promise.all(document.getAnimations().map(a=>a.finished.catch(()=>{})));});
      check(await page.locator('[data-native-id=text_desc]').evaluate(el=>(el.textContent+' '+[...el.querySelectorAll('[aria-label]')].map(n=>n.getAttribute('aria-label')).join(' ')).includes('空中')),'air property '+label);
      await page.evaluate(()=>closeDetail());
      const animation=await page.evaluate(fixture=>{
        renderDirty=true;render(performance.now(),true);
        const air=battlefield.visibleUnitMarkers.find(m=>m.unit.id==='tyej.exercise.fighter'),ground=battlefield.visibleUnitMarkers.find(m=>m.unit.id==='tyej.exercise.engineer');
        const unit=state.units.find(u=>u.id==='tyej.exercise.fighter'),profile=visualProfile(unit),lib=libraries.get(profile.library);
        const a=battlefield.motionPose(unit,profile,lib,1,113),b=battlefield.motionPose(unit,profile,lib,-1,457),moving=battlefield.motionPose(unit,profile,lib,1,457,true);
        const stopped=battlefield.motionPose(unit,profile,lib,1,501,false);
        const planes=state.units.filter(u=>u.id.startsWith('tyej.exercise.')&&u.layer==='air');
        const staticPlanes=planes.every(u=>{
          const p=visualProfile(u),l=libraries.get(p.library);
          for(const action of ['ready','undoready']){
            battlefield.unitMotions.set(u.id,{action,playing:true,started:0});
            for(const time of [0,113,457,2500]){
              const pose=battlefield.motionPose(u,p,l,1,time,false);
              if(pose.item?.name!=='ready'||pose.tick!==0||pose.native?.static!==true)return false;
            }
          }
          return !p.motions.die&&!p.motions.support;
        });
        const attack=effects.attackPlan(profile,lib,'infantry',1,1);
        return {dx:air?.p.x-ground?.p.x,dy:air?.p.y-ground?.p.y,a:a.tick,b:b.tick,move:moving.item?.name,moveTick:moving.tick,stopped:stopped.item?.name,staticPlanes,attack:attack.segments.map(s=>s.name),frames:lib.byName.get('ready')?.duration,libraries:fixture.units.every(record=>libraries.has(visualProfile(state.units.find(u=>u.id===record.id)).library))};
      },fixture);
      check(animation.dx===18&&animation.dy===-24,'both visible and offset '+label);
      check(animation.frames===24&&animation.a===0&&animation.b===0&&animation.staticPlanes,'all aircraft static during idle and selection '+label);
      check(animation.move==='move'&&animation.libraries,'movement action and all libraries '+label);
      check(animation.stopped==='ready'&&animation.moveTick>0&&animation.attack.join(',')==='attack','only movement and attack animate; stop returns idle '+label);
      await page.screenshot({path:path.join(out,label+'.png')});
      if(source){
        await page.evaluate(async()=>{await act('end');stageIntroOpen=false;});
        await page.waitForFunction(()=>!busy&&state.activeFaction===state.playerFaction&&state.units.find(u=>u.id==='tyej.exercise.engineer')?.acted===false);
        await page.evaluate(()=>{if(turnUi.open)closeDetail();turnUi.pending=null;});
        await page.evaluate(async cell=>{await selectCell(cell,true,true);openDefense();},fixture.center);
        check(await page.locator('.defense-cards>button').count()===4,'engineer original fort menu plus airport');
        await page.locator('.defense-cards>button').last().click();
        const explanation=await page.locator('.unit-description').textContent();
        check(explanation.includes('工兵修筑一次后即可离开'),'construction explanation '+explanation);
        await page.screenshot({path:path.join(out,'construction-menu.png')});
        await page.locator('#detail-confirm').click();
        await page.waitForFunction(cell=>state.cells[cell].buildingConstructionTurns===2,fixture.center);
        const built=await page.evaluate(cell=>({turns:state.cells[cell].buildingConstructionTurns,movement:state.units.find(u=>u.id==='tyej.exercise.engineer').remainingMovement,construction:state.units.find(u=>u.id==='tyej.exercise.engineer').constructionTurns}),fixture.center);
        check(built.turns===2&&built.movement>0&&!built.construction,'independent building timer keeps engineer mobile');
        await page.screenshot({path:path.join(out,'construction-active.png')});
        report.construction=built;
        const departure=await page.evaluate(async cell=>{
          await selectCell(cell,true,true,'tyej.exercise.engineer');
          const to=selection.range.find(c=>c!==cell&&!state.units.some(u=>u.cell===c&&u.layer!=='air'));
          if(to===undefined)throw Error('No engineer departure cell');
          await act('move',{cell:to});
          return {to,cell:state.units.find(u=>u.id==='tyej.exercise.engineer').cell,turns:state.cells[cell].buildingConstructionTurns};
        },fixture.center);
        check(departure.cell===departure.to&&departure.turns===2,'engineer leaves immediately and construction continues');
        await page.evaluate(async cell=>{
          await selectCell(cell,true,true,'tyej.exercise.fighter');
          const to=selection.range.find(c=>c!==cell&&!state.units.some(u=>u.cell===c&&u.layer==='air'));
          if(to===undefined)throw Error('No aircraft departure cell');
          await act('move',{cell:to});
        },fixture.center);
        for(const remaining of [1,0]){
          await page.evaluate(async()=>{if(turnUi.open)closeDetail();turnUi.pending=null;await act('end');stageIntroOpen=false;});
          await page.waitForFunction(()=>!busy&&state.activeFaction===state.playerFaction);
          await page.evaluate(()=>{if(turnUi.open)closeDetail();turnUi.pending=null;});
          check(await page.evaluate(cell=>state.cells[cell].buildingConstructionTurns,fixture.center)===remaining,'owner-turn construction countdown '+remaining);
        }
        await page.evaluate(async cell=>{await selectCell(cell,true,true);},fixture.center);
        const offers=await page.evaluate(()=>selection.offers.map(o=>o.id));
        check(offers.includes('tyej.recruit.recon_air')&&!offers.includes('tyej.recruit.fighter'),'level one airport offers');
        await page.evaluate(async cell=>{await act('recruit',{cell,offer:'tyej.recruit.recon_air'});},fixture.center);
        check(await page.evaluate(cell=>state.cells[cell].buildingRecruitsThisTurn===1&&state.units.some(u=>u.cell===cell&&u.profile==='tyej.unit.recon_air'),fixture.center),'real airport recruitment');
        await page.screenshot({path:path.join(out,'airport-complete.png')});
        report.departure=departure;report.recruitment=offers;
      }
      report.views.push({label,animation});await context.close();
    }
    check(report.errors.length===0&&report.missing.length===0,'no script errors or missing assets');
  }finally{await fs.writeFile(path.join(home,'browser-verification.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e);process.exitCode=1;});
