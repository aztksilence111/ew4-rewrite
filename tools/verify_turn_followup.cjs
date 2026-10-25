'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),vm=require('node:vm'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],folder=path.resolve(process.argv[3]||'build/turn-regression-followup-20261025/browser');
const frozen=path.resolve('build/turn-regression-followup-20261025/frontend-before');
const ordinaryOnly=process.argv.includes('--ordinary-only');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],cases:[],scope:'Desktop Edge, original-resolution Canvas/Pixi, emulated desktop/phone viewports; no physical phone timing'};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const source=await fs.readFile('rewrite/Ew4.Web/wwwroot/map-views.js','utf8'),sandbox={};vm.runInNewContext(source,sandbox);
  const projection=new sandbox.Ew4MapViews.Projection(),map={},world={scenario:'test',factions:[{id:'a',camp:'one',color:11,country:'fra'},{id:'b',camp:'two',color:22,country:'gbr'}]};
  check(projection.sync(world,map),'initial projection');const index=projection.factions;
  check(!projection.sync({...world,factions:world.factions.map(f=>({...f,resources:{money:44}}))},map)&&projection.factions===index,'resources-only updates reuse projection');
  check(!projection.sync({...world,factions:[...world.factions].reverse()},map),'reordering preserves projection');
  check(projection.sync({...world,factions:[{...world.factions[0],camp:'two'},world.factions[1]]},map),'camp reassignment invalidates projection');
  check(projection.borderOwner({owner:'a'},'camp')==='two','new camp used');
  check(projection.sync({...world,factions:[world.factions[1]]},map)&&projection.color({owner:'a'},'normal')===0,'removed faction invalidates projection');
  check(projection.sync(world,{}),'map identity resets projection');
  world.factions[0].color=99;
  check(projection.sync(world,projection.map)&&projection.color({owner:'a'},'normal')===99,'in-place appearance change invalidates projection');
  world.factions[0].camp='two';
  check(projection.sync(world,projection.map)&&projection.borderOwner({owner:'a'},'camp')==='two','in-place camp change invalidates projection');
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const backend of ordinaryOnly?[]:['canvas','pixi'])for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844]]){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(()=>start('conquest1','fra'));await page.waitForFunction(()=>state&&ctx&&[...images.values()].every(i=>i.complete));
      await page.evaluate(()=>{stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();damageNumbers.clear();suspendRendering();});
      const result=await page.evaluate(async()=>{
        const digest=async()=>{const data=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',data))).join(',');};
        const capture=async()=>{render(2000);suspendRendering();return digest();},original=state,images=[];
        for(const mode of ['normal','camp','district']){
          mapViews.select(mode);const first=await capture();
          const saved=state;state={...saved,factions:saved.factions.map(f=>({...f,resources:{...f.resources,money:f.resources.money+1}}))};
          const signature=battlefield.groundSignature,second=await capture();
          images.push({mode,same:first===second,noRebuild:signature===battlefield.groundSignature});state=saved;
        }
        state=original;mapViews.select('normal');await capture();
        const bench=[];
        for(let i=0;i<12;i++){
          const start=performance.now();state={...state,factions:state.factions.map(f=>({...f}))};render(2000);suspendRendering();bench.push(performance.now()-start);
        }
        return{images,bench,units:state.units.length,cells:state.cells.length};
      });
      for(const image of result.images){check(image.same,backend+' '+name+' '+image.mode+' identical pixels after resource-only state');check(image.noRebuild,backend+' '+name+' '+image.mode+' ground cache retained');}
      await page.screenshot({path:path.join(folder,backend+'-'+name+'.png')});
      // Isolated page probes only; production files contain no timing wrappers.
      const turn=await page.evaluate(async()=>{
        const counts={},times={},fetchOriginal=fetch,installOriginal=installState,groundOriginal=battlefield.groundLayers;
        const count=name=>counts[name]=(counts[name]||0)+1;
        window.fetch=async(...args)=>{const url=String(args[0]),api=url.includes('/api/'),started=performance.now();if(api)count(url.split('/api/')[1].split('?')[0]);const response=await fetchOriginal(...args);if(api){times.rpcHeaders=(times.rpcHeaders||0)+performance.now()-started;const json=response.json.bind(response);response.json=async()=>{const start=performance.now();const value=await json();times.responseJson=(times.responseJson||0)+performance.now()-start;return value;};}return response;};
        installState=async(...args)=>{const start=performance.now();count('install');try{return await installOriginal(...args);}finally{times.install=(times.install||0)+performance.now()-start;}};
        battlefield.groundLayers=function(...args){count('ground');counts.groundCells=(counts.groundCells||0)+this.visibleCells.length;return groundOriginal.apply(this,args);};
        try{settings.autoTurnFast=true;stageIntroOpen=false;await act('end');return{counts,times,record:performanceLog.records.at(-1),memory:performance.memory?{used:performance.memory.usedJSHeapSize,total:performance.memory.totalJSHeapSize}:null};}
        finally{window.fetch=fetchOriginal;installState=installOriginal;battlefield.groundLayers=groundOriginal;}
      });
      check(!turn.counts['pause-objectives']&&!turn.counts.objectives,backend+' '+name+' no closed-panel objective requests');
      check(turn.record.kind==='turn'&&turn.record.status==='returned',backend+' '+name+' fast turn returned to player');
      report.cases.push({backend,name,...result,turn});await page.close();
    }
    // Actual repeated state preparation with before/after scripts and identical content.
    for(const variant of ordinaryOnly?[]:['before','after','after','before']){
      const page=await browser.newPage({viewport:{width:1280,height:720}});
      if(variant==='before')await page.route(/\/(map-views|battlefield)\.js(?:\?|$)/,async route=>{
        const file=new URL(route.request().url()).pathname.split('/').pop();await route.fulfill({contentType:'text/javascript',body:await fs.readFile(path.join(frozen,file),'utf8')});
      });
      await page.goto(base+'/?renderer=canvas');await page.waitForFunction(()=>startupReady);await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>[...images.values()].every(i=>i.complete));
      const measurements=await page.evaluate(()=>{
        suspendRendering();effects.clear();stageIntroOpen=false;selected=null;selection=null;
        render(2000);suspendRendering();const times=[];
        for(let i=0;i<80;i++){state={...state,factions:state.factions.map(f=>({...f}))};const start=performance.now();render(2000);suspendRendering();if(i>=10)times.push(performance.now()-start);}
        return times;
      });
      (report.statePreparation??=[]).push({variant,measurements});await page.close();
    }
    for(const backend of ['canvas','pixi']){
      const page=await browser.newPage({viewport:{width:1280,height:720}});
      page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest1','fra'));await page.waitForFunction(()=>[...images.values()].every(i=>i.complete));
      const ordinary=await page.evaluate(async()=>{
        stageIntroOpen=false;$('detail-modal').hidden=true;settings.autoTurnFast=false;effects.clear();
        const result={steps:0,requests:0,installMs:0,rpcMs:0,groundBuilds:0,groundCells:0},original=battlefield.groundLayers;
        battlefield.groundLayers=function(...args){result.groundBuilds++;result.groundCells+=this.visibleCells.length;return original.apply(this,args);};
        try{
          for(let i=0;i<13;i++){
            if(i>0&&!aiCanContinue(state))break;
            const start=performance.now(),next=await rpc('action',{op:i===0?'end':'ai',revision:state.revision});
            result.rpcMs+=performance.now()-start;result.requests++;
            const installed=performance.now();await installState(next);result.installMs+=performance.now()-installed;
            render(2000+i*200);suspendRendering();if(i>0)result.steps++;
          }
          const full=await rpc('action',{op:'state'});
          result.matchesFull=JSON.stringify(full.cells)===JSON.stringify(state.cells)&&JSON.stringify(full.units)===JSON.stringify(state.units);
          return result;
        }finally{battlefield.groundLayers=original;}
      });
      check(ordinary.steps>0&&ordinary.matchesFull,backend+' ordinary AI sample reconstructs full state');
      (report.ordinary??=[]).push({backend,...ordinary});await page.close();
    }
    check(!report.errors.length&&!report.missing.length,'no browser exceptions or missing resources');
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,cases:report.cases.length,errors:report.errors.length,missing:report.missing.length}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
