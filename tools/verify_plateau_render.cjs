'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],folder=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const report={scope:'Desktop Edge DPR2; same loaded scene and frozen animation time, reference/compiled pose ABBA; not Android timings',cases:[],errors:[]};
  try{
    for(const [name,width,height] of [['desktop',1280,720],['phone',1188,540],['portrait',540,1188]]){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:2,hasTouch:true});
      page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>ctx?.gpu&&[...images.values()].every(i=>i.complete));
      await page.waitForTimeout(1500);
      const result=await page.evaluate(async()=>{
        suspendRendering();appVisible=true;selection=null;selected=null;effects.clear();
        const compiled=ctx.drawPose,time=2000,groups=[];
        const image=()=>new Uint8Array(ctx.renderer.extract.pixels());
        ctx.drawPose=undefined;render(time);suspendRendering();const reference=image();
        ctx.drawPose=compiled;render(time);suspendRendering();const actual=image();
        let differing=0,totalDifference=0;const colors=new Set();
        for(let i=0;i<actual.length;i+=4){let diff=0;for(let c=0;c<3;c++)diff=Math.max(diff,Math.abs(actual[i+c]-reference[i+c]));if(diff>2)differing++;totalDifference+=diff;if(i%397===0)colors.add(actual[i]+','+actual[i+1]+','+actual[i+2]);}
        for(const enabled of [false,true,true,false]){
          ctx.drawPose=enabled?compiled:undefined;const samples=[];
          for(let i=0;i<80;i++){await new Promise(requestAnimationFrame);const began=performance.now();render(time);suspendRendering();samples.push(performance.now()-began);}
          groups.push({compiled:enabled,samples});
        }
        ctx.drawPose=compiled;
        const oldBuilds=battlefield.staticBuilds;cameraInputUntil=performance.now()+1000;
        battlefield.warmTarget={...battlefield.warmTarget};battlefield.scheduleChunkWarm();
        await new Promise(resolve=>setTimeout(resolve,150));
        const paused=battlefield.staticBuilds===oldBuilds;cameraInputUntil=0;
        return {groups,differingPixels:differing,pixels:actual.length/4,meanPixelDifference:totalDifference/(actual.length/4),colors:colors.size,paused,pixelsCached:battlefield.chunkPixels};
      });
      assert(result.colors>40,'nonblank battlefield');
      assert(result.differingPixels/result.pixels<.005,'compiled poses match reference pixels');
      assert(result.paused,'background chunks must pause during camera input');
      assert(result.pixelsCached<=12*1024*1024,'terrain cache bounded');
      await page.screenshot({path:path.join(folder,name+'.png')});
      for(const mode of [false,true]){const samples=result.groups.filter(g=>g.compiled===mode).flatMap(g=>g.samples).sort((a,b)=>a-b);result[mode?'compiled':'reference']={median:samples[Math.floor(samples.length/2)],p95:samples[Math.floor(samples.length*.95)]};}
      report.cases.push({name,width,height,...result});await page.close();
    }
    assert.deepEqual(report.errors,[]);
  }finally{await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report.cases.map(({groups,...c})=>c)));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
