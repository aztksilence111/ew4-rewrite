'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2]||'http://127.0.0.1:5532',folder=path.resolve('build/eastasia-mountains-20261026/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const report={checks:0,cases:[],errors:[],missing:[]},check=(v,m)=>{assert.ok(v,m);report.checks++;};
  try{
    for(const [name,width,height] of [['desktop',1280,720],['phone',844,390],['portrait',390,844]])for(const backend of ['canvas','pixi']){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest7','fra'));await page.waitForFunction(()=>ctx&&[...images.values()].every(i=>i.complete));
      const stateCheck=await page.evaluate(async()=>{
        stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();suspendRendering();
        const labels=manifest.maps[state.map].labels;
        await Promise.all(labels.map(l=>Ew4OriginalUI.prepareMapText(l.textZh,!l.sea)));
        const r=Object.create(Ew4Battlefield.Renderer.prototype),draw=document.createElement('canvas').getContext('2d');
        Object.assign(r,{api:{...battlefield.api},mapLabels:new Map(),visible:()=>true,visibleRect:{left:-10000,top:-10000,width:40000,height:40000},draw});
        const glyphs=[];
        for(const language of ['zh','en']){
          r.api.geographicLanguage=language;r.mapLabels.clear();r.labelIndex=new Ew4Battlefield.SpatialIndex(labels,Ew4Battlefield.mapLabelBounds);r.mapText();
          for(const l of labels){
            const cache=r.mapLabels.get(l.libName+':1:'+l.opacity+':'+language);if(!cache)throw Error('Missing '+l.text);
            const c=cache.canvas,p=c.getContext('2d').getImageData(0,0,c.width,c.height).data;
            let ink=0,edge=0;
            for(let y=0;y<c.height;y++)for(let x=0;x<c.width;x++)if(p[(y*c.width+x)*4+3]){ink++;if(!x||!y||x===c.width-1||y===c.height-1)edge++;}
            glyphs.push({text:l.text,language,ink,edge});
          }
        }
        return {map:state.map,gpu:!!ctx.renderer,cities:state.cells.filter(c=>c.buildingKind==='city').length,glyphs};
      });
      check(stateCheck.map==='eastasia'&&stateCheck.gpu===(backend==='pixi'),'map and renderer loaded');
      check(stateCheck.cities===279,'279 cities retained in actual startup');
      check(stateCheck.glyphs.length===174,'87 names rendered in two languages');
      for(const l of stateCheck.glyphs)check(l.ink>0&&!l.edge,l.text+' '+l.language+' nonblank and unclipped');
      const regions=name==='desktop'?
        [['altai','ALTAI MOUNTAINS',2100],['yinshan','YINSHAN MOUNTAINS',1500],['altyn','ALTYN TAGH',1700],
          ['tibet','GANGDISE MOUNTAINS',2100],['tibet-east','TANGGULA MOUNTAINS',1900],['north-china','LULIANG MOUNTAINS',1800],
          ['central','DABA MOUNTAINS',1700],['southwest','WUMENG MOUNTAINS',1500],['southeast','NANLING MOUNTAINS',2000]]:
        [['yinshan','YINSHAN MOUNTAINS',1400],['central','WUSHAN MOUNTAINS',1500],['southeast','NANLING MOUNTAINS',1700]];
      for(const language of ['zh','en'])for(const [region,text,span] of regions){
        await page.evaluate(({language,text,span})=>{
          settings.geographicLanguage=language;settings.cityLanguage='zh';const l=manifest.maps[state.map].labels.find(l=>l.text===text);
          Object.assign(view,{x:l.tx,y:l.ty,zoom:Math.min(innerWidth/span,innerHeight/(span*.6))});
          clampView();battlefield.invalidateGround();render(2000);suspendRendering();
        },{language,text,span});
        const image=await page.screenshot({path:path.join(folder,`${name}-${backend}-${region}-${language}.png`)}),png=PNG.sync.read(image),colors=new Set();
        for(let i=0;i<png.data.length;i+=76)colors.add(png.data.subarray(i,i+3).toString('hex'));
        check(colors.size>100,'nonblank '+region+' screenshot');
      }
      report.cases.push({name,backend,...stateCheck});await page.close();
    }
    check(!report.errors.length,'no browser errors');check(!report.missing.length,'no missing resources');
  }finally{await fs.writeFile(path.join(folder,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,cases:report.cases.length,errors:report.errors.length,missing:report.missing.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
