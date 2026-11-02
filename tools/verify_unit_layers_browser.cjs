'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright');
const base=process.argv[2]||'http://127.0.0.1:57944',run=path.resolve(process.argv[3]||'build/unit-layers-20261102');
(async()=>{
  const fixture=JSON.parse(await fs.readFile(path.join(run,'mod/fixture.json'),'utf8')),out=path.join(run,'browser');
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],views:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    const activation=await fetch(base+'/api/mods',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:'activate',key:fixture.key})});
    check((await activation.json()).active===fixture.key,'activate isolated test Mod');
    for(const [source,renderer,width,height] of [[true,'canvas',1280,720],[false,'pixi',1280,720],[false,'canvas',390,844],[false,'pixi',844,390]]){
      const label=[source?'source':'compat',renderer,width].join('-'),context=await browser.newContext({viewport:{width,height},hasTouch:width<1000,isMobile:width<1000}),page=await context.newPage();
      page.on('pageerror',e=>report.errors.push(label+': '+e.message));
      page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(source)await page.route(base+'/',async route=>{const response=await route.fetch();await route.fulfill({response,body:(await response.text()).replace(/((?:src|href)=")compat\//g,'$1')});});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(async fixture=>{
        await load(1,0);stageIntroOpen=false;$('detail-modal').hidden=true;
        if(!state?.unitLayers)throw Error('Layered fixture did not load');
        const p=point(fixture.cell);view.x=p.x;view.y=p.y;view.zoom=1.6;clampView();
        for(const id of [fixture.ground,fixture.air])await loadLibrary(visualProfile(state.units.find(u=>u.id===id)).library);
        await selectCell(fixture.cell,true,true);requestRender();
      },fixture);
      check(await page.evaluate(id=>selected.id===id,fixture.ground),label+' default surface selection');
      check(await page.locator('.unit-layer-switch').count()===0,label+' no extra battlefield layer buttons');
      await page.evaluate(async cell=>selectCell(cell),fixture.cell);await page.waitForFunction(id=>selected?.id===id,fixture.air);
      check(await page.evaluate(id=>selected.id===id,fixture.air),label+' repeat cell selection cycles to air');
      await page.evaluate(async()=>openUnitInfo(selected));
      await page.evaluate(async()=>{await Promise.all(document.getAnimations().map(animation=>animation.finished.catch(()=>{})));});
      const attribute=await page.locator('[data-native-id=text_desc]').evaluate(el=>({text:el.textContent,labels:[...el.querySelectorAll('[aria-label]')].map(n=>n.getAttribute('aria-label')).join(' '),rect:el.getBoundingClientRect().toJSON()}));
      check((attribute.text+' '+attribute.labels).includes('作战层：空中'),label+' layer appears in unit properties');
      check(attribute.rect.x>=0&&attribute.rect.y>=0&&attribute.rect.right<=width&&attribute.rect.bottom<=height,label+' unit properties fit viewport');
      await page.screenshot({path:path.join(out,label+'-properties.png')});
      await page.evaluate(()=>closeDetail());
      const markers=await page.evaluate(fixture=>{
        renderDirty=true;render(performance.now(),true);
        const a=battlefield.visibleUnitMarkers.find(m=>m.unit.id===fixture.air),g=battlefield.visibleUnitMarkers.find(m=>m.unit.id===fixture.ground);
        return {dx:a.p.x-g.p.x,dy:a.p.y-g.p.y,stationed:battlefield.stationed.get(fixture.cell)?.id};
      },fixture);
      check(markers.dx===18&&markers.dy===-24&&markers.stationed===fixture.ground,label+' separate markers and surface occupancy');
      const shot=await page.screenshot({path:path.join(out,label+'.png')});
      const colors=await page.evaluate(async encoded=>{
        const bitmap=new Image();bitmap.src='data:image/png;base64,'+encoded;await bitmap.decode();
        const sample=document.createElement('canvas');sample.width=bitmap.width;sample.height=bitmap.height;const draw=sample.getContext('2d');draw.drawImage(bitmap,0,0);
        const pixels=draw.getImageData(0,0,sample.width,sample.height).data,colors=new Set();for(let i=0;i<pixels.length;i+=64)colors.add((pixels[i]<<16)|(pixels[i+1]<<8)|pixels[i+2]);return colors.size;
      },shot.toString('base64'));
      check(colors>100,label+' rendered screenshot is nonblank');
      await page.evaluate(async cell=>selectCell(cell),fixture.cell);await page.waitForFunction(id=>selected?.id===id,fixture.ground);
      check(await page.evaluate(id=>selected.id===id,fixture.ground),label+' repeat cell selection cycles back to surface');
      await page.evaluate(async fixture=>{selection.targetUnits=[fixture.ground,fixture.air];selection.targets=[fixture.cell];await selectCell(fixture.cell);},fixture);
      check(await page.locator('.layer-targets>button').count()===2,label+' target picker presents both eligible IDs');
      await page.evaluate(()=>closeDetail());
      const sea=await page.evaluate(fixture=>{
        const oldState=state,unit=state.units.find(u=>u.id===fixture.air),origin=point(unit.cell);
        const candidates=state.cells.map((c,i)=>({c,i,p:point(i)})).filter(s=>s.c.active&&s.c.sea).sort((a,b)=>Math.hypot(a.p.x-origin.x,a.p.y-origin.y)-Math.hypot(b.p.x-origin.x,b.p.y-origin.y));
        const cell=candidates[0].i;state={...state,units:state.units.map(u=>u.id===fixture.air?{...u,cell}:u)};
        const p=point(cell);view.x=p.x;view.y=p.y;clampView();
        const airPosition=Ew4Battlefield.unitPoint(state,state.units.find(u=>u.id===fixture.air));
        let ships=0;const sprite=battlefield.api.sprite;
        battlefield.api.sprite=(atlas,name,...rest)=>{if((name==='transportship1.png'||name==='transportship2.png')&&rest[0]===airPosition.x&&rest[1]===airPosition.y)ships++;return sprite(atlas,name,...rest);};
        let visible=false;
        try{renderDirty=true;render(performance.now(),true);visible=battlefield.visibleUnitMarkers.some(m=>m.unit.id===fixture.air&&m.p.x===airPosition.x&&m.p.y===airPosition.y);}
        finally{battlefield.api.sprite=sprite;state=oldState;}
        return {ships,sea:state.cells[cell].sea,visible};
      },fixture);
      check(sea.sea&&sea.visible&&sea.ships===0,label+' visible aircraft over water does not draw transport ship '+JSON.stringify(sea));
      report.views.push({label,markers,colors,attribute});await context.close();
    }
    const context=await browser.newContext(),page=await context.newPage();
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await page.evaluate(async()=>{await fetch('/api/mods',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:'activate',key:null})});});
    await page.reload();await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await page.evaluate(async()=>{await start('conquest1','fra');stageIntroOpen=false;$('detail-modal').hidden=true;await selectCell(state.units.find(u=>u.owner===state.playerFaction).cell,true,true);});
    check(await page.evaluate(()=>state.unitLayers===false),'builtin gameplay remains disabled');
    check(await page.locator('.unit-layer-switch').count()===0,'builtin has no layer controls');
    await context.close();
    check(!report.errors.length&&!report.missing.length,'no script errors or missing resources');
  }finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e);process.exitCode=1;});
