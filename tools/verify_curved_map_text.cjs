'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const runtime=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=runtime('playwright'),{PNG}=runtime('pngjs');
const base=process.argv[2]||'http://127.0.0.1:5532',feedback=process.argv.includes('--lake-range-feedback'),folder=path.resolve('build/curved-map-text-20261025/'+(feedback?'lake-range-feedback':'browser'));
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const mask=PNG.sync.read(await fs.readFile('maps/eastasia_rewrite/land_mask.png'));
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,cases:[],errors:[],missing:[]},check=(v,message)=>{assert.ok(v,message);report.checks++;};
  try{
    const viewports=feedback?[['desktop',1280,720]]:[['desktop',1280,720],['phone',844,390],['portrait',390,844]];
    for(const [name,width,height] of viewports)for(const backend of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest7','fra'));await page.waitForFunction(()=>ctx&&[...images.values()].every(i=>i.complete));
      const result=await page.evaluate(async()=>{
        stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();suspendRendering();
        const labels=manifest.maps[state.map].labels;
        await Promise.all(labels.map(l=>Ew4OriginalUI.prepareMapText(l.textZh,!l.sea)));
        const r=Object.create(Ew4Battlefield.Renderer.prototype),draw=document.createElement('canvas').getContext('2d');
        Object.assign(r,{api:{...battlefield.api},mapLabels:new Map(),visible:()=>true,visibleRect:{left:-10000,top:-10000,width:40000,height:40000},draw});
        const results=[],seaInk=[];
        for(const language of ['en','zh']){
          r.api.geographicLanguage=language;r.mapLabels.clear();r.labelIndex=new Ew4Battlefield.SpatialIndex(labels,Ew4Battlefield.mapLabelBounds);r.mapText();
          for(const label of labels){
            const cache=r.mapLabels.get(label.libName+':1:'+label.opacity+':'+language);if(!cache)throw Error('Missing '+label.text);
            const c=cache.canvas,pixels=c.getContext('2d').getImageData(0,0,c.width,c.height).data;
            let ink=0,edge=0;const points=[];
            for(let y=0;y<c.height;y++)for(let x=0;x<c.width;x++)if(pixels[(y*c.width+x)*4+3]){
              ink++;if(!x||!y||x===c.width-1||y===c.height-1)edge++;
              if(label.sea&&!label.lake&&x%2===0&&y%2===0)points.push([Math.round(label.tx+cache.left+x+32),Math.round(label.ty+cache.top+y+54)]);
            }
            const p=Ew4Battlefield.textPath(label.textPath),angles=[.1,.3,.7,.9].map(t=>p.at(p.length*t).angle);
            const glyphs=Ew4Battlefield.pathLettering(libraries.get('maptext'),label,p);
            const first=label.textPath[0],last=label.textPath[label.textPath.length-1],dx=last[0]-first[0],dy=last[1]-first[1];
            const expectedCurved=label.textPath.some(([x,y])=>Math.abs((x-first[0])*dy-(y-first[1])*dx)>.01);
            results.push({text:label.text,language,ink,edge,angles,expectedCurved,glyphs:glyphs.length,width:c.width,height:c.height});
            if(points.length)seaInk.push({text:label.text,language,points});
          }
        }
        // A new map label works with only textPath and original glyph assets, without a baked group.
        const generic={text:'TEST SEA',textZh:'测试海',tx:500,ty:500,size:45,sea:true,textPath:[[-250,0],[0,100],[250,0]],opacity:.72,libName:'future-map-only'};
        r.api.geographicLanguage='en';r.labelIndex=new Ew4Battlefield.SpatialIndex([generic],Ew4Battlefield.mapLabelBounds);r.mapLabels.clear();r.mapText();
        const genericCache=r.mapLabels.get('future-map-only:1:0.72:en'),genericPixels=genericCache.canvas.getContext('2d').getImageData(0,0,genericCache.canvas.width,genericCache.canvas.height).data;
        r.indexMap({overlays:[],tiles:[],labels:[{...generic,textPath:[[-250,0],[0,0],[250,0]]}]});r.mapText();
        const mapSwitch=r.mapLabels.get('future-map-only:1:0.72:en').height!==genericCache.height;
        const legacy=manifest.maps.europe.labels[0];r.labelIndex=new Ew4Battlefield.SpatialIndex([legacy],Ew4Battlefield.mapLabelBounds);r.mapLabels.clear();r.mapText();
        const p=Ew4Battlefield.textPath([[0,0],[50,100],[100,0],[150,-100],[200,0]]);
        return {results,seaInk,expectedLabels:labels.length,genericInk:genericPixels.some(v=>v>0),mapSwitch,legacy:r.mapLabels.size===1,gpu:!!ctx.renderer,
          joined:[p.at(0),p.at(p.length/2),p.at(p.length)],invalid:Ew4Battlefield.textPath([[0,0],[0,0],[0,0]])===null};
      });
      check(result.results.length===result.expectedLabels*2,'all current labels render in both languages');check(result.genericInk,'new maps can curve original English glyphs without a baked animation');
      check(result.legacy,'legacy straight label works');check(result.gpu===(backend==='pixi'),'renderer selection');check(result.invalid,'zero length path rejected');
      check(result.mapSwitch,'new map with reused label name gets its own curve');
      check(Math.abs(result.joined[1].x-100)<.001&&Math.abs(result.joined[1].y)<.001,'chained curves meet at shared point');
      for(const label of result.results){check(label.ink>0&&!label.edge,label.text+' '+label.language+' nonblank and unclipped');const turn=Math.max(...label.angles)-Math.min(...label.angles);check(label.expectedCurved?turn>.001:turn<.001,label.text+' requested character direction');}
      result.water=result.seaInk.map(({text,language,points})=>{const land=points.filter(([x,y])=>x<0||y<0||x>=mask.width||y>=mask.height||mask.data[(y*mask.width+x)*4]>=128).length;return {text,language,land,sampled:points.length,fraction:land/points.length};});delete result.seaInk;
      for(const water of result.water.filter(w=>['EAST CHINA SEA','SOUTH CHINA SEA','SETO INLAND SEA'].includes(w.text)))check(water.fraction<.02,water.text+' lettering stays on water: '+water.fraction);
      const regions=feedback?[['baikal','LAKE BAIKAL',900],['taebaek','TAEBAEK MOUNTAINS',650]]:[['himalaya','HIMALAYAS',2400],['china','QINLING MOUNTAINS',1800],['sea','EAST CHINA SEA',2200]];
      for(const language of ['zh','en'])for(const [region,text,span] of regions){
        await page.evaluate(({language,text,span})=>{
          settings.geographicLanguage=language;const label=manifest.maps[state.map].labels.find(l=>l.text===text);
          Object.assign(view,{x:label.tx,y:label.ty,zoom:Math.min(innerWidth/span,innerHeight/(span*.6))});clampView();battlefield.invalidateGround();render(2000);suspendRendering();
        },{language,text,span});
        await page.screenshot({path:path.join(folder,`${name}-${backend}-${region}-${language}.png`)});
      }
      report.cases.push({name,backend,...result});await page.close();
    }
    check(!report.errors.length,'no browser errors');check(!report.missing.length,'no missing resources');
  }finally{await fs.writeFile(path.join(folder,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,cases:report.cases.length,labels:report.cases.reduce((n,c)=>n+c.results.length,0),errors:report.errors.length,missing:report.missing.length}));
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
