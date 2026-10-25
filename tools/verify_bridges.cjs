'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5507',folder=path.resolve(process.argv[3]||'build/conquest-bridges-20261024/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const conquests=JSON.parse(await fs.readFile('build/conquest-bridges-20261024/inventory.json','utf8')).conquests;
  const campaigns=JSON.parse(await fs.readFile('build/campaign-bridges-20261024/inventory.json','utf8')).campaigns;
  const runtime=JSON.parse(await fs.readFile('build/conquest-bridges-20261024/runtime-checks.json','utf8')).scenarios;
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,cases:[],errors:[],missing:[],scope:'Isolated desktop browser; Canvas and Pixi at desktop/phone viewports; no physical Android execution'};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  try{
    for(const [viewport,width,height] of [['desktop',1280,720],['phone',844,390]])for(const backend of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:2,hasTouch:true});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      const fixtures=await page.evaluate(inventory=>inventory.map(test=>{
        const calls=[],renderer=new Ew4Battlefield.Renderer({sprite:(atlas,image,x,y)=>calls.push({image,x,y})});
        renderer.visibleCells=test.bridgeSites.map(b=>({site:{active:b.active,bridgeEdges:b.styles.reduce((a,b)=>a|b,0),bridgeStyle0:b.styles[0],bridgeStyle1:b.styles[1],bridgeStyle2:b.styles[2]},p:Ew4Battlefield.point(b.cell,test.width)}));
        renderer.bridges();return {id:test.id,draws:calls.length};
      }),[...conquests,...campaigns]);
      fixtures.forEach((result,i)=>check(result.draws===[...conquests,...campaigns][i].uniqueBridgeEdges,'all bridge edges drawn once '+result.id));
      for(const test of [...conquests,campaigns.find(c=>c.id==='campaign2_01')]){
        const country=runtime.find(r=>r.scenario===test.id)?.country;
        await page.evaluate(({id,country})=>start(id,country),{id:test.id,country});
        await page.waitForFunction(()=>ctx&&(!ctx.stats||ctx.stats.frames>0)&&[...images.values()].every(i=>i.complete));
        await page.evaluate(()=>{stageIntroOpen=false;$('detail-modal').hidden=true;});
        const bridge=test.bridgeSites.find(b=>b.active&&b.styles.some(mask=>mask&7));
        for(const relativeZoom of [1,.38]){
          const result=await page.evaluate(({cell,relativeZoom})=>{
            suspendRendering();
            const p=point(cell),scale=Math.max(1,Math.min(innerWidth/568,innerHeight/320,3));
            Object.assign(view,p,{zoom:scale*relativeZoom});clampView();
            const calls=[],original= battlefield.api.sprite;
            battlefield.api.sprite=function(atlas,image,x,y,...rest){if(image.startsWith('bridge_'))calls.push({image,x,y});return original(atlas,image,x,y,...rest);};
            battlefield.invalidateGround();const time=performance.now();render(time);suspendRendering();battlefield.api.sprite=original;
            const pixels=()=>ctx.renderer?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;
            const a=pixels(),saved=state,signature=battlefield.groundSignature;
            state={...state,baseRevision:state.revision,changedCells:[cell],cells:state.cells.map((site,i)=>i===cell?{...site,bridgeEdges:0}:site)};
            render(time);suspendRendering();const b=pixels();
            let changed=0;
            for(let i=0;i<a.length;i+=4)if(a[i]!==b[i]||a[i+1]!==b[i+1]||a[i+2]!==b[i+2])changed++;
            const invalidated=battlefield.groundSignature>signature;
            state=saved;render(time);suspendRendering();
            return {cell,relativeZoom,changed,invalidated,targetCalls:calls.filter(c=>c.x===p.x&&c.y===p.y),gpu:!!ctx.renderer,
              screen:{x:(p.x-view.x)*view.zoom+viewport.w/2,y:(p.y-view.y)*view.zoom+viewport.h/2},width:viewport.w,height:viewport.h};
          },{cell:bridge.cell,relativeZoom});
          check(result.gpu===(backend==='pixi'),'requested backend');
          check(result.targetCalls.length>0,'bridge sprite submitted '+test.id);
          check(result.changed>5,'bridge contributes visible pixels '+test.id);
          check(result.invalidated,'bridge removal invalidates cached ground '+test.id);
          check(result.screen.x>=0&&result.screen.x<result.width&&result.screen.y>=0&&result.screen.y<result.height,'bridge in viewport '+test.id);
          report.cases.push({scenario:test.id,viewport,backend,...result});
          if(relativeZoom===1)await page.screenshot({path:path.join(folder,`${viewport}-${backend}-${test.id}.png`)});
        }
      }
      await page.close();
    }
    check(report.errors.length===0,'no browser errors');check(report.missing.length===0,'no missing assets');
  }finally{
    await fs.writeFile(path.join(folder,'checks.json'),JSON.stringify(report,null,2));await browser.close();
  }
  console.log(JSON.stringify({checks:report.checks,cases:report.cases.length,errors:report.errors.length,missing:report.missing.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
