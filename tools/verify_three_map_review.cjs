'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5585',output=path.resolve('build/map-review-tests-20261027/browser');
(async()=>{
  await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  let checks=0;const errors=[],missing=[],views=[],lettering=[];
  const check=(value,label)=>{assert.ok(value,label);checks++;};
  try{
    for(const renderer of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width:1440,height:900}});
      page.on('pageerror',e=>errors.push(e.message));page.on('response',r=>{if(r.status()>=400)missing.push([r.status(),r.url()]);});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));
      await page.waitForFunction(()=>startupReady,null,{timeout:180000});
      await page.locator('[data-mode="2"]').click();
      await page.locator('#conquest-next').click();
      await page.waitForFunction(()=>!conquestEntering);
      check(await page.locator('[data-scenario]').count()===3,'three review choices on one page');
      check(JSON.stringify(await page.locator('[data-scenario]').evaluateAll(rows=>rows.map(r=>r.dataset.scenario)))===JSON.stringify(['conquest7','conquest8','conquest9']),'East Asia, Japan and HRE share the review page');
      await page.waitForFunction(()=>[...document.querySelectorAll('.scenario-art')].every(i=>i.complete&&i.naturalWidth>0));
      if(renderer==='pixi')await page.screenshot({path:path.resolve('maps/japan_hre_source_20261026/game-review-menu.jpg'),type:'jpeg',quality:88});
      // Start Japan through the actual scenario/country controls, including its strategic preview.
      await page.locator('[data-scenario="conquest8"]').click();
      await page.waitForFunction(()=>!busy&&chosenCountry);
      check(await page.locator('#confirm-country').isEnabled(),'country selection ready');
      await page.locator('#confirm-country').click();
      const idle=()=>page.waitForFunction(()=>!!state&&!busy&&!sessionTransition&&!aiRunning,null,{timeout:180000});
      await idle();
      const agriculture=JSON.parse(await fs.readFile('maps/japan_hre_source_20261026/agriculture-summary.json','utf8'));
      for(const [scenario,key,total,buildings,anchor] of [['conquest8','japan',57939,257+agriculture.japan.farms,'飞驒山脉'],['conquest9','hre',52461,385+agriculture.hre.farms,'阿尔卑斯山脉'],['conquest7','eastasia',21488,521,'喜马拉雅山脉']]){
        if(await page.evaluate(()=>state.scenario)!==scenario){await page.evaluate(id=>start(id,'fra'),scenario);await idle();}
        const stats=await page.evaluate(()=>({map:state.map,total:state.cells.length,buildings:state.cells.filter(c=>c.buildingKind).length,gpu:!!ctx.gpu,high:state.cells.filter((c,i)=>i>32767&&c.buildingKind).length}));
        check(stats.map===key&&stats.total===total&&stats.buildings===buildings,'actual client map and complete buildings '+JSON.stringify(stats));
        check(stats.gpu===(renderer==='pixi'),'requested renderer '+renderer);
        if(key!=='eastasia')check(stats.high>30,'buildings beyond legacy signed cell range remain visible');
        for(const [width,height] of [[1440,900],[390,844]]){
          await page.setViewportSize({width,height});
          await page.evaluate(name=>{
            const label=manifest.maps[state.map].labels.find(l=>l.textZh===name)||manifest.maps[state.map].labels[0];
            settings.geographicLanguage='zh';
            view.x=Number(label.tx);view.y=Number(label.ty);view.zoom=Math.min(.7,innerWidth/(label.width+500),innerHeight/(label.height+400));
            clampView();battlefield.invalidateGround();requestRender();
          },anchor);
          await page.waitForTimeout(700);
          await page.waitForFunction(()=>[...images.values()].every(i=>i.complete&&i.naturalWidth>0),null,{timeout:60000});
          const pixels=await page.evaluate(()=>{
            render(performance.now(),true);
            const data=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;
            const colors=new Set();let filled=0;for(let i=0;i<data.length;i+=64){if(data[i+3])filled++;colors.add(data[i]+','+data[i+1]+','+data[i+2]);}
            return {colors:colors.size,filled,overflow:document.documentElement.scrollWidth>innerWidth+1};
          });
          check(pixels.colors>150&&pixels.filled>1000&&!pixels.overflow,'nonblank game canvas and viewport '+JSON.stringify(pixels));
          if(renderer==='pixi'&&width===1440&&key!=='eastasia')await page.screenshot({path:path.resolve('maps/japan_hre_source_20261026/game-'+key+'.jpg'),type:'jpeg',quality:90});
          else await page.screenshot({path:path.join(output,`${renderer}-${key}-${width}.png`)});
          views.push({renderer,key,width,height,...pixels});
        }
        if(key!=='eastasia'){
          const glyphs=await page.evaluate(async()=>{
            const labels=manifest.maps[state.map].labels,rows=[];
            await Promise.all(labels.map(l=>Ew4OriginalUI.prepareMapText(l.textZh,!l.sea)));
            for(const language of ['en','zh']){
              const r=Object.create(Ew4Battlefield.Renderer.prototype);
              Object.assign(r,{api:{...battlefield.api,geographicLanguage:language},map:{labels},mapLabels:new Map(),
                labelIndex:{query:()=>labels},visibleRect:{left:0,top:0,width:30000,height:30000},
                draw:document.createElement('canvas').getContext('2d')});
              r.mapText();
              for(const l of labels){
                const cached=r.mapLabels.get(l.libName+':1:'+l.opacity+':'+language),c=cached.canvas;
                const data=c.getContext('2d').getImageData(0,0,c.width,c.height).data;
                let ink=0,edge=0;for(let y=0;y<c.height;y++)for(let x=0;x<c.width;x++)if(data[(y*c.width+x)*4+3]){
                  ink++;if(!x||!y||x===c.width-1||y===c.height-1)edge++;
                }
                const path=Ew4Battlefield.textPath(l.textPath),angles=path.samples.map(p=>p.x);
                rows.push({text:l.text,language,ink,edge,font:c.getContext('2d').font,
                  regional:l.geographicStyle==='regional',length:path.length,spread:Math.max(...angles)-Math.min(...angles)});
              }
            }
            return rows;
          });
          for(const row of glyphs){
            check(row.ink>0&&row.edge===0&&row.regional,'complete curved glyph canvas '+key+' '+JSON.stringify(row));
            if(row.language==='zh')check(row.font.includes(row.text.includes('SEA')||row.text.includes('OCEAN')?'EW4 Kai':'EW4 Serif'),'same bundled Chinese map fonts as East Asia');
          }
          lettering.push({renderer,key,labels:glyphs});
          const actual=await page.evaluate(()=>{
            const previous={draw:battlefield.draw,zoom:battlefield.zoom,rect:battlefield.visibleRect},sprite=battlefield.api.sprite;
            const results=[];const probe=document.createElement('canvas').getContext('2d');
            battlefield.draw=probe;battlefield.visibleRect={left:-200,top:-200,width:30000,height:30000};
            battlefield.api.sprite=(atlas,name,x,y,scale,draw)=>{const matrix=draw.getTransform();results.push({name,sx:matrix.a,sy:matrix.d});};
            try{
              battlefield.zoom=.31;battlefield.terrain(false,false);battlefield.terrain(false,true);const near=results.splice(0);
              battlefield.zoom=.29;battlefield.terrain(false,false);battlefield.terrain(false,true);const far=results.splice(0);
              const unique=new Set(near.map(r=>r.name)),sizes=new Set(near.filter(r=>r.name.startsWith('mountain_')).map(r=>r.sy));
              return {native:near.every(r=>/^(hill|mountain)_[1-6]\.png$/.test(r.name)),equal:near.every(r=>r.sx===r.sy),materials:unique.size,tiers:sizes.size,near:near.length,far:far.length};
            }finally{Object.assign(battlefield,{draw:previous.draw,zoom:previous.zoom,visibleRect:previous.rect});battlefield.api.sprite=sprite;}
          });
          check(actual.native&&actual.equal&&actual.materials===12&&actual.tiers===3&&actual.near>actual.far,'actual native draws preserve all original shapes, equal proportions and LOD '+JSON.stringify(actual));
          if(renderer==='pixi'&&key==='hre'){
            await page.setViewportSize({width:1440,height:900});
            await page.evaluate(async()=>{
              settings.geographicLanguage='zh';await prepareMapFonts(state);
              const labels=manifest.maps.hre.labels.filter(l=>['西阿尔卑斯山脉','奔宁阿尔卑斯山脉','雷蒂亚阿尔卑斯山脉','多洛米蒂山脉','利古里亚亚平宁山脉'].includes(l.textZh));
              const left=Math.min(...labels.map(l=>l.left)),right=Math.max(...labels.map(l=>l.left+l.width)),top=Math.min(...labels.map(l=>l.top)),bottom=Math.max(...labels.map(l=>l.top+l.height));
              view={x:(left+right)/2,y:(top+bottom)/2,zoom:Math.min(innerWidth/(right-left+350),innerHeight/(bottom-top+250))};clampView();battlefield.invalidateGround();requestRender();
            });
            await page.waitForTimeout(700);
            await page.screenshot({path:path.resolve('maps/japan_hre_source_20261026/game-hre-italy.jpg'),type:'jpeg',quality:92});
          }
        }
      }
      await page.close();
    }
    check(errors.length===0,'no browser exceptions '+JSON.stringify(errors));check(missing.length===0,'no missing resources '+JSON.stringify(missing));
    await fs.writeFile(path.resolve('maps/japan_hre_source_20261026/game-browser-verification.json'),JSON.stringify({checks,errors,missing,views,
      lettering:lettering.map(({renderer,key,labels})=>({renderer,key,canvasChecks:labels.length,names:[...new Set(labels.map(l=>l.text))],clipped:labels.filter(l=>l.edge).length,blank:labels.filter(l=>!l.ink).length}))},null,2)+'\n');
    console.log(JSON.stringify({checks,errors,missing,views:views.length}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
