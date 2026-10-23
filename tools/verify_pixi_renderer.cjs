'use strict';
const fs=require('node:fs/promises'),assert=require('node:assert/strict');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5475',folder=process.argv[3]||'build/pixi-performance-20261023/browser-final';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const report={cases:[],errors:[]};
  try{
    for(const [name,width,height,dpr] of [['desktop',1280,720,2],['mobile',844,390,2],['portrait',390,844,2]]){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:dpr});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.errors.push(r.status()+' '+r.url());});
      page.on('request',r=>{if(!r.url().startsWith(base)&&!r.url().startsWith('data:'))report.errors.push('External request: '+r.url());});
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      if(name==='mobile')await page.evaluate(()=>{PIXI.settings.PREFER_ENV=PIXI.ENV.WEBGL_LEGACY;});
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>ctx?.stats.frames>0&&[...images.values()].every(i=>i.complete));
      const tint=await page.evaluate(async()=>{
        await loadImage('images/eff.png');
        const [name,s]=Object.entries(manifest.atlases.eff).find(([,s])=>s.w>=8&&s.w<=64&&s.h>=8&&s.h<=64),base=effects.particleAtlas();
        const source=effects.texture(name,[128,208,32,255]),surface=document.createElement('canvas'),reference=document.createElement('canvas');
        reference.width=source.width;reference.height=source.height;
        const gpu=new Ew4Pixi.Context(surface),cpu=reference.getContext('2d');let maxMean=0;
        for(const blend of ['source-over','lighter']){
          gpu.begin(source.width,source.height);gpu.fillStyle='#688e91';gpu.fillRect(0,0,source.width,source.height);
          gpu.tint=0x80d020;gpu.globalAlpha=.37;gpu.globalCompositeOperation=blend;gpu.drawImage(base,s.x,s.y,s.w,s.h,0,0,source.width,source.height);gpu.end();
          cpu.globalCompositeOperation='source-over';cpu.globalAlpha=1;cpu.fillStyle='#688e91';cpu.fillRect(0,0,source.width,source.height);cpu.globalAlpha=.37;cpu.globalCompositeOperation=blend;cpu.drawImage(source,0,0);
          const a=gpu.renderer.extract.pixels(),b=cpu.getImageData(0,0,source.width,source.height).data;let sum=0;for(let i=0;i<a.length;i++)sum+=Math.abs(a[i]-b[i]);maxMean=Math.max(maxMean,sum/a.length);
        }
        gpu.renderer.destroy(true);return {name,maxMean};
      });
      assert(tint.maxMean<3,'GPU particle tint/blending must match Canvas material generation');report.cases.push({name,tint});
      await page.evaluate(()=>{requestRender();});await page.waitForTimeout(150);
      for(const [mode,zoomValue] of [['near',1.5],['far',.48]]){
        const result=await page.evaluate(async zoomValue=>{
          view.zoom=zoomValue;clampView();requestRender();await new Promise(requestAnimationFrame);suspendRendering();
          const samples=[],origin={...view},before=battlefield.staticBuilds||0;
          for(let i=0;i<50;i++){
            const time=await new Promise(requestAnimationFrame);view.x=origin.x+Math.sin(i/49*Math.PI)*220;view.y=origin.y+Math.sin(i/49*Math.PI*2)*80;clampView();
            renderDirty=true;const started=performance.now();render(time);samples.push(performance.now()-started);suspendRendering();
          }
          const pixels=ctx.renderer.extract.pixels(),colors=new Set();let nonzero=0;
          for(let i=0;i<pixels.length;i+=4*97){if(pixels[i]||pixels[i+1]||pixels[i+2])nonzero++;colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);}
          samples.sort((a,b)=>a-b);
          return {pixi:PIXI.VERSION,webgl:ctx.renderer.context.webGLVersion,frames:ctx.stats.frames,colors:colors.size,nonzero,badgeOnly:battlefield.badgeOnly,visibleUnits:battlefield.visibleUnits.length,chunks:battlefield.chunks.size,chunkPixels:battlefield.chunkPixels,sourcePixels:ctx.sourcePixels,builds:(battlefield.staticBuilds||0)-before,p95:samples[Math.floor(samples.length*.95)],max:samples.at(-1)};
        },zoomValue);
        assert(result.colors>40&&result.nonzero>100,'Battlefield must contain rendered assets');assert.equal(result.badgeOnly,mode==='far');
        if(name==='mobile')assert.equal(result.webgl,1,'WebGL 1 compatibility');
        assert(result.chunkPixels<=12*1024*1024,'Chunk cache budget');
        report.cases.push({name,mode,...result});await page.screenshot({path:folder+'/'+name+'-'+mode+'.png'});
      }
      if(name==='desktop'){
        const actions=await page.evaluate(async()=>{
          const mobile=state.units.find(u=>u.owner===state.playerFaction&&u.canNavigateMove);
          const selectionView=await rpc('action',{op:'select',cell:mobile.cell,revision:state.revision});
          const destination=selectionView.range.find(cell=>cell!==mobile.cell),p=point(mobile.cell);view.x=p.x;view.y=p.y;view.zoom=1.5;
          const moved=await rpc('action',{op:'move',unit:mobile.id,cell:destination,revision:state.revision});
          const route=moved.movements.find(m=>m.unit===mobile.id);await installState(moved);
          let attack=null;
          const attacker=state.units.find(u=>u.owner===state.playerFaction&&u.canAttack);
          if(attacker){const targets=await rpc('action',{op:'select',cell:attacker.cell,revision:state.revision});
            const target=state.units.find(u=>u.cell===targets.targets[0]);if(target){const result=await rpc('action',{op:'attack',unit:attacker.id,target:target.id,revision:state.revision});attack=!!result.combat;await installState(result);}}
          return {route:!!route&&route.path[0]===mobile.cell&&route.path.at(-1)===destination,moved:state.units.find(u=>u.id===mobile.id)?.cell===destination,attack};
        });
        assert(actions.route&&actions.moved);report.cases.push({name,actions});
        await page.screenshot({path:folder+'/desktop-action.png'});
      }
      // Exercise ordinary deltas, deletion/addition reconstruction and full resync.
      const delta=await page.evaluate(async()=>{
        const before=state,raw=await (await fetch('/api/action?q='+encodeURIComponent(JSON.stringify({op:'end',revision:state.revision,delta:true})),{method:'POST'})).json();
        if(!raw.ok)throw Error(raw.message);
        const d=raw.data;state=before;
        const full=await rpc('action',{op:'state'});await installState(full);
        return {base:d.baseRevision,expected:before.revision,cells:d.cells,units:d.units,cellUpdates:d.cellUpdates.length,unitUpdates:d.unitUpdates.length,removed:d.removedUnits.length,fullCells:full.cells.length,fullUnits:full.units.length};
      });
      assert.equal(delta.base,delta.expected);assert.equal(delta.cells,null);assert.equal(delta.units,null);assert(delta.fullCells>5000);report.cases.push({name,delta});
      const recovery=await page.evaluate(async()=>{
        const gl=ctx.renderer.gl,extension=gl.getExtension('WEBGL_lose_context');if(!extension)return {supported:false};
        const losses=ctx.stats.contextLosses,restores=ctx.stats.contextRestores;
        extension.loseContext();await new Promise(resolve=>setTimeout(resolve,150));const lost=ctx.stats.contextLosses>losses;extension.restoreContext();
        for(let i=0;i<40&&ctx.lost;i++)await new Promise(resolve=>setTimeout(resolve,50));requestRender();await new Promise(requestAnimationFrame);
        const pixels=ctx.renderer.extract.pixels(),colors=new Set();for(let i=0;i<pixels.length;i+=4*397)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);
        return {supported:true,lost,restored:!ctx.lost&&ctx.stats.contextRestores>restores,contextLost:gl.isContextLost(),colors:colors.size};
      });
      if(recovery.supported){report.cases.push({name,recovery});assert(recovery.lost&&recovery.restored&&!recovery.contextLost&&recovery.colors>40,JSON.stringify(recovery));}
      if(name==='desktop'){
        await page.evaluate(async()=>{
          await start('campaign1_02b',null);settings.speed=1;await selectCell(2544,true);
          const attacker=selected.id,defender=state.units.find(u=>u.cell===selection.targets[0]).id;
          const next=await rpc('action',{op:'attack',unit:attacker,target:defender,revision:state.revision});
          if(!next.combat)throw Error('Campaign fixture did not produce combat');
          window.pixiCombatResult={attacker,defender,counterattack:next.combat.counterattack,frames:0,particles:0,damage:0};
          const original=battlefield.frame;
          battlefield.frame=function(...args){const p=window.pixiCombatResult;p.frames++;p.particles=Math.max(p.particles,effects.instances.length);p.damage=Math.max(p.damage,damageNumbers.instances.length);return original.apply(this,args);};
          // The final impact requests a frame that can run after installState resolves.
          window.pixiCombatPromise=installState(next).then(()=>new Promise(requestAnimationFrame)).finally(()=>{battlefield.frame=original;});requestRender();
        });
        await page.waitForFunction(()=>damageNumbers.instances.length>0,null,{timeout:20000});
        await page.screenshot({path:folder+'/desktop-combat.png'});
        const combat=await page.evaluate(async()=>{await window.pixiCombatPromise;return window.pixiCombatResult;});
        assert(combat.frames>10&&combat.damage>0&&combat.particles>0,JSON.stringify(combat));report.cases.push({name,combat});
      }
      await page.close();
    }
    assert.deepEqual(report.errors,[]);
  }finally{await fs.writeFile(folder+'/results.json',JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report,null,2));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
