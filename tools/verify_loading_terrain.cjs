'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5573',out=path.resolve(process.argv[3]||'build/loading-terrain-20261025');
(async()=>{
  await fs.mkdir(out,{recursive:true});const results=[],errors=[];
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const renderer of ['pixi','canvas'])for(const [width,height] of [[1920,873],[844,390]]){
      const page=await browser.newPage({viewport:{width,height}});
      page.on('pageerror',e=>errors.push(e.message));
      await page.route('**/content/maps/**',async route=>{await new Promise(resolve=>setTimeout(resolve,150));await route.continue();});
      await page.goto(base+'/?renderer='+renderer);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      const stages=await page.evaluate(async()=>{
        const ground=battlefield.groundLayers;
        battlefield.groundLayers=function(...args){
          const result=ground.apply(this,args);
          this.loadingGround={signature:this.groundSignature,ready:this.map.tiles.every(t=>{const img=images.get(t.file);return img?.complete&&img.naturalWidth>0;})};
          return result;
        };
        const snapshot=()=>({sameWorld:battlefield.state===state&&battlefield.loadingGround?.signature===battlefield.groundSignature,terrainReady:battlefield.loadingGround?.ready===true,
          gpu:!!ctx?.gpu,nativeTextures:ctx?.gpu?manifest.maps[state.map].tiles.filter(t=>ctx.sources.has(images.get(t.file))).length:null});
        await start('conquest1','fra');
        $('detail-modal').hidden=true;$('menu-button').click();
        const started=snapshot();
        await rpc('save',{slot:1,mode:2});await showMain();
        await load(1,2);
        $('detail-modal').hidden=true;$('menu-button').click();
        return {started,loaded:snapshot()};
      });
      for(const [stage,result] of Object.entries(stages)){
        assert(result.sameWorld&&result.terrainReady,renderer+' '+width+' '+stage+': complete terrain must be drawn before immediate pause');
        assert.equal(result.gpu,renderer==='pixi');
        if(result.gpu)assert(result.nativeTextures>0,'native map textures must reach the GPU before loading closes');
        results.push({renderer,width,height,stage,...result});
      }
      await page.waitForTimeout(350);await page.screenshot({path:path.join(out,`${renderer}-${width}x${height}-paused.png`)});await page.close();
    }
    assert.deepEqual(errors,[]);console.log(JSON.stringify({cases:results.length,errors:0}));
  }finally{await fs.writeFile(path.join(out,'results.json'),JSON.stringify({results,errors},null,2));await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
