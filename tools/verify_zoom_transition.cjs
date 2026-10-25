'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5503',folder=path.resolve(process.argv[3]||'build/zoom-menu-20261024/before');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={cases:[],errors:[],scope:'Desktop Edge, DPR 2, one second idle warmup, three zoom cycles; no physical Android execution'};
  try{
    for(const [name,width,height] of [['phone',1188,540],['ace3',1264,576],['desktop',1280,720]]){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:2,hasTouch:true});
      page.on('pageerror',e=>report.errors.push(e.message));
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>ctx?.stats.frames>0&&[...images.values()].every(i=>i.complete));
      await page.waitForTimeout(1000);
      const samples=await page.evaluate(async()=>{
        suspendRendering();
        const origin={...view},scale=Math.max(1,Math.min(innerWidth/568,innerHeight/320,3)),result=[];
        for(let cycle=0;cycle<3;cycle++){
          for(const relative of [1,.85,.7,.55,.48,.38,.28,.38,.48,.55,.7,.85,1]){
            Object.assign(view,origin,{zoom:scale*relative});clampView();
            const builds=battlefield.staticBuilds||0,start=performance.now();render(start);suspendRendering();
            result.push({cycle,relative,ms:performance.now()-start,builds:(battlefield.staticBuilds||0)-builds,pixels:battlefield.chunkPixels});
            await new Promise(resolve=>setTimeout(resolve,16));
          }
        }
        return result;
      });
      assert(samples.every(s=>Number.isFinite(s.ms)));
      assert(samples.every(s=>s.pixels<=12*1024*1024),'bounded map tile cache');
      if(process.argv.includes('--expect-warm-reuse'))assert(samples.filter(s=>s.cycle>0).every(s=>s.builds===0),'repeated zoom cycles reuse terrain tiles');
      report.cases.push({name,width,height,samples,max:Math.max(...samples.map(s=>s.ms)),builds:samples.reduce((n,s)=>n+s.builds,0)});
      await page.screenshot({path:path.join(folder,name+'.png')});await page.close();
    }
    assert.deepEqual(report.errors,[]);
    await fs.writeFile(path.join(folder,'result.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify(report.cases.map(({samples,...r})=>r)));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
