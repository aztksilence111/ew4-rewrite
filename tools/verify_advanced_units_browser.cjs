'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:57945',run=path.resolve('build/advanced-units-20261102');
(async()=>{
  const fixture=JSON.parse(await fs.readFile(path.join(run,'mod/fixture.json'),'utf8')),out=path.join(run,'browser');await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],views:[]},check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    await fetch(base+'/api/mods',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:'activate',key:fixture.key})});
    for(const [source,renderer,width,height] of [[true,'canvas',1280,720],[false,'pixi',1280,720],[false,'canvas',390,844],[false,'pixi',844,390]]){
      const label=[source?'source':'compat',renderer,width].join('-'),context=await browser.newContext({viewport:{width,height},hasTouch:width<1000,isMobile:width<1000}),page=await context.newPage();
      page.on('pageerror',e=>report.errors.push(label+': '+e.message));page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(source)await page.route(base+'/',async route=>{const response=await route.fetch();await route.fulfill({response,body:(await response.text()).replace(/((?:src|href)=")compat\//g,'$1')});});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(async fixture=>{await load(1,0);stageIntroOpen=false;$('detail-modal').hidden=true;await selectCell(fixture.cell,true,true);const p=point(fixture.cell);view.x=p.x;view.y=p.y;view.zoom=1.6;clampView();requestRender();},fixture);
      check(await page.evaluate(()=>selection.buildingOffers.length===1&&selection.installationOffers.length===3),label+' engineer has airport and original construction');
      await page.evaluate(()=>openDefense());await page.evaluate(async()=>{await Promise.all(document.getAnimations().map(a=>a.finished.catch(()=>{})));});
      check(await page.locator('.defense-cards>button').count()===4,label+' airport build card appears with existing installations');
      await page.locator('.defense-cards>button').last().click();
      const rectangles=await page.locator('.defense-cards').evaluate(el=>({width:el.clientWidth,scroll:el.scrollWidth}));
      check(await page.locator('.defense-cards').evaluate(el=>el.getBoundingClientRect().left>=0&&el.getBoundingClientRect().right<=innerWidth),label+' construction cards stay inside viewport and airport scrolls into view');
      await page.screenshot({path:path.join(out,label+'-build.png')});await page.evaluate(()=>closeDetail());
      await page.evaluate(async fixture=>{await load(4,0);stageIntroOpen=false;$('detail-modal').hidden=true;const p=point(fixture.cell);view.x=p.x;view.y=p.y;view.zoom=1.6;clampView();await selectCell(fixture.cell,true,true);requestRender();},fixture);
      check(await page.evaluate(id=>selected.id===id&&selection.occupants.length===1,fixture.heli),label+' one selection controls both parts');
      const parts=await page.evaluate(fixture=>{const u=state.units.find(u=>u.id===fixture.heli),p=visualProfile(u);return effects.parts(p,false).map(p=>({layer:p.partLayer,library:p.library}));},fixture);
      check(parts.length===2&&parts[0].layer==='surface'&&parts[1].layer==='air',label+' two independent visual components');
      await page.evaluate(async()=>openUnitInfo(selected));await page.evaluate(async()=>{await Promise.all(document.getAnimations().map(a=>a.finished.catch(()=>{})));});
      check(await page.locator('[data-native-id=text_desc]').evaluate(el=>[...el.querySelectorAll('[aria-label]')].some(n=>n.getAttribute('aria-label').includes('空中＋地表'))),label+' dual footprint appears in properties');
      await page.screenshot({path:path.join(out,label+'-properties.png')});await page.evaluate(()=>closeDetail());
      const animations=await page.evaluate(async fixture=>{
        const unit=state.units.find(u=>u.id===fixture.heli),target={...state.units.find(u=>u.owner!==unit.owner),id:'visual.target',cell:unit.cell+1},original=state;
        state={...state,units:state.units.concat(target)};
        const seen=[],plan=effects.attackPlan;effects.attackPlan=function(profile,...args){seen.push({part:profile.part,layer:profile.partLayer,visual:profile.visualMotion});return plan.call(this,profile,...args);};
        try{await playCombat({attacker:unit.id,defender:target.id,damage:0,attackLayer:'air'});combatMotions.clear();await playCombat({attacker:unit.id,defender:target.id,damage:0,attackLayer:'surface'});}
        finally{effects.attackPlan=plan;combatMotions.clear();state=original;}
        return seen;
      },fixture);
      check(animations.length===2&&animations[0].part===1&&animations[0].visual==='demo.attack.air'&&animations[1].part===0,label+' actual combat selects air override then ground animation');
      const bodies=await page.evaluate(fixture=>{
        const unit=state.units.find(u=>u.id===fixture.heli),profile=visualProfile(unit),p=unitPoint(unit),bodyParts=effects.parts(profile,false),old=state;
        const record=(sea)=>{let ships=0;const calls=[],sprite=battlefield.api.sprite,animation=battlefield.unitAnimation;
          battlefield.api.sprite=(atlas,name,...rest)=>{if(name==='transportship1.png'||name==='transportship2.png')ships++;return sprite(atlas,name,...rest);};
          battlefield.unitAnimation=function(lib,item,tick){if(bodyParts.some(p=>libraries.get(p.library)===lib))calls.push({item:item?.name,tick});return animation.call(this,lib,item,tick);};
          try{renderDirty=true;render(performance.now(),true);}finally{battlefield.api.sprite=sprite;battlefield.unitAnimation=animation;}
          return {parts:effects.parts(profile,sea).length,ships,calls:calls.length,visible:battlefield.visibleUnitMarkers.some(m=>m.unit.id===unit.id)};
        };
        const land=record(false),candidates=state.cells.map((c,i)=>({c,i,p:point(i)})).filter(s=>s.c.active&&s.c.sea).sort((a,b)=>Math.hypot(a.p.x-p.x,a.p.y-p.y)-Math.hypot(b.p.x-p.x,b.p.y-p.y));
        const cell=candidates[0].i;state={...state,units:state.units.map(u=>u.id===unit.id?{...u,cell}:u)};const q=point(cell);view.x=q.x;view.y=q.y;clampView();const sea=record(true);return {land,sea};
      },fixture);
      check(bodies.land.parts===2&&bodies.land.visible&&bodies.land.calls>0,label+' both-part unit renders on land');
      check(bodies.sea.parts===1&&bodies.sea.visible&&bodies.sea.ships===0,label+' water shows only air body without transport ship');
      const shot=await page.screenshot({path:path.join(out,label+'-sea.png')});
      const colors=await page.evaluate(async encoded=>{const bitmap=new Image();bitmap.src='data:image/png;base64,'+encoded;await bitmap.decode();const sample=document.createElement('canvas');sample.width=bitmap.width;sample.height=bitmap.height;const d=sample.getContext('2d');d.drawImage(bitmap,0,0);const pixels=d.getImageData(0,0,sample.width,sample.height).data,c=new Set();for(let i=0;i<pixels.length;i+=64)c.add((pixels[i]<<16)|(pixels[i+1]<<8)|pixels[i+2]);return c.size;},shot.toString('base64'));
      check(colors>100,label+' battlefield canvas is nonblank');
      report.views.push({label,parts,animations,rectangles,colors});await context.close();
    }
    check(!report.errors.length&&!report.missing.length,'no script errors or missing resources');
  }finally{
    await fetch(base+'/api/mods',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:'activate',key:null})});
    await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();
  }
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e);process.exitCode=1;});
