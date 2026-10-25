'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const runtime=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=runtime('playwright'),{PNG}=runtime('pngjs');
const base=process.argv[2]||'http://127.0.0.1:5532',estuaries=process.argv.includes('--estuaries'),shoreline=process.argv.includes('--shoreline'),quick=process.argv.includes('--quick'),folder=path.resolve('build/eastasia-water-20261025/'+(shoreline?'mouths/correction-browser':estuaries?'mouths/browser':'browser'));
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  for(let attempt=0;attempt<90;attempt++){
    try{const response=await fetch(base,{signal:AbortSignal.timeout(1000)});if(response.ok)break;}catch{}
    if(attempt===89)throw Error('Preview did not start');
    await new Promise(resolve=>setTimeout(resolve,500));
  }
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,cases:[],errors:[],missing:[]},check=(v,message)=>{assert.ok(v,message);report.checks++;};
  try{
    for(const [name,width,height] of (quick?[['desktop',1280,720]]:[['desktop',1280,720],['phone',844,390],['portrait',390,844]]))for(const backend of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest7','fra'));await page.waitForFunction(()=>ctx&&[...images.values()].every(i=>i.complete));
      const stateCheck=await page.evaluate(async()=>{
        stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();settings.geographicLanguage='zh';settings.cityLanguage='zh';
        await Ew4OriginalUI.prepareMapText('燕山山脉杭州天津福州仰光加尔各答',true);
        return {map:state.map,gpu:!!ctx.renderer};
      });
      check(stateCheck.map==='eastasia'&&stateCheck.gpu===(backend==='pixi'),'actual map and renderer loaded');
      for(const region of estuaries||shoreline?['qiantang','hai','dong','min','irrawaddy','ganges','yangtze','paektu']:['huai','huai-join',...(name==='desktop'?['yanshan']:[])]){
        await page.evaluate(({region,shoreline})=>{
          let x=6326,y=4303,spanX=1150,spanY=950;
          if(region==='huai-join'){x=5920;y=4430;spanX=700;spanY=430;}
          if(region==='yanshan'){
            const label=manifest.maps[state.map].labels.find(l=>l.text==='YANSHAN MOUNTAINS');
            x=label.tx;y=label.ty;spanX=1100;spanY=650;
          }
          const mouths={qiantang:[6570,4840,470,460],hai:[6050,3395,480,400],dong:[5786,5923,370,280],
            min:[6475,5530,470,390],irrawaddy:[2665,7020,550,650],ganges:[1885,5790,760,730],paektu:[7295,2707,380,490],
            yangtze:[6575,4510,900,560]};
          if(mouths[region])[x,y,spanX,spanY]=mouths[region];
          if(shoreline){spanX*=.65;spanY*=.65;if(region==='paektu'){x=7295;y=2710;spanX=250;spanY=240;}}
          Object.assign(view,{x,y,zoom:Math.min(innerWidth/spanX,innerHeight/spanY)});
          clampView();battlefield.invalidateGround();render(2000);suspendRendering();
        },{region,shoreline});
        const buffer=await page.screenshot({path:path.join(folder,`${name}-${backend}-${region}.png`)});
        const png=PNG.sync.read(buffer),colors=new Set();
        for(let i=0;i<png.data.length;i+=76)colors.add(png.data.subarray(i,i+3).toString('hex'));
        check(colors.size>100,'map screenshot nonblank '+name+' '+region);
      }
      report.cases.push({name,backend,...stateCheck});await page.close();
    }
    check(!report.errors.length,'no browser errors');check(!report.missing.length,'no missing resources');
  }finally{await fs.writeFile(path.join(folder,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
