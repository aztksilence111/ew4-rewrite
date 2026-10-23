'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],folder=process.argv[3];
if(!base||!folder)throw Error('Usage: node render.cjs isolated-url output-folder');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720},deviceScaleFactor:2});
  const errors=[],requests=[];page.on('pageerror',e=>errors.push(e.message));
  page.on('request',r=>{if(r.url().includes('/api/'))requests.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady);
    await page.evaluate(()=>start('conquest1','fra'));
    await page.waitForFunction(()=>!busy&&[...images.values()].every(i=>i.complete));
    await page.evaluate(()=>document.fonts.ready);
    const report={scope:'Desktop Edge local scene, 1280x720 DPR2; JS draw-submission times and rAF intervals, not phone or GPU completion times',cases:[],errors};
    for(const variant of ['near-pan','far-pan','far-pan-warm','far-pan-no-body-draw','near-move-cold','near-move-warm','near-state-clone','near-idle']){
      const beforeRequests=requests.length;
      const result=await page.evaluate(async variant=>{
        suspendRendering();appVisible=true;selected=null;selection=null;effects.clear();
        const originals={},timings={},counts={},stack=[];let draws=0,cacheMisses=0;
        const surface={coastCalls:0,coastMisses:0,coastMs:0,borderCalls:0,borderMisses:0,borderMs:0},originalCoast=battlefield.surface.coast,originalBorder=battlefield.surface.border;
        battlefield.surface.coast=function(neighbors){const key=neighbors.map(n=>n?`${+n.active},${+n.sea},${n.color||0}`:'-').join(';');surface.coastCalls++;if(!this.coasts.has(key))surface.coastMisses++;const t=performance.now();try{return originalCoast.call(this,neighbors);}finally{surface.coastMs+=performance.now()-t;}};
        battlefield.surface.border=function(mask,...args){surface.borderCalls++;if(!this.borders.has(mask))surface.borderMisses++;const t=performance.now();try{return originalBorder.call(this,mask,...args);}finally{surface.borderMs+=performance.now()-t;}};
        const methods=['frame','staticGround','ground','terrain','ownership','borders','mapText','units','unitAnimation','flags','unitMarkers','labels','highlights','motionPose','cellsInRect'];
        const fullState=state,originalDraw=ctx.drawImage,originalBody=battlefield.unitAnimation;
        let moving=null;
        view.zoom=variant.startsWith('far')?.48:1.5;centerPlayer();view.zoom=variant.startsWith('far')?.48:1.5;clampView();
        if(variant.includes('move')){
          moving=state.units.find(u=>u.owner===state.playerFaction);
          const p=point(moving.cell);view.x=p.x;view.y=p.y;clampView();
          const next=Ew4Battlefield.neighbor(moving.cell,0,state.width,state.height);
          motions.set(moving.id,{path:[moving.cell,next>=0?next:moving.cell-1],started:performance.now(),stepMs:5000});
          if(variant.endsWith('cold')){battlefield.unitFrames.clear();battlefield.unitFramePixels=0;}
        }
        const origin={...view};
        if(variant.endsWith('no-body-draw'))battlefield.unitAnimation=()=>{};
        for(const method of methods){
          originals[method]=battlefield[method];
          battlefield[method]=function(...args){
            if(method==='unitAnimation'&&args[1]){const lib=args[0],item=args[1],id=this.unitLibraryIds.get(lib),frame=Math.floor(args[2])%item.duration;if(!id||!this.unitFrames.has(id+':'+item.name+':'+frame))cacheMisses++;}
            const entry={start:performance.now(),child:0};stack.push(entry);counts[method]=(counts[method]||0)+1;
            try{return originals[method].apply(this,args);}finally{const duration=performance.now()-entry.start;stack.pop();timings[method]=(timings[method]||0)+duration-entry.child;if(stack.length)stack.at(-1).child+=duration;}
          };
        }
        ctx.drawImage=function(...args){draws++;return originalDraw.apply(this,args);};
        const durations=[],intervals=[];const builds=battlefield.staticBuilds||0;let last;
        try{
          for(let i=0;i<120;i++){
            const now=await new Promise(requestAnimationFrame);if(last!==undefined)intervals.push(now-last);last=now;
            if(variant.includes('pan')){view.x=origin.x+Math.sin(i/119*Math.PI)*220;view.y=origin.y+Math.sin(i/119*Math.PI*2)*80;clampView();}
            if(variant.endsWith('state-clone'))state={...fullState};
            if(!variant.endsWith('idle'))renderDirty=true;
            const start=performance.now();render(now);durations.push(performance.now()-start);suspendRendering();
          }
        }finally{
          for(const method of methods)battlefield[method]=originals[method];
          battlefield.unitAnimation=originalBody;ctx.drawImage=originalDraw;state=fullState;motions.clear();
          battlefield.surface.coast=originalCoast;battlefield.surface.border=originalBorder;
        }
        const stats=a=>{const s=[...a].sort((a,b)=>a-b);return {mean:a.reduce((x,y)=>x+y,0)/a.length,p95:s[Math.floor((s.length-1)*.95)],max:s.at(-1)};};
        return {variant,zoom:view.zoom,visibleUnits:battlefield.visibleUnits.length,frames:counts.frame||0,drawCalls:draws,cacheMisses,staticBuilds:(battlefield.staticBuilds||0)-builds,drawMs:stats(durations),rafIntervalMs:stats(intervals),exclusiveMs:timings,calls:counts,surface};
      },variant);
      result.apiRequests=requests.length-beforeRequests;assert.equal(result.apiRequests,0,'render/camera benchmark must not call C#');report.cases.push(result);
      await fs.writeFile(path.join(folder,'render.json'),JSON.stringify(report,null,2));
      console.log(JSON.stringify({variant,frames:result.frames,mean:result.drawMs.mean,p95:result.drawMs.p95,builds:result.staticBuilds,units:result.visibleUnits}));
    }
    const pointerRequestStart=requests.length;
    await page.evaluate(()=>{
      window.auditPointer={frames:[],builds:battlefield.staticBuilds,original:battlefield.frame};
      battlefield.frame=function(...args){const started=performance.now();try{return window.auditPointer.original.apply(this,args);}finally{window.auditPointer.frames.push(performance.now()-started);}};
    });
    await page.mouse.move(420,350);await page.mouse.down();
    for(let i=1;i<=40;i++){await page.mouse.move(420+i*8,350+Math.sin(i/5)*20);await page.waitForTimeout(16);}
    await page.mouse.up();
    report.pointerDrag=await page.evaluate(()=>{const audit=window.auditPointer;battlefield.frame=audit.original;return {frames:audit.frames.length,maxMs:Math.max(...audit.frames),meanMs:audit.frames.reduce((a,b)=>a+b,0)/audit.frames.length,builds:battlefield.staticBuilds-audit.builds};});
    report.pointerDrag.apiRequests=requests.length-pointerRequestStart;
    assert.equal(report.pointerDrag.apiRequests,0,'actual pointer drag must not call C#');
    await fs.writeFile(path.join(folder,'render.json'),JSON.stringify(report,null,2));
    assert.equal(errors.length,0,'page errors');
    await page.screenshot({path:path.join(folder,'scene.png')});
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
