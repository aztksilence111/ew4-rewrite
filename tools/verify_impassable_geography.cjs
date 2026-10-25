'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5532',folder=path.resolve('build/impassable-geography-20261025/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const ridge=JSON.parse(await fs.readFile('maps/eastasia_rewrite/impassable/himalaya-cells.json','utf8'));
  const shikoku=JSON.parse(await fs.readFile('maps/eastasia_rewrite/cities.json','utf8')).find(c=>c.name==='高知');
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,cases:[],errors:[],missing:[]};
  const check=(v,label)=>{assert.ok(v,label);report.checks++;};
  try{
    const originalStatus=await(await fetch(base+'/api/mods')).json();
    for(const [name,width,height] of [['desktop',1280,720],['phone',844,390],['portrait',390,844]])for(const backend of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest7','fra'));
      await page.waitForFunction(()=>ctx&&[...images.values()].every(i=>i.complete));
      const result=await page.evaluate(async ridge=>{
        stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();
        const pass=ridge.passes[1],cell=pass.openedBarrierCells[0];
        const blocked=ridge.blockedCells.reduce((a,b)=>Math.abs(a-cell)<Math.abs(b-cell)?a:b);
        const total=state.cells.filter(c=>c.geographyType==='impassable_mountain').length;
        const samples=[];
        const seas=state.cells.map((c,i)=>({c,i})).filter(p=>p.c.active&&p.c.sea);
        const oceanTarget=seas.reduce((a,b)=>Math.hypot(a.i%state.width-state.width*.75,Math.floor(a.i/state.width)-state.height*.65)<Math.hypot(b.i%state.width-state.width*.75,Math.floor(b.i/state.width)-state.height*.65)?a:b).i;
        for(const target of [blocked,oceanTarget]){
          const ocean=state.cells[target].sea;
          if(ocean){
            const changes=[target,target-1,target+1,target-state.width,target+state.width].filter(i=>state.cells[i]?.active&&state.cells[i]?.sea);
            state={...state,cells:state.cells.map((c,i)=>changes.includes(i)?{...c,geographyType:'impassable_sea',geographyName:'不可通行',geographySprite:'impassable_sea.png'}:c)};
          }
          await selectCell(target,false,true);suspendRendering();
          const p=point(target);Object.assign(view,p,{zoom:1.3});
          const calls=[],original=battlefield.api.sprite;
          battlefield.api.sprite=function(atlas,image,x,y,...args){if(image.startsWith('impassable_'))calls.push(image);return original(atlas,image,x,y,...args);};
          battlefield.invalidateGround();render(2000);suspendRendering();battlefield.api.sprite=original;
          const pixels=()=>new Uint8Array(ctx.renderer?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data);
          const before=pixels(),saved=state;
          state={...state,cells:state.cells.map(c=>({...c,geographySprite:null,geographyType:'none',geographyName:null}))};
          battlefield.invalidateGround();render(2000);suspendRendering();const after=pixels();
          let changed=0;for(let i=0;i<before.length;i+=4)if(before[i]!==after[i]||before[i+1]!==after[i+1]||before[i+2]!==after[i+2])changed++;
          state=saved;battlefield.invalidateGround();render(2000);suspendRendering();
          Object.assign(view,p,{zoom:.45});battlefield.invalidateGround();render(2000);suspendRendering();
          const distant=pixels();let nonblank=0;for(let i=0;i<distant.length;i+=4)if(distant[i]+distant[i+1]+distant[i+2]>30)nonblank++;
          Object.assign(view,p,{zoom:1.3});battlefield.invalidateGround();render(2000);suspendRendering();
          const rows=Ew4Battlefield.terrainRows(state.cells[target]);
          samples.push({kind:ocean?'ocean':'mountain',target,changed,calls,rows,nonblank,gpu:!!ctx.renderer});
        }
        const passSite=state.cells[cell],passRows=Ew4Battlefield.terrainRows(passSite);
        return {total,passOpen:pass.cells.every(i=>!state.cells[i].geographyType.startsWith('impassable_')),
          passMountains:pass.openedBarrierCells.every(i=>state.cells[i].geographyType==='mountain_pass'),
          passOriginal:pass.openedBarrierCells.every(i=>state.cells[i].geographySprite==='mountain_1.png'),
          passInfoOriginal:JSON.stringify(passRows)===JSON.stringify(Ew4Battlefield.terrainRows({...passSite,geographyName:null}))&&!passRows.some(r=>r.geography),samples};
      },ridge);
      check(result.total===121,'actual East Asia contains 121 barriers');check(result.passOpen,'historical corridor remains open');
      check(result.passMountains,'open crest cells still render as mountain passes');
      check(result.passOriginal,'pass uses the original single-peak mountain sprite');
      check(result.passInfoOriginal,'pass uses original terrain information without extra labels');
      for(const sample of result.samples){check(sample.changed>50,'visible geography pixels');check(sample.calls.length>0,'atlas sprite submitted');check(sample.gpu===(backend==='pixi'),'requested renderer');check(sample.rows[0].geography,'terrain info identifies barrier');check(sample.nonblank>width*height*.5,'zoomed view nonblank');}
      report.cases.push({name,backend,...result});
      await page.screenshot({path:path.join(folder,`${name}-${backend}-ocean.png`)});
      await page.evaluate(async cell=>{await selectCell(cell,false,true);const p=point(cell);Object.assign(view,p,{zoom:1.2});battlefield.invalidateGround();render(2000);suspendRendering();},result.samples[0].target);
      await page.screenshot({path:path.join(folder,`${name}-${backend}-himalaya.png`)});
      await page.evaluate(async cell=>{await selectCell(cell,false,true);const p=point(cell);Object.assign(view,p,{zoom:1.2});battlefield.invalidateGround();render(2000);suspendRendering();},ridge.passes[1].openedBarrierCells[0]);
      await page.screenshot({path:path.join(folder,`${name}-${backend}-pass.png`)});
      const city=await page.evaluate(async cell=>{
        await Ew4OriginalUI.prepareText('高知Kochi');settings.cityLanguage='zh';
        battlefield.cityLabels.clear();
        await selectCell(cell,false,true);Object.assign(view,point(cell),{zoom:1.6});battlefield.invalidateGround();render(2000);suspendRendering();
        const zhLabel=battlefield.api.cityLabel(state.cells[cell].name,cell).getAttribute('aria-label');
        settings.cityLanguage='en';const enLabel=battlefield.api.cityLabel(state.cells[cell].name,cell).getAttribute('aria-label');settings.cityLanguage='zh';
        return {site:state.cells[cell],translation:manifest.maps[state.map].cityNames[cell],
          zhLabel,enLabel,glyphs:Ew4OriginalUI.availableText('高知')==='高知'};
      },shikoku.cell);
      check(city.site.name==='高知'&&city.site.buildingKind==='city','Shikoku has the Kochi city');
      check(city.site.active&&!city.site.sea&&city.site.geographyType==='none','Kochi is on ordinary accessible land');
      check(city.translation.zh==='高知'&&city.translation.en==='Kochi'&&city.zhLabel==='高知'&&city.enLabel==='Kochi'&&city.glyphs,'city has bilingual labels and Chinese glyphs');
      await page.screenshot({path:path.join(folder,`${name}-${backend}-shikoku.png`)});
      report.cases[report.cases.length-1].shikoku=city;
      await page.close();
    }
    check(report.errors.length===0,'no browser errors');check(report.missing.length===0,'no missing assets');
    report.previewMod=originalStatus.active;
  }finally{await fs.writeFile(path.join(folder,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,cases:report.cases.length,errors:report.errors.length,missing:report.missing.length}));
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
