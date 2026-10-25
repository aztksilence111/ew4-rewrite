'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],folder=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(folder,{recursive:true});const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const report={cases:[],errors:[]};
  try{
    for(const mode of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width:844,height:390},deviceScaleFactor:2});page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base+(mode==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);await page.evaluate(()=>start('conquest1','fra'));await page.waitForFunction(()=>ctx&&[...images.values()].every(i=>i.complete));
      const result=await page.evaluate(async()=>{
        suspendRendering();
        const near=state.units.find(u=>u.owner===state.playerFaction),far={...near,id:'offscreen-fixture',cell:state.cells.length-1,profile:'fixture-far'};
        manifest.profiles['fixture-near']={...manifest.profiles[near.profile],library:'fixture-near-library'};manifest.profiles['fixture-far']={...manifest.profiles[near.profile],library:'fixture-far-library'};
        const fixture={...state,units:[{...near,profile:'fixture-near'},far]};
        const resolveProfile=effects.profile;effects.profile=id=>manifest.profiles[id];
        const scoped=battleLibraries(fixture,true),forced=battleLibraries({...fixture,movements:[{unit:far.id}]},true),combat=battleLibraries({...fixture,combat:{attacker:far.id,defender:near.id}},true);
        effects.profile=resolveProfile;
        delete manifest.profiles['fixture-near'];delete manifest.profiles['fixture-far'];
        const profile=visualProfile(near),library=profile.library;libraries.delete(library);
        view.x=Ew4Battlefield.point(near.cell,state.width).x;view.y=Ew4Battlefield.point(near.cell,state.width).y;view.zoom=2;clampView();
        render(2000);suspendRendering();const queued=battlefield.libraryRequests?.has(library);
        for(let i=0;i<200&&!libraries.has(library);i++)await new Promise(resolve=>setTimeout(resolve,25));
        render(2000);suspendRendering();
        return {scoped,forced,combat,queued,loaded:libraries.has(library),pending:battlefield.libraryRequests?.has(library),visible:battlefield.visibleUnits.some(u=>u.id===near.id)};
      });
      assert(result.scoped.includes('fixture-near-library')&&!result.scoped.includes('fixture-far-library'),'offscreen-only models do not block state installation');
      assert(result.forced.includes('fixture-far-library')&&result.combat.includes('fixture-far-library'),'movement and combat actors are loaded before presentation');
      assert(result.queued&&result.loaded&&!result.pending&&result.visible,'newly visible model loads and redraws on both backends');report.cases.push({mode,...result});await page.screenshot({path:path.join(folder,mode+'.png')});await page.close();
    }
    assert.deepEqual(report.errors,[]);
  }finally{await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
