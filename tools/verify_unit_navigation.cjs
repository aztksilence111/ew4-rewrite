'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),vm=require('node:vm'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5426',folder=path.resolve(process.argv[3]||'build/unit-navigation-fix-20261023/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],screenshots:[],scope:'Pure unit-state cases and real shared Web UI; Android safe viewport sizes simulated, no device execution'};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const sandbox={};vm.runInNewContext(await fs.readFile('rewrite/Ew4.Web/wwwroot/unit-navigation.js','utf8'),sandbox);
  const api=sandbox.Ew4UnitNavigation;
  const unit=(id,fields={})=>({id,owner:'p',cell:0,health:100,morale:0,constructionTurns:0,category:'infantry',canMove:true,canNavigateMove:true,canAttack:false,remainingMovement:10,acted:false,...fields});
  const fixture={scenario:'test',round:1,activeFaction:'p',playerFaction:'p',phase:'Active',factions:[{id:'p',human:true}],units:[
    unit('fresh-a',{canAttack:true}),unit('moved',{canMove:false,canNavigateMove:false,canAttack:true,remainingMovement:0}),unit('fresh-b'),unit('attacked',{acted:true}),
    unit('spent',{canMove:false,canNavigateMove:false,remainingMovement:0,acted:true}),unit('fort',{canMove:false,canNavigateMove:false,canAttack:true,remainingMovement:0,category:'fort'}),
    unit('idle-fort',{canMove:false,canNavigateMove:false,remainingMovement:0,category:'fort'}),unit('blocked',{canNavigateMove:false}),
    unit('moved-no-target',{canMove:false,canNavigateMove:false,remainingMovement:0}),
    unit('enemy',{owner:'e'}),unit('stunned',{morale:-3}),unit('building',{constructionTurns:2}),unit('dead',{health:0})]};
  const cycle=new api.Cycle(),expected=['fresh-a','fresh-b','fort','moved','attacked','fresh-a'];
  for(const id of expected)check(cycle.next(fixture)?.id===id,'priority cycle '+id);
  check(api.groups(fixture)[0].some(u=>u.id==='fresh-b'&&!u.canAttack),'unit with a legal move remains eligible without an attack target');
  check(api.groups(fixture)[1].some(u=>u.id==='moved'),'moved unit with a legal attack remains eligible');
  check(!api.groups(fixture).flat().some(u=>['idle-fort','blocked','moved-no-target'].includes(u.id)),'no-target forts, surrounded units and spent movers are excluded');
  fixture.units[0].canMove=false;fixture.units[0].canNavigateMove=false;fixture.units[0].remainingMovement=0;
  check(cycle.next(fixture)?.id==='fresh-b','moving the last found unit cannot skip other untouched units');
  check(cycle.next(fixture)?.id==='fort','finish untouched tier before revisiting a moved unit');
  check(cycle.next(fixture)?.id==='fresh-a','a newly partial unit can be found again in the second tier');
  fixture.round++;check(cycle.next(fixture)?.id==='fresh-b','new turn clears visited units');
  for(const patch of [{phase:'Finished'},{dialogue:'pending'},{scenarioPopup:{}},{activeFaction:'e'},{factions:[{id:'p',human:false}]},{factions:[{id:'p',human:true,defeated:true}]},{units:[]}])
    check(api.groups({...fixture,...patch}).every(group=>group.length===0),'unavailable state '+Object.keys(patch)[0]);
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720},hasTouch:true});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    await page.evaluate(()=>start('conquest1','fra'));
    await page.waitForFunction(()=>!busy&&!$('next-unit').disabled);
    const initial=await page.evaluate(()=>({revision:state.revision,units:JSON.stringify(state.units),ids:Ew4UnitNavigation.groups(state)[0].map(u=>u.id),zoom:view.zoom}));
    check(initial.ids.length>2,'real scenario has multiple untouched player units');
    check(await page.evaluate(()=>!Ew4UnitNavigation.groups(state).flat().some(u=>!u.canNavigateMove&&!u.canAttack)),'real no-target forts are excluded');
    for(const id of initial.ids){
      await page.locator('#next-unit').click();
      await page.waitForFunction(id=>!findingUnit&&selected?.id===id,id);
      check(await page.evaluate(id=>selected.id===id&&selection.cell===selected.cell,id),'real untouched selection '+id);
      check(await page.evaluate(()=>{const p=point(selected.cell),expected=Ew4Camera.constrain({...view,x:p.x,y:p.y},{w:canvas.clientWidth,h:canvas.clientHeight},mapBounds||manifest.maps[state.map]);return Math.abs(view.x-expected.x)<.01&&Math.abs(view.y-expected.y)<.01;}),'camera follows selected unit within map bounds');
    }
    check(await page.evaluate(before=>state.revision===before.revision&&JSON.stringify(state.units)===before.units&&view.zoom===before.zoom,initial),'search only selects and pans, preserving gameplay state and zoom');
    // Make a real legal move, then ensure all untouched units precede that unit.
    const moved=await page.evaluate(async()=>{
      let cell;
      for(const unit of Ew4UnitNavigation.groups(state)[0].filter(u=>u.canMove)){
        await selectCell(unit.cell,true,true);
        cell=selection.range.find(c=>c!==selected.cell&&!state.units.some(u=>u.cell===c));
        if(cell!==undefined)break;
      }
      const id=selected.id;
      if(cell===undefined)throw Error('No legal test move');
      await act('move',{cell});unitNavigation.reset();
      return {id,cell,canAttack:state.units.find(u=>u.id===id).canAttack,untouched:Ew4UnitNavigation.groups(state)[0].map(u=>u.id)};
    });
    for(const id of moved.untouched){await page.locator('#next-unit').click();await page.waitForFunction(id=>!findingUnit&&selected?.id===id,id);}
    await page.locator('#next-unit').click();await page.waitForFunction(()=>!findingUnit);
    check(await page.evaluate(({id,canAttack})=>canAttack?selected.id===id:!Ew4UnitNavigation.groups(state).flat().some(u=>u.id===id),moved),'real moved unit remains only when a legal attack exists');
    check(await page.evaluate(()=>Number($('next-unit-count').dataset.count)===Ew4UnitNavigation.groups(state).flat().length),'badge and navigation share the same remaining-unit count');
    const before=await page.evaluate(()=>state.revision);
    let selectRequests=0;
    await page.route('**/api/action?**',async route=>{const args=JSON.parse(new URL(route.request().url()).searchParams.get('q'));if(args.op==='select'){selectRequests++;await new Promise(resolve=>setTimeout(resolve,180));}await route.continue();});
    await page.evaluate(()=>Promise.all([findNextUnit(),findNextUnit()]));await page.unroute('**/api/action?**');
    check(selectRequests===1,'rapid repeated search uses one selection request');
    check(await page.evaluate(revision=>!busy&&!findingUnit&&state.revision===revision,before),'rapid search cannot spend actions');
    await page.evaluate(()=>{$('pause').hidden=false;});
    const paused=await page.evaluate(()=>selected.id);await page.evaluate(()=>findNextUnit());
    check(await page.evaluate(id=>selected.id===id,paused),'paused battle blocks unit search');await page.evaluate(()=>{$('pause').hidden=true;});
    // Native Android pads the parent and gives the WebView exactly these safe dimensions.
    const cases=[['desktop',1280,720],['phone-landscape',820,336],['phone-reverse-landscape',820,336],['wide-phone',956,360],['small-landscape',544,248],['portrait',336,700]];
    for(const [name,width,height] of cases){
      await page.setViewportSize({width,height});await page.waitForTimeout(120);
      const bounds=await page.evaluate(()=>{
        const ids=['undo-move','next-unit','end-turn','menu-button'],rects=ids.map(id=>{const r=$(id).getBoundingClientRect();return {id,x:r.x,y:r.y,right:r.right,bottom:r.bottom,width:r.width,height:r.height};});
        return {rects,w:innerWidth,h:innerHeight,canvas:[canvas.clientWidth,canvas.clientHeight],scale:+getComputedStyle(document.documentElement).getPropertyValue('--battle-scale')};
      });
      for(const r of bounds.rects)check(r.x>=-.1&&r.y>=-.1&&r.right<=width+.1&&r.bottom<=height+.1,name+' '+r.id+' fully inside safe viewport');
      check(Math.abs(bounds.rects[1].right-bounds.rects[2].right)<.1&&bounds.rects[1].bottom<bounds.rects[2].y,name+' honeycomb is aligned above the hourglass');
      check(bounds.canvas[0]===width&&bounds.canvas[1]===height,name+' canvas uses the same safe viewport');
      for(const id of ['undo-move','next-unit','menu-button'])check(await page.locator('#'+id).evaluate(el=>{const r=el.getBoundingClientRect();return document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest('button')===el;}),name+' '+id+' touch target is not covered');
      check(await page.locator('#next-unit').evaluate(el=>{const r=el.getBoundingClientRect();return document.elementFromPoint(r.left-2,r.top-2)?.closest('button')!==el;}),name+' shadow does not intercept map input');
      const badgeCases=await page.evaluate(()=>{
        const saved=state,unit=state.units.find(u=>u.owner===state.playerFaction&&u.canNavigateMove),results=[];
        for(const count of [0,1,9,99,100,999,1000,10000]){
          state={...saved,units:Array.from({length:count},(_,i)=>({...unit,id:'badge-case.'+i}))};refreshHud();
          const badge=$('next-unit-count'),b=$('next-unit'),canvas=badge.querySelector('canvas'),r=canvas.getBoundingClientRect(),p=b.getBoundingClientRect();
          const pixels=canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
          results.push({count,matches:badge.dataset.count===String(count),disabled:b.disabled,
            inside:r.left>=p.left&&r.top>=p.top&&r.right<=p.right+.1&&r.bottom<=p.bottom+.1,
            ink:pixels.some((v,i)=>i%4===3&&v>0)});
        }
        state=saved;refreshHud();return results;
      });
      for(const b of badgeCases)check(b.matches&&b.inside&&b.ink&&b.disabled===(b.count===0),name+' count '+b.count+' is correct, legible and inside the button');
      await page.screenshot({path:path.join(folder,name+'.png')});report.screenshots.push(name+'.png');
      await page.locator('#menu-button').click();await page.locator('#pause-settings').click();
      check(await page.locator('#confirm-settings').evaluate(el=>{const r=el.getBoundingClientRect();return r.x>=0&&r.y>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;}),name+' settings confirm fits');
      await page.locator('#close-settings').click();await page.locator('#resume').click();
    }
    await page.evaluate(()=>{state={...state,units:state.units.map(u=>u.owner===state.playerFaction?{...u,acted:true,canMove:false,canNavigateMove:false,canAttack:false,remainingMovement:0}:u)};refreshHud();});
    check(await page.locator('#next-unit').isDisabled(),'no remaining units disables search');
    check(await page.locator('#next-unit-count').getAttribute('data-count')==='0','zero remaining units displays zero');
    await page.evaluate(()=>{state={...state,activeFaction:state.factions.find(f=>!f.human).id};refreshHud();});
    check(await page.locator('#next-unit').isHidden(),'AI turn hides human search control');
    check(report.errors.length===0,'no browser exceptions');check(report.missing.length===0,'no missing resources');
  }finally{await browser.close();await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));}
  console.log(JSON.stringify(report));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
