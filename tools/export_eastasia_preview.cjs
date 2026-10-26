'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright');
const base=process.argv[2]||'http://127.0.0.1:5532';
const output=path.resolve('maps/eastasia_rewrite/eastasia_full_preview.jpg');
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    const page=await browser.newPage({viewport:{width:6000,height:4355},deviceScaleFactor:1});
    const errors=[];
    page.on('pageerror',e=>errors.push(e.message));
    await page.goto(base+'/?renderer=canvas');await page.waitForFunction(()=>startupReady);
    await page.evaluate(()=>start('conquest7','fra'));
    await page.waitForFunction(()=>ctx&&[...images.values()].every(i=>i.complete));
    const result=await page.evaluate(async()=>{
      stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();suspendRendering();
      settings.geographicLanguage='zh';settings.cityLanguage='zh';showGrid=false;
      selection=undefined;selected=undefined;
      // The inspection scene's two placeholder troops are not map geography.
      state={...state,units:[],cells:state.cells.map(c=>({...c,owner:null}))};
      const map=manifest.maps.eastasia,b=map.cameraBounds;
      await Ew4OriginalUI.prepareMapText(map.labels.map(l=>l.textZh).join('')+state.cells.map(c=>c.name||'').join(''),true);
      Object.assign(view,{x:b.left+b.width/2,y:b.top+b.height/2,zoom:Math.min(innerWidth/b.width,innerHeight/b.height)});
      battlefield.invalidateGround();render(2000,true);suspendRendering();
      for(const el of document.querySelectorAll('body *'))if(el!==canvas&&!el.contains(canvas))el.style.visibility='hidden';
      return {map:state.map,namedPlaces:state.cells.filter(c=>c.name).length,buildings:state.cells.filter(c=>c.buildingKind).length,width:canvas.width,height:canvas.height};
    });
    assert.equal(result.namedPlaces,280);assert.equal(result.buildings,521);
    await page.locator('#map').screenshot({path:output,type:'jpeg',quality:94,timeout:60000});
    assert.deepEqual(errors,[]);
    console.log(JSON.stringify({...result,path:output,bytes:(await fs.stat(output)).size,errors:0}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
