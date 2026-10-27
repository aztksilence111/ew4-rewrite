'use strict';
const assert=require('node:assert/strict'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root=path.resolve(__dirname,'..');
const baselineIndex=process.argv.indexOf('--baseline-ref'),baseline=baselineIndex>=0?process.argv[baselineIndex+1]:null;
if(baselineIndex>=0&&!baseline)throw Error('--baseline-ref requires a Git revision');
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const results=[];
  try{
    const page=await browser.newPage();
    await page.setContent('<!doctype html><canvas></canvas>');
    for(const name of ['vendor/pixi-7.4.3.min.js','pixi-battle.js']){
      if(baseline&&name==='pixi-battle.js')await page.addScriptTag({content:require('node:child_process').execFileSync('git',['show',baseline+':rewrite/Ew4.Web/wwwroot/pixi-battle.js'],{cwd:root,encoding:'utf8'})});
      else await page.addScriptTag({path:path.join(root,'rewrite/Ew4.Web/wwwroot',name)});
    }
    for(const mode of ['release','resurrect','evict-resurrect','release-during-frame','release-warm-sprite']){
      const result=await page.evaluate(mode=>{
        const canvas=document.createElement('canvas'),draw=new Ew4Pixi.Context(canvas),source=document.createElement('canvas');
        source.width=source.height=16;const paint=source.getContext('2d');paint.fillStyle='#ff0000';paint.fillRect(0,0,16,16);
        const rect={left:0,top:0,width:32,height:32};let paints=0;
        const frame=()=>{draw.begin(32,32);draw.cachedLayer('ground',rect,()=>{paints++;draw.drawImage(source,0,0);});draw.end();};
        let error=null,red=false,reused=false,retryError=null;
        try{
          frame();frame();reused=paints===1;
          if(mode==='release-during-frame'||mode==='release-warm-sprite'){
            if(mode==='release-warm-sprite'){draw.begin(32,32);draw.drawImage(source,0,0);draw.end();}
            draw.begin(32,32);draw.drawImage(source,0,0);draw.release(source);draw.end();
          }else if(mode==='evict-resurrect'){
            // Keep a different layer active while the old layer remains in its pool slot.
            const filler=document.createElement('canvas');filler.width=filler.height=8;
            draw.begin(32,32);draw.drawImage(filler,0,0);draw.sourcePixels+=49*1024*1024;draw.end();
          }else draw.release(source);
          if(mode==='resurrect'||mode==='evict-resurrect')draw.texture(source,0,0,16,16);
          frame();const pixels=draw.renderer.extract.pixels();red=pixels[0]===255&&pixels[1]===0&&pixels[2]===0;
        }catch(e){error=e.message;try{frame();}catch(retry){retryError=retry.message;}}
        finally{draw.renderer.destroy(true);}
        return {mode,reused,paints,red,error,retryError};
      },mode);
      results.push(result);
    }
    console.log(JSON.stringify(results));
    if(baseline)assert(results.filter(r=>r.error).length===4,'reproduce stale layers and releases before submission');
    else for(const r of results){assert.equal(r.error,null,JSON.stringify(r));assert(r.reused&&r.paints===2&&r.red,JSON.stringify(r));}
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
