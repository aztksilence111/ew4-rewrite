'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5420',folder=process.argv[3]||'build/release-0.2.14-20261023/browser';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:844,height:390},deviceScaleFactor:3,hasTouch:true});
  const report={checks:0,errors:[],missing:[],shots:[],deviceExecution:false};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  const shot=async name=>{
    await page.waitForFunction(()=>[...document.images].filter(n=>n.getClientRects().length).every(n=>n.complete&&n.naturalWidth)&&[...document.querySelectorAll('.rank-gauge')].every(n=>n.dataset.ready==='true'));
    await page.screenshot({path:folder+'/'+name+'.png'});report.shots.push(name);
  };
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady);await page.evaluate(()=>Ew4OriginalUI.prepareText('中文数字0123456789升级'));
    report.fonts=await page.evaluate(()=>{
      const node=originalTextRenderer.element('中文数字0123456789升级','font_text_2_cn_hd');
      const c=node.firstElementChild,draw=c.getContext('2d');
      return {sheets:originalTextRenderer.sheets.size,pixels:c.width,cssWidth:parseFloat(c.style.width),ink:[...draw.getImageData(0,0,c.width,c.height).data].filter((v,i)=>i%4===3&&v>0).length,
        bitmapGlyph:originalTextRenderer.glyph('中','font_text_2_cn_hd')};
    });
    check(report.fonts.sheets===0&&report.fonts.bitmapGlyph===null,'all UI text bypasses bitmap glyphs');
    check(report.fonts.pixels/report.fonts.cssWidth>=6&&report.fonts.ink>100,'mobile DPR3 text has dense nonblank raster');
    for(const [width,height] of [[844,390],[1280,720],[2376,1080],[544,248],[390,844]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(200);await page.evaluate(()=>showMain());
      const labels=await page.locator('.main-entry').evaluateAll(nodes=>nodes.map(node=>{const c=node.querySelector('canvas'),p=c?.getContext('2d').getImageData(0,0,c.width,c.height).data;return {text:node.getAttribute('aria-label'),ink:p?[...p].filter((v,i)=>i%4===3&&v>128).length:0};}));
      check(labels.length===5&&labels.every(label=>label.text&&label.ink>100),'all five main-menu captions remain visible '+width);await shot('menu-'+width);
      await page.evaluate(()=>start('conquest1','fra'));await page.waitForTimeout(200);
      await page.evaluate(()=>{const u=state.units.find(u=>u.owner===state.playerFaction&&u.canMove);return selectCell(u.cell,true,true);});
      check(await page.evaluate(()=>battlefield.mapLabels.size>0&&[...battlefield.mapLabels.values()].every(l=>l.canvas.width>0&&l.canvas.height>0)),'legacy geographic labels retain nonblank dimensions '+width);
      const sizes=await page.evaluate(()=>{
        const rect=id=>{const r=$(id).getBoundingClientRect();return {id,x:r.x,y:r.y,right:r.right,bottom:r.bottom,w:r.width,h:r.height};};
        return {scale:parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--battle-scale')),controls:['menu-button','end-turn','undo-move','next-unit','actions'].map(rect),resources:document.querySelector('.battle-resources').getBoundingClientRect().height};
      });
      const [pause,end,undo,next,actions]=sizes.controls;
      check(Math.abs(pause.w-27*sizes.scale)<.1,'pause size preserved '+width);
      check(Math.abs(end.w/pause.w-.75)<.01,'end turn reduced 25 percent '+width);
      check(Math.abs(undo.w-37*sizes.scale*.75)<.1&&Math.abs(next.w-undo.w)<.1,'undo and locate reduced '+width);
      check(Math.abs(sizes.resources-23*sizes.scale*.75)<.1,'resource bar reduced '+width);
      for(const r of sizes.controls)check(r.x>=-.1&&r.y>=-.1&&r.right<=width+.1&&r.bottom<=height+.1,'control inside viewport '+width+' '+r.id);
      check(next.right<=actions.x+1&&actions.right<=end.x+1,'toolbar groups do not overlap '+width);
      await shot('battle-'+width);
    }
    await page.setViewportSize({width:844,height:390});
    report.geography=await page.evaluate(()=>{
      const renderer=battlefield,old={map:renderer.map,draw:renderer.draw,visible:renderer.visible,mapLabels:renderer.mapLabels},samples=[],oldLanguage=settings.geographicLanguage;
      try{
        settings.geographicLanguage='en';
        renderer.draw=document.createElement('canvas').getContext('2d');renderer.visible=()=>true;
        for(const name of ['europe','america','eastasia']){
          renderer.map={labels:[manifest.maps[name].labels[0]]};renderer.mapLabels=new Map();renderer.mapText();
          const cached=[...renderer.mapLabels.values()][0],c=cached?.canvas,pixels=c?.getContext('2d').getImageData(0,0,c.width,c.height).data;
          const expected=document.createElement('canvas');expected.width=c.width;expected.height=c.height;
          const draw=expected.getContext('2d'),label=renderer.map.labels[0],lib=renderer.api.libraries.get('maptext');
          draw.translate(-cached.left,-cached.top);draw.globalAlpha=Number.isFinite(label.opacity)?Math.max(0,Math.min(1,label.opacity)):60/255;
          renderer.api.animation(lib,lib.byName.get(label.libName),0,0,draw);
          samples.push({name,width:c?.width,height:c?.height,originalArtworkPixels:c.toDataURL()===expected.toDataURL(),ink:pixels?[...pixels].filter((v,i)=>i%4===3&&v>0).length:0});
        }
      }finally{Object.assign(renderer,old);settings.geographicLanguage=oldLanguage;}
      return samples;
    });
    for(const sample of report.geography)check(sample.originalArtworkPixels&&sample.ink>50&&sample.width>10&&sample.height>10,'geographic label pixels match original artwork in '+sample.name);
    report.formations=await page.evaluate(()=>{
      const ids=[0,1,2].map(grade=>Object.keys(manifest.profiles).find(id=>manifest.profiles[id].grade===grade));
      const result=ids.map(id=>{const node=abilityGrid(id).querySelector('.formation-count');return {count:Number(node.dataset.count),filled:node.querySelectorAll('[data-filled="true"]').length,empty:[...node.querySelectorAll('[data-filled="false"]')].every(n=>n.style.opacity==='.2'||n.style.opacity==='0.2')};});
      catalog.formationCounts={[ids[0]]:5};const node=abilityGrid(ids[0]).querySelector('.formation-count');result.push({count:Number(node.dataset.count),icons:node.querySelectorAll('canvas:not(.original-text canvas)').length,text:node.querySelector('.original-text')?.getAttribute('aria-label')});catalog.formationCounts={};return result;
    });
    for(let i=0;i<3;i++)check(report.formations[i].filled===i+1&&report.formations[i].empty,'formation markers distinguish '+(i+1));
    check(report.formations[3].count===5&&report.formations[3].text==='\u00d75','configurable count above three uses icon times number');
    await page.evaluate(()=>{const cell=state.cells.findIndex(c=>c.owner===state.playerFaction&&c.buildingKind==='city');return selectCell(cell,true,true);});
    await page.evaluate(()=>{
      const family=Object.values(manifest.profiles).find(p=>p.grade===0&&[1,2].every(grade=>Object.values(manifest.profiles).some(q=>q.unitIndex===p.unitIndex&&q.grade===grade)));
      // Isolate the card-cycling view from the starting city's funds and unlocks.
      selection.offers=[0,1,2].map(grade=>{const profile=Object.keys(manifest.profiles).find(id=>manifest.profiles[id].unitIndex===family.unitIndex&&manifest.profiles[id].grade===grade);return {id:'fixture-'+grade,profile,cost:{money:0,industry:0,food:0}};});
      openRecruitment(selected);
    });
    if(await page.locator('.recruit-cards>button').count()){
      const card=page.locator('.recruit-cards>button').first();
      report.cycle=[];
      for(let i=0;i<3;i++){await card.click();await page.waitForTimeout(280);report.cycle.push(await page.locator('.recruit-description .formation-count').getAttribute('data-count'));}
      check(new Set(report.cycle).size===3,'real card clicks update all three formation markers');await shot('recruitment');
    }else throw Error('Recruitment fixture has no offers');
    await page.evaluate(async()=>{closeDetail();await showMain();await menus.headquarters();if(!menus.data.roster.length){const p=menus.data.princesses.find(p=>!p.claimed);await menus.updateAccount({op:'claim',offer:p.id});}});
    report.promotion=await page.evaluate(()=>{
      const held=menus.data.roster[0];if(!held)throw Error('missing commander fixture');menus.promotion(held.id);
      return [...document.querySelectorAll('[data-layout="form_generalupgrade"] [data-native-id]')].filter(n=>/text_life|text_medal/.test(n.dataset.nativeId)).map(n=>({size:Number(n.querySelector('.original-text')?.dataset.size),height:n.clientHeight}));
    });
    check(report.promotion.length===7&&report.promotion.every(n=>n.size>=12&&n.height===20),'all promotion values and prices enlarged');await shot('promotion');
    report.damage=await page.evaluate(()=>{damageNumbers.add(123,true,view.x,view.y);const c=damageNumbers.instances.at(-1).canvas;const pixels=c.getContext('2d').getImageData(0,0,c.width,c.height).data;let red=0;for(let i=0;i<pixels.length;i+=4)if(pixels[i]>180&&pixels[i+1]<90&&pixels[i+3]>128)red++;return {red,width:c.width,height:c.height};});
    check(report.damage.red>100&&report.damage.height===96,'damage digits render with bundled font and critical red');
    check(report.errors.length===0&&report.missing.length===0,'no browser or resource failures');
  }catch(e){report.failure=e.message;throw e;}
  finally{await fs.writeFile(folder+'/verification.json',JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,screenshots:report.shots.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
