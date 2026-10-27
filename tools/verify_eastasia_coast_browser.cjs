'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5586',phase=process.argv[3]||'after',out=path.resolve('build/eastasia-coast-fix-20261027/browser',phase);
const regions=[['bohai',6095,3361],['yangtze',6724,4593],['fujian',6549,5535],['pearl',5649,6156],['korea',7215,3753],['japan',7941,4000],['ganges',2014,5926],['irrawaddy',2553,7075]];
(async()=>{
 await fs.mkdir(out,{recursive:true});
 const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
 const report={phase,checks:0,errors:[],missing:[],views:[],glyphs:[]};
 const check=(v,m)=>{assert.ok(v,m);report.checks++;};
 try{
  for(const renderer of phase==='before'?['canvas']:['canvas','pixi']){
   const page=await browser.newPage({viewport:{width:1440,height:900}});
   page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
   await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:180000});
   const startMap=async scenario=>{
    await page.evaluate(scenario=>start(scenario,'fra'),scenario);await page.waitForFunction(()=>state&&!busy&&!sessionTransition,null,{timeout:180000});
    await page.evaluate(async()=>{settings.geographicLanguage='zh';settings.cityLanguage='zh';await prepareMapFonts(state);stageIntroOpen=false;$('detail-modal').hidden=true;showGrid=false;effects.clear();
     await Promise.all(manifest.maps[state.map].tiles.map(t=>loadImage(t.file)));
     for(const el of document.querySelectorAll('body *'))if(el!==canvas&&!el.contains(canvas))el.style.visibility='hidden';
    });
   };
   await startMap('conquest7');
   for(const [viewport,width,height] of phase==='before'?[['desktop',1440,900]]:[['desktop',1440,900],['mobile',390,844]]){
    await page.setViewportSize({width,height});
    for(const [name,x,y] of regions.filter(r=>viewport==='desktop'||['fujian','ganges','korea'].includes(r[0]))){
     const pixels=await page.evaluate(({x,y})=>{
      suspendRendering();Object.assign(view,{x,y,zoom:2});clampView();battlefield.invalidateGround();render(performance.now(),true);suspendRendering();
      const data=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;
      const colors=new Set();for(let i=0;i<data.length;i+=64)colors.add(data[i]+','+data[i+1]+','+data[i+2]);
      return {gpu:!!ctx.gpu,colors:colors.size,view:{...view},overflow:document.documentElement.scrollWidth>innerWidth};
     },{x,y});
     check(pixels.gpu===(renderer==='pixi')&&pixels.colors>100&&!pixels.overflow,'actual coast pixels '+renderer+name+viewport);
     await page.locator('#map').screenshot({path:path.join(out,`${renderer}-${viewport}-${name}.jpg`),type:'jpeg',quality:93});
     report.views.push({renderer,viewport,name,...pixels});
    }
   }
   if(phase==='after')for(const [scenario,key,name,count] of [['conquest8','japan','濑户内海',4],['conquest9','hre','利古里亚海',5]]){
    await page.setViewportSize({width:1440,height:900});await startMap(scenario);
    for(const language of ['zh','en']){
     const result=await page.evaluate(async({name,language})=>{
      settings.geographicLanguage=language;await prepareMapFonts(state);await loadLibrary('maptext');
      const label=manifest.maps[state.map].labels.find(l=>l.textZh===name),r=Object.create(Ew4Battlefield.Renderer.prototype);
      Object.assign(r,{api:{...battlefield.api,geographicLanguage:language},mapLabels:new Map(),labelIndex:new Ew4Battlefield.SpatialIndex([label],Ew4Battlefield.mapLabelBounds),
       visibleRect:{left:0,top:0,width:30000,height:30000},draw:document.createElement('canvas').getContext('2d')});
      const drawn=[],original=CanvasRenderingContext2D.prototype.fillText;
      CanvasRenderingContext2D.prototype.fillText=function(text,...args){drawn.push(text);return original.call(this,text,...args);};
      try{r.mapText();}finally{CanvasRenderingContext2D.prototype.fillText=original;}
      const cached=[...r.mapLabels.values()][0],c=cached.canvas,data=c.getContext('2d').getImageData(0,0,c.width,c.height).data;
      let ink=0,edge=0;for(let y=0;y<c.height;y++)for(let x=0;x<c.width;x++)if(data[(y*c.width+x)*4+3]){ink++;if(!x||!y||x===c.width-1||y===c.height-1)edge++;}
      const characters=label.characterPositionsZh.map((p,i)=>{
       const cx=p.x-cached.left,cy=p.y-cached.top,radius=label.characterSizeZh;let ink=0;
       for(let y=Math.max(0,Math.floor(cy-radius));y<Math.min(c.height,cy+radius);y++)for(let x=Math.max(0,Math.floor(cx-radius));x<Math.min(c.width,cx+radius);x++)if(data[(y*c.width+x)*4+3])ink++;
       const px=Number(label.tx)+p.x,py=Number(label.ty)+p.y;
       return {character:[...label.textZh][i],ink,visible:r.labelIndex.query({left:px-20,top:py-20,width:40,height:40}).includes(label)};
      });
      const b=Ew4Battlefield.mapLabelBounds(label);Object.assign(view,{x:(b.left+b.right)/2,y:(b.top+b.bottom)/2,zoom:Math.min(1.6,innerWidth/(b.right-b.left+400),innerHeight/(b.bottom-b.top+300))});clampView();battlefield.invalidateGround();render(performance.now(),true);suspendRendering();
      return {name,language,ink,edge,drawn,characters,fontFailures:Ew4OriginalUI.fontStatus.failures};
     },{name,language});
     check(result.ink>0&&result.edge===0&&!result.fontFailures.length,'complete glyph cache '+renderer+name+language);
     if(language==='zh'){check(result.drawn.join('')===name&&result.characters.length===count,'each full-name character drawn once');check(result.characters.every(c=>c.ink>0&&c.visible),'each distributed character has ink and remains visible away from anchor');}
     await page.locator('#map').screenshot({path:path.join(out,`${renderer}-${key}-${language}.jpg`),type:'jpeg',quality:93});
     report.glyphs.push({renderer,key,...result});
    }
   }
   await page.close();
  }
  check(!report.errors.length&&!report.missing.length,'no errors or missing assets');
  const target=phase==='after'?'maps/eastasia_rewrite/coast-correction-browser-20261027.json':path.join(out,'checks.json');
  await fs.writeFile(target,JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify({checks:report.checks,views:report.views.length,glyphCaches:report.glyphs.length,errors:report.errors.length,missing:report.missing.length}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
