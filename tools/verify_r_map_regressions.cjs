'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2]||'http://127.0.0.1:5575',folder=path.resolve(process.argv[3]||'build/r-map-fix-20261026/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,cases:[],errors:[],missing:[]};
  const check=(value,message)=>{assert.ok(value,message);report.checks++;};
  try{
    for(const [name,width,height] of [['desktop',1280,720],['phone',844,390],['portrait',390,844]])for(const backend of ['canvas','pixi']){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:2});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest7','fra'));await page.waitForFunction(()=>ctx&&[...images.values()].every(i=>i.complete));
      await page.evaluate(async()=>{
        stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();suspendRendering();cameraInputUntil=0;
        await Ew4OriginalUI.prepareMapText('岘港北京',true);
      });
      check(await page.evaluate(()=>!!ctx.gpu)===(backend==='pixi'),'actual backend '+backend);
      const labels=await page.evaluate(async()=>{
        const results=[];
        for(const language of ['zh','en'])for(const city of ['北京','岘港'])for(const relative of [1,.5,.28]){
          settings.cityLanguage=language;battlefield.cityLabels.clear();battlefield.pendingCityLabels?.clear();
          const cell=state.cells.findIndex(c=>c.name===city),p=point(cell),detail=Math.max(1,Math.min(innerWidth/568,innerHeight/320,3));
          Object.assign(view,{x:p.x,y:p.y,zoom:detail*relative});clampView();render(2000);suspendRendering();
          const density=Math.max(1,Math.min(4,Math.ceil(ctx.getTransform().a*Math.max(1,battlefield.markerScale)/2)));
          const key=cell+':'+city+':'+density;
          if(!battlefield.cityLabels.has(key))battlefield.cacheCityLabel(key,city,cell,density);
          const target=battlefield.cityLabels.get(key),old=ctx.drawImage;let rect;
          ctx.drawImage=function(source,x,y,w,h){if(source===target)rect={x,y,width:w,height:h};};
          try{battlefield.labels();}finally{ctx.drawImage=old;}
          render(2000);suspendRendering();
          const bounds=Ew4Battlefield.cameraBounds(state,manifest.maps[state.map]);
          const top=(rect.y-battlefield.visibleRect.top)*view.zoom,bottom=top+rect.height*view.zoom;
          results.push({city,language,relative,zoom:view.zoom,anchor:rect.y-p.y,rect,bounds,screen:{top,bottom,height:canvas.clientHeight}});
        }
        return results;
      });
      for(const item of labels){
        check(item.rect.x>=item.bounds.left-.01&&item.rect.x+item.rect.width<=item.bounds.left+item.bounds.width+.01,'label horizontal bounds');
        check(item.rect.y>=item.bounds.top-.01&&item.rect.y+item.rect.height<=item.bounds.top+item.bounds.height+.01,'label vertical bounds');
        if(item.city==='北京')check(Math.abs(item.anchor-29)<.01,'stable city anchor at '+item.relative);
        else check(item.screen.top>=0&&item.screen.bottom<=item.screen.height,'Da Nang stays visible');
      }
      const screenshot=await page.screenshot({path:path.join(folder,`${name}-${backend}-danang.png`)}),png=PNG.sync.read(screenshot),colors=new Set();
      for(let i=0;i<png.data.length;i+=4*67)colors.add(png.data.subarray(i,i+3).toString('hex'));
      check(colors.size>100,'nonblank map '+name);
      const movement=await page.evaluate(async()=>{
        const sites=state.cells.map((c,i)=>({c,i})).filter(({c})=>c.buildingKind==='industry');
        const unit=state.units.find(u=>u.owner===state.playerFaction),choice=await rpc('action',{op:'select',cell:unit.cell,revision:state.revision});
        const destination=choice.range.find(i=>i!==unit.cell),next=await rpc('action',{op:'move',unit:unit.id,cell:destination,revision:state.revision});
        await installState(next);motions.clear();effects.clear();
        const missing=()=>sites.filter(({c,i})=>state.cells[i].buildingKind!==c.buildingKind||state.cells[i].buildingLevel!==c.buildingLevel).map(s=>s.i);
        const after=missing();await rpc('save',{slot:2,mode:2});await installState(await rpc('load',{slot:2,mode:2}),true);
        stageIntroOpen=false;$('detail-modal').hidden=true;
        Object.assign(view,{x:5950,y:3350,zoom:1.3});render(2000);suspendRendering();
        return {count:sites.length,destination,after,reloaded:missing(),hebei:[9728,9733,10206].map(i=>state.cells[i].buildingKind)};
      });
      check(movement.count===62&&!movement.after.length&&!movement.reloaded.length,'all factories survive movement and reload');
      check(movement.hebei.every(k=>k==='industry'),'Hebei factories survive');
      await page.screenshot({path:path.join(folder,`${name}-${backend}-hebei.png`)});
      let eviction;
      if(backend==='pixi'){
        eviction=await page.evaluate(()=>{
          render(2000);suspendRendering();
          const gl=ctx.renderer.gl,read=()=>{const pixels=new Uint8Array(ctx.renderer.width*ctx.renderer.height*4);gl.readPixels(0,0,ctx.renderer.width,ctx.renderer.height,gl.RGBA,gl.UNSIGNED_BYTE,pixels);return pixels;};
          const before=read(),buildingAtlas=image('images/buildings'+(manifest.atlases.buildings_hd?.['factory_east_lv2.png']?'_hd':'')+'.png');
          const resident=ctx.sources.has(buildingAtlas);ctx.release(buildingAtlas);render(2000);suspendRendering();
          const after=read();let changed=0;for(let i=0;i<before.length;i++)if(before[i]!==after[i])changed++;
          const actual={resident,restored:ctx.sources.has(buildingAtlas),changed,bytes:before.length};
          const surface=document.createElement('canvas'),draw=new Ew4Pixi.Context(surface),source=document.createElement('canvas');
          source.width=source.height=20;const paint=source.getContext('2d');paint.fillStyle='#ff0000';paint.fillRect(0,0,20,20);
          const rect={left:0,top:0,width:100,height:100};let paints=0;
          const frame=()=>{draw.begin(100,100);draw.cachedLayer('factory',rect,()=>{paints++;draw.drawImage(source,0,0);});draw.end();};
          frame();frame();const reused=paints===1;draw.release(source);frame();
          const restored=draw.sources.has(source),pixels=draw.renderer.extract.pixels(draw.stage),red=pixels[0]===255&&pixels[1]===0&&pixels[2]===0;
          const outcome={reused,restored,red,paints,actual};draw.renderer.destroy(true);return outcome;
        });
        check(eviction.reused&&eviction.restored&&eviction.red&&eviction.paints===2,'released cached texture rebuilt');
        check(eviction.actual.resident&&eviction.actual.restored&&eviction.actual.changed===0,'actual Hebei factory atlas rebuilt without pixel changes');
      }
      report.cases.push({name,backend,labels:labels.length,movement,eviction});
      await page.close();
    }
    check(!report.errors.length,'no browser errors');check(!report.missing.length,'no missing resources');
  }finally{await fs.writeFile(path.join(folder,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
