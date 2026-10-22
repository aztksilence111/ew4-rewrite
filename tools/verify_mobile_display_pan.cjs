'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),cp=require('node:child_process'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5353',folder=process.argv[3]||'rewrite/reports/mobile-display-pan-20261022';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:844,height:390},deviceScaleFactor:3,hasTouch:true});
  const report={checks:0,errors:[],missing:[],scope:'Edge at mobile/desktop sizes; no Android device execution'};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady);
    await page.evaluate(()=>start('conquest1','fra'));await page.waitForTimeout(400);
    report.language=await page.evaluate(async()=>{
      const before={hd:settings.hd,view:{...view},width:canvas.width,height:canvas.height};
      openSettings();$('city-language').click();$('confirm-settings').click();
      await new Promise(r=>setTimeout(r,500));requestRender();await new Promise(requestAnimationFrame);
      return {before,after:{hd:settings.hd,view:{...view},width:canvas.width,height:canvas.height},language:settings.cityLanguage,
        dpr:viewport.dpr,labelPixels:[...battlefield.cityLabels.values()].map(c=>[c.width,c.height])};
    });
    check(report.language.language==='zh','city language toggles to Chinese');
    check(JSON.stringify(report.language.before)===JSON.stringify(report.language.after),'Chinese switch preserves HD, camera and map resolution');
    check(report.language.dpr>2.9,'HD uses native DPR3 within six megapixel budget');
    check(report.language.labelPixels.length>0,'Chinese city labels render');
    let selections=0;
    const observe=request=>{if(request.url().includes('/api/action')&&decodeURIComponent(request.url()).includes('"select"'))selections++;};
    page.on('request',observe);
    const camera=await page.evaluate(()=>({...view}));
    await page.mouse.move(440,190);await page.mouse.down();await page.mouse.move(560,220,{steps:15});await page.mouse.up();
    check(await page.evaluate(before=>Math.abs(view.x-before.x)>20||Math.abs(view.y-before.y)>20,camera),'actual pointer drag moves the camera');
    check(selections===0,'drag does not send a cell selection request');page.off('request',observe);
    const zoomBefore=await page.evaluate(()=>view.zoom);await page.mouse.wheel(0,-120);await page.waitForTimeout(150);
    check(await page.evaluate(before=>view.zoom>before,zoomBefore),'wheel zoom updates the rendered view');
    // Use the same scene/time for both renderers and compare their actual pixels.
    const previous=cp.execFileSync('git',['show','HEAD:rewrite/Ew4.Web/wwwroot/battlefield.js'],{encoding:'utf8'});
    report.pan=await page.evaluate(async source=>{
      const current=Ew4Battlefield;(0,eval)(source);const baseline=new Ew4Battlefield.Renderer(battlefield.api);globalThis.Ew4Battlefield=current;
      const original={...view},times={},builds={};
      function frame(renderer){
        ctx.setTransform(viewport.dpr,0,0,viewport.dpr,0,0);ctx.fillStyle='#688e91';ctx.fillRect(0,0,viewport.w,viewport.h);
        ctx.translate(viewport.w/2,viewport.h/2);ctx.scale(view.zoom,view.zoom);ctx.translate(-view.x,-view.y);
        const min=screenToWorld(0,0),max=screenToWorld(viewport.w,viewport.h);
        renderer.frame({draw:ctx,state,map:manifest.maps[state.map],visibleRect:{left:min.x,top:min.y,width:max.x-min.x,height:max.y-min.y},zoom:view.zoom,
          showGrid:false,selection:null,selected:null,motions:new Map(),facings,arrivals:new Map(),deaths:new Map(),combatMotions:new Map()},500);
      }
      const cachedGround=battlefield.staticGround;
      const direct=function(){this.ground();this.facilities(true);this.terrain(false);this.ownership();this.mapTiles();this.mapText();this.borders();this.terrain(true);this.facilities(false);};
      const samples=[];
      try{
        for(const [name,renderer] of [['before',baseline],['after',battlefield]]){
          view={...original};let count=0;const ground=renderer.ground;renderer.ground=function(){count++;return ground.call(this);};
          frame(renderer);const durations=[];
          for(let i=0;i<90;i++){
            view.x+=3/view.zoom;view.y+=Math.sin(i/10)*1.5/view.zoom;clampView();
            const start=performance.now();frame(renderer);durations.push(performance.now()-start);
            if(name==='after'&&[0,30,89].includes(i)){
              const cached=ctx.getImageData(0,0,canvas.width,canvas.height).data;
              renderer.staticGround=direct;frame(renderer);const raw=ctx.getImageData(0,0,canvas.width,canvas.height).data;renderer.staticGround=cachedGround;
              let delta=0,strong=0;for(let p=0;p<raw.length;p+=4){let d=0;for(let c=0;c<3;c++)d+=Math.abs(raw[p+c]-cached[p+c]);delta+=d;if(d>90)strong++;}
              samples.push({mean:delta/(raw.length/4*3),strong:strong/(raw.length/4)});
            }
          }
          renderer.ground=ground;durations.sort((a,b)=>a-b);times[name]={mean:durations.reduce((a,b)=>a+b,0)/durations.length,p95:durations[Math.floor(durations.length*.95)]};builds[name]=count;
        }
        return {times,builds,samples,cachePixels:battlefield.staticCanvas.width*battlefield.staticCanvas.height};
      }finally{view=original;battlefield.staticGround=cachedGround;requestRender();}
    },previous);
    check(report.pan.builds.after<report.pan.builds.before/3,'panning reuses ground instead of rebuilding every frame');
    check(report.pan.cachePixels<=10*1024*1024,'ground cache memory remains bounded');
    for(const result of report.pan.samples)check(result.mean<3&&result.strong<.025,'panned cache matches direct drawing');
    report.viewports=[];
    for(const [width,height] of [[844,390],[1280,720],[1920,1080],[544,248],[390,844]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(200);
      await page.evaluate(async()=>{const u=state.units.find(u=>u.owner===state.playerFaction&&u.canMove);await selectCell(u.cell,true,true);});
      const controls=await page.evaluate(()=>['menu-button','end-turn','undo-move','next-unit','actions'].map(id=>{const r=$(id).getBoundingClientRect();return {id,x:r.x,y:r.y,right:r.right,bottom:r.bottom,width:r.width,height:r.height};}));
      for(const r of controls)check(r.x>=-.1&&r.y>=-.1&&r.right<=width+.1&&r.bottom<=height+.1,'controls fit '+width+' '+r.id);
      const [menu,end,undo,next,actions]=controls;
      check(next.right<=actions.x+1&&actions.right<=end.x+1,'bottom toolbar has no overlap at '+width);
      if(width>=800)check(menu.width>=47.9&&end.width>=47.9,'comfortable side touch targets at '+width);
      await page.screenshot({path:folder+'/battle-'+width+'.png'});report.viewports.push({width,height,controls});
    }
    check(report.errors.length===0&&report.missing.length===0,'no browser or resource failures');
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(folder+'/verification.json',JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
