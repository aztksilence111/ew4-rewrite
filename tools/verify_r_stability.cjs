'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2],out=path.resolve(process.argv[3]),baseline=process.argv.includes('--baseline'),source=process.argv.includes('--source'),contextOnly=process.argv.includes('--context-only');
const refIndex=process.argv.indexOf('--baseline-ref'),baselineRef=refIndex>=0?process.argv[refIndex+1]:null;
if(refIndex>=0&&!baselineRef)throw Error('--baseline-ref requires a Git revision');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,cases:[],errors:[],missing:[]},check=(v,m)=>{assert.ok(v,m);report.checks++;};
  try{
    for(const [name,width,height,dpr] of (baseline||contextOnly?[['desktop',1280,720,2]]:[['desktop',1280,720,2],['phone',844,390,3],['portrait',390,844,2]])){
      for(const backend of (baseline||contextOnly?['pixi']:['pixi','canvas'])){
        const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:dpr});
        page.on('pageerror',e=>report.errors.push(e.message));
        page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
        if(source||baselineRef)await page.route(/\/(?:compat\/)?app\.js(?:\?|$)/,async route=>{
          const body=baselineRef?require('node:child_process').execFileSync('git',['show',baselineRef+':rewrite/Ew4.Web/wwwroot/app.js'],{cwd:path.join(__dirname,'..'),encoding:'utf8'})
            :await fs.readFile(path.join(__dirname,'../rewrite/Ew4.Web/wwwroot/app.js'),'utf8');
          await route.fulfill({contentType:'text/javascript',body});
        });
        await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));
        await page.waitForFunction(()=>startupReady,null,{timeout:90000});
        if(name==='phone')await page.evaluate(()=>{PIXI.settings.PREFER_ENV=PIXI.ENV.WEBGL_LEGACY;});
        await page.evaluate(()=>start('conquest1','fra'));
        await page.waitForFunction(()=>ctx&&!busy&&!stageIntroOpen);
        if(source)check(await page.evaluate(()=>render.toString().includes('battlefieldNeedsFrame')),'load patched application source');
        await page.evaluate(()=>{
          closeDetail();window.stabilitySample=()=>{
            const pixels=ctx.gpu?(()=>{const gl=ctx.renderer.gl,p=new Uint8Array(canvas.width*canvas.height*4);
              gl.readPixels(0,0,canvas.width,canvas.height,gl.RGBA,gl.UNSIGNED_BYTE,p);return p;})():ctx.getImageData(0,0,canvas.width,canvas.height).data;
            const colors=new Set();let opaque=0;
            for(let i=0;i<pixels.length;i+=4*397){colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);if(pixels[i+3])opaque++;}
            return {colors:colors.size,opaque};
          };
          window.stabilitySubmitted=0;const end=ctx.end.bind(ctx);
          ctx.end=()=>{end();stabilitySubmitted++;if(window.stabilityCaptureNext){window.stabilityCaptureNext=false;window.stabilityLastPixels=stabilitySample();}};
        });
        const item={name,backend,pans:[],turns:[],recoveries:[]};report.cases.push(item);
        check(await page.evaluate(()=>!!ctx.gpu)===(backend==='pixi'),'actual backend');
        if(!baseline&&!contextOnly){
          item.pans=await page.evaluate(()=>{
            const origin={...view},samples=[];
            for(let i=0;i<36;i++){
              view={x:origin.x+Math.sin(i*.7)*3500,y:origin.y+Math.cos(i*.9)*2500,zoom:[.16,.48,1.5,2.5][i%4]};
              clampView();render(performance.now(),true);suspendRendering();samples.push(stabilitySample());
            }
            view=origin;render(performance.now(),true);suspendRendering();return samples;
          });
          for(const sample of item.pans)check(sample.colors>40&&sample.opaque>100,'pan and zoom display actual map pixels');
          for(const fast of [true,false]){
            const turn=await page.evaluate(async fast=>{
              closeDetail();settings.autoTurnFast=fast;settings.speed=4;
              const begin=performance.now(),round=state.round;await act('end');
              render(performance.now(),true);suspendRendering();
              return {fast,round:state.round,previous:round,active:state.activeFaction,player:state.playerFaction,
                busy,aiRunning,dialog:turnUi.open,pixels:stabilitySample(),ms:performance.now()-begin};
            },fast);
            check(turn.round===turn.previous+1&&turn.active===turn.player&&!turn.busy&&!turn.aiRunning,'complete round returns to player');
            check(turn.pixels.colors>40&&turn.pixels.opaque>100,'turn entry has real map pixels');item.turns.push(turn);
          }
          const priorView=await page.evaluate(()=>{closeDetail();return {...view};});
          await page.mouse.move(width*.45,height*.5);await page.mouse.down();
          await page.mouse.move(width*.65,height*.65,{steps:10});await page.mouse.up();
          await page.mouse.wheel(0,200);
          const input=await page.evaluate(prior=>{render(performance.now(),true);suspendRendering();return {
            changed:view.x!==prior.x||view.y!==prior.y,zoomed:view.zoom!==prior.zoom,pixels:stabilitySample()};},priorView);
          check(input.changed&&input.zoomed&&input.pixels.colors>40,'real pointer drag and wheel retain map');item.input=input;
          await page.evaluate(()=>{
            $('pause').hidden=false;render(performance.now(),true);suspendRendering();ew4SetAppVisible(false);
            if(ctx.gpu)ctx.renderer.clear();else{ctx.save();ctx.resetTransform();ctx.clearRect(0,0,canvas.width,canvas.height);ctx.restore();}
            window.stabilityBeforeResume=stabilitySubmitted;window.stabilityCaptureNext=true;ew4SetAppVisible(true);
          });
          await page.waitForTimeout(200);
          item.resume=await page.evaluate(()=>({frames:stabilitySubmitted-stabilityBeforeResume,pixels:window.stabilityLastPixels}));
          check(item.resume.frames>0&&item.resume.pixels?.colors>40,'foreground resume repaints discarded surface under pause');
        }
        if(backend==='pixi'){
          for(const cover of (baseline?['round']:['none','round','pause'])){
            await page.evaluate(async cover=>{
              closeDetail();$('pause').hidden=true;
              if(cover==='round'){
                state={...state,round:Math.max(2,state.round),factions:state.factions.map(f=>f.id===state.playerFaction?{...f,
                  turnSummary:{round:Math.max(2,state.round),income:{money:1,industry:1,food:1},foodConsumption:1}}:f)};
                turnUi.seen.clear();turnUi.observe(state);turnUi.tick();
              }
              if(cover==='pause')$('pause').hidden=false;
              render(performance.now(),true);suspendRendering();
              window.stabilityExtension=ctx.renderer.gl.getExtension('WEBGL_lose_context');
              if(!stabilityExtension)throw Error('Context loss extension unavailable');
              window.stabilityRestores=ctx.stats.contextRestores;
              window.stabilityFrames=ctx.stats.frames;
              const lost=new Promise(resolve=>canvas.addEventListener('webglcontextlost',()=>resolve(),{once:true}));
              stabilityExtension.loseContext();
              await lost;
            },cover);
            await page.waitForTimeout(160);
            await page.evaluate(()=>{if(ctx.lost)stabilityExtension.restoreContext();});
            await page.waitForFunction(()=>!ctx.lost&&ctx.stats.contextRestores>stabilityRestores);
            if(!baseline)await page.waitForFunction(()=>ctx.stats.frames>stabilityFrames,null,{timeout:15000});
            await page.waitForTimeout(220);
            const recovery=await page.evaluate(()=>({frames:ctx.stats.frames-stabilityFrames,lost:ctx.renderer.gl.isContextLost(),
              roundOpen:turnUi.open,pause:!$('pause').hidden}));
            const shot=await page.screenshot({path:path.join(out,name+'-'+cover+'.png')}),png=PNG.sync.read(shot),colors=new Set();
            // Stay below the HUD, above side controls, and left of the centered dialog.
            for(let y=Math.floor(png.height*.11);y<Math.floor(png.height*.17);y+=3)for(let x=Math.floor(png.width*.02);x<Math.floor(png.width*.12);x+=3){
              const i=(y*png.width+x)*4;colors.add(png.data[i]+','+png.data[i+1]+','+png.data[i+2]);
            }
            recovery.cornerColors=colors.size;item.recoveries.push({cover,...recovery});
            if(baseline)check(recovery.frames===0&&recovery.cornerColors<5,'reproduce blank map under turn dialog after GPU restore');
            else{check(recovery.frames>0&&!recovery.lost,'context restoration submits a new displayed frame under '+cover);check(colors.size>20,'restored visible map pixels under '+cover);}
          }
        }
        await page.evaluate(()=>{closeDetail();$('pause').hidden=true;requestRender();});
        await page.screenshot({path:path.join(out,name+'-'+backend+'-final.png')});
        await page.close();
      }
    }
    check(!report.errors.length,'no script exceptions');check(!report.missing.length,'no missing resources');
  }finally{await fs.writeFile(path.join(out,'results.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors,missing:report.missing,cases:report.cases.map(c=>({name:c.name,backend:c.backend,pans:c.pans.length,turns:c.turns.length,recoveries:c.recoveries}))}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
