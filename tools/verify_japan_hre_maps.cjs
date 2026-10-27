'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {pathToFileURL}=require('node:url'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root=path.resolve(__dirname,'..'),output=path.join(root,'build/japan-hre-source-20261026/browser');
let stage='startup';
(async()=>{
  await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio','--allow-file-access-from-files']});
  let checks=0;const results=[],errors=[],failures=[];
  const check=(value,message)=>{assert.ok(value,message);checks++;};
  try{
    for(const [width,height] of [[1440,1000],[900,700],[390,844],[844,390],[360,640]]){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1});
      page.on('pageerror',e=>errors.push(e.message));page.on('requestfailed',r=>failures.push(r.url()));
      await page.goto(pathToFileURL(path.join(root,'maps/japan_hre_source_20261026/index.html')).href);
      await page.waitForFunction(()=>Object.values(atlases).every(i=>i.complete&&i.naturalWidth>0));
      for(const [key,total,query] of [['japan',257,'京都'],['hre',385,'科隆']]){
        stage=`${key}/${width}x${height}`;
        await page.locator('#'+key+'-tab').click();
        await page.waitForFunction(()=>image.complete&&image.naturalWidth>0);
        check(await page.locator('.point-row').count()===total,'all buildings '+stage);
        check(await page.evaluate(()=>map.totalCells===map.gridWidth*map.gridHeight&&map.landCells+map.waterCells===map.totalCells&&$('#map-status').textContent.includes(map.totalCells.toLocaleString('zh-CN'))),'consistent full-grid total '+stage);
        const stats=await page.evaluate(()=>{
          const pixels=ctx.getImageData(0,0,canvas.width,canvas.height).data;let land=0,sea=0;const colors=new Set();
          for(let i=0;i<pixels.length;i+=16){if(pixels[i]>100&&pixels[i+1]>100&&pixels[i+2]>100&&Math.abs(pixels[i]-pixels[i+2])<45)land++;if(pixels[i]<90&&pixels[i+1]>70&&pixels[i+2]>110)sea++;colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);}
          return {land,sea,colors:colors.size,overflow:document.documentElement.scrollWidth>innerWidth||document.documentElement.scrollHeight>innerHeight+2};
        });
        check(stats.land>200&&stats.sea>1000&&stats.colors>100,'actual land, water and original sprites '+JSON.stringify(stats));
        check(!stats.overflow,'no viewport overflow');
        const cartography=await page.evaluate(()=>({plainLabels:geographicLabels.filter(l=>l.category==='plain').length,
          reliefCount:map.overlays.filter(o=>[8,9].includes(o.cellKind)).length,expected:Array.from({length:map.totalCells},(_,c)=>terrainAt(c)).filter(t=>[8,9].includes(t.kind)).length,rivers:map.rivers.length,base:map.cartography.base}));
        check(cartography.reliefCount===cartography.expected&&cartography.rivers>0&&cartography.base==='native-game-texture','every classified terrain cell has native artwork '+JSON.stringify(cartography));
        if(width>=900)check(cartography.plainLabels>=(width>=1440?2:1),'plains remain identifiable '+JSON.stringify(cartography));
        if(width===1440||width===390)await page.screenshot({path:path.join(output,`${key}-${width}-overview.png`)});
        if(['japan','hre'].includes(key)){
          const hierarchy=await page.evaluate(()=>{
            const original=sprite,calls=[];
            const at=new Map(map.overlays.filter(o=>o.cellKind===9).map(o=>[`${o.x},${o.y}`,o.relief]));
            sprite=(atlas,name,x,y,sx=1,sy=1,opacity=1)=>{if(atlas==='tiles'&&name.startsWith('mountain_'))calls.push({name,level:at.get(`${x},${y}`),sx,sy,h:data.atlases.tiles[name].h*sy});original(atlas,name,x,y,sx,sy,opacity);};
            try{
              const anchor=map.labels.find(l=>l.textZh===(mapKey==='japan'?'飞驒山脉':'阿尔卑斯山脉'));
              const capture=value=>{setScale(value);tx=surface.clientWidth/2-anchor.x*scale;ty=surface.clientHeight/2-anchor.y*scale;calls.length=0;draw();return [...calls];};
              const far=capture(.29),near=capture(.31);
              const bands={};for(const level of ['low','middle','high']){
                const heights=far.filter(r=>r.level===level).map(r=>r.h);
                bands[level]={count:heights.length,average:heights.reduce((a,b)=>a+b,0)/heights.length};
              }
              const stable=far.every(r=>near.some(n=>n.name===r.name&&n.sx===r.sx&&n.sy===r.sy));
              return {bands,stable,legend:[...document.querySelectorAll('#terrain-legend span')].map(e=>e.textContent)};
            }finally{sprite=original;fit();}
          });
          check(hierarchy.stable,'band geometry survives overview transition');
          check(hierarchy.bands.low.count>0&&hierarchy.bands.middle.count>0&&hierarchy.bands.high.count>0,'all mountain tiers visible near Alps');
          check(hierarchy.bands.low.average<hierarchy.bands.middle.average&&hierarchy.bands.middle.average<hierarchy.bands.high.average,'actual canvas draws increasing native size tiers');
          check(hierarchy.legend.join(',')==='平地,丘陵,低山,中山,高山','five terrain tiers in legend');
          check(await page.evaluate(()=>!map.ridgeOverlays&&map.overlays.filter(o=>[8,9].includes(o.cellKind)).every(o=>o.image===map.terrainRules[o.cellKind].sprites[o.cellIndex]&&o.scaleX===o.scaleY)),'original terrain sprites without deformation or infill');
          if(width===1440||width===390){
            await page.evaluate(()=>{const anchor=map.labels.find(l=>l.textZh===(mapKey==='japan'?'飞驒山脉':'阿尔卑斯山脉'));setScale(fitScale*10);tx=surface.clientWidth/2-anchor.x*scale;ty=surface.clientHeight/2-anchor.y*scale;draw();});
            await page.screenshot({path:path.join(output,`${key}-${width}-relief-bands.png`)});
            await page.locator('#fit').click();
          }
        }
        await page.locator('#plus').click();check(Number(await page.locator('#zoom').inputValue())>100,'zoom works');
        await page.locator('#fit').click();check(await page.locator('#zoom').inputValue()==='100','fit works');
        for(const kind of ['city','stable','port'])await page.locator(`.kinds input[value=${kind}]`).uncheck();
        check(await page.locator('.point-row').count()===(key==='japan'?20:36),'kind filter');
        for(const kind of ['city','stable','port'])await page.locator(`.kinds input[value=${kind}]`).check();
        await page.locator('#search').fill(query);check(await page.locator('.point-row').count()===1,'search');
        await page.locator('.point-row').click();await page.locator('#grid').check();
        const selectedState=await page.evaluate(()=>({name:selected.name,empty:selected.empty_land_neighbors,zoom:scale/fitScale,landOnly:map.records.filter(p=>p.kind!=='port'&&p.id!==selected.id).every(p=>distance(p.cell,selected.cell)>=(p.kind===selected.kind&&p.kind==='city'?map.policy.cityMinimumHexDistance:map.policy.minimumHexDistance)),overflow:document.documentElement.scrollHeight>innerHeight+2}));
        check(selectedState.name===query&&selectedState.empty>=1&&selectedState.landOnly&&selectedState.zoom>=9.9&&!selectedState.overflow,'real city anchors and deployment space '+JSON.stringify(selectedState));
        check(await page.evaluate(()=>screenPoints.length>1),'search retains neighboring buildings for comparison');
        const withRange=await page.evaluate(()=>ctx.getImageData(0,0,canvas.width,canvas.height).data.reduce((a,b)=>a+b,0));
        await page.locator('#deployment').uncheck();
        const withoutRange=await page.evaluate(()=>ctx.getImageData(0,0,canvas.width,canvas.height).data.reduce((a,b)=>a+b,0));
        check(withRange!==withoutRange,'deployment overlay renders');await page.locator('#deployment').check();
        if(width===1440||width===390)await page.screenshot({path:path.join(output,`${key}-${width}-deployment.png`)});
        for(const [kind,cost,type] of [[0,3,'land'],[8,5,'hill'],[9,6,'mountain']]){
          const target=await page.evaluate(kind=>{
            const candidate=map.overlays.find(o=>o.cellKind===kind&&!map.records.some(p=>distance(p.cell,o.cell)<2));
            const cell=candidate?.cell??Array.from({length:map.totalCells},(_,c)=>c).find(c=>terrainAt(c).kind===kind&&!map.records.some(p=>distance(p.cell,c)<2));
            selected=null;inspectedCell=null;setScale(fitScale*12);const [x,y]=point(cell);tx=surface.clientWidth/2-x*scale;ty=surface.clientHeight/2-y*scale;draw();return {cell,x:surface.clientWidth/2,y:surface.clientHeight/2};
          },kind);
          const rect=await page.locator('#map').boundingBox();await page.mouse.click(rect.x+target.x,rect.y+target.y);
          check(await page.locator('.terrain-properties').getAttribute('data-cell')===String(target.cell),'click any actual terrain cell');
          check(await page.locator('.terrain-properties').getAttribute('data-type')===type&&await page.locator('.terrain-properties').getAttribute('data-cost')===String(cost),'native movement property '+type);
          check(await page.locator('.terrain-properties').textContent().then(t=>t.includes('兵种惩罚')&&t.includes('河流／通桥')),'combat and river properties visible');
          if(kind===9&&(width===1440||width===390))await page.screenshot({path:path.join(output,`${key}-${width}-mountain.png`)});
        }
        await page.locator('#search').fill('no-such-place-20261026');check(await page.locator('.point-row').count()===0,'empty search');
        await page.locator('#search').fill('');await page.locator('#fit').click();
        if(key==='japan'){
          const beforeBorders=await page.evaluate(()=>ctx.getImageData(0,0,canvas.width,canvas.height).data.reduce((a,b)=>a+b,0));
          await page.locator('#provinces').uncheck();
          check(beforeBorders!==await page.evaluate(()=>ctx.getImageData(0,0,canvas.width,canvas.height).data.reduce((a,b)=>a+b,0)),'real old-country boundaries render');
          await page.locator('#provinces').check();
          await page.locator('#search').fill('伊贺');check(await page.locator('.point-row').count()>=1,'historical anchor and aliases searchable');
          await page.locator('#search').fill('杂太佐渡');await page.locator('.point-row').click();
          check(await page.locator('#selected').innerText().then(t=>t.includes('佐渡国')&&t.includes('雑太城')),'island historical node and province visible');
          await page.locator('#search').fill('');await page.locator('#fit').click();
          await page.locator('#region').selectOption('朝鲜半岛');check(await page.locator('.point-row').count()===23,'sparse Korea preserved');await page.locator('#region').selectOption('');
          await page.locator('#search').fill('符拉迪沃斯托克');check(await page.locator('.point-row').count()===1,'Vladivostok alias');await page.locator('#search').fill('');
        }else{
          for(const name of ['诺里奇','维斯比','叶尔加瓦','比亚韦斯托克','贝尔格莱德','加茨科','纳博讷']){
            await page.locator('#search').fill(name);check(await page.locator('.point-row').count()===1,'circled building '+name);
          }
          await page.locator('#search').fill('');
        }
        await page.locator('#grid').uncheck();
        results.push({key,width,height,total,landPixels:stats.land,colors:stats.colors});
      }
      await page.close();
    }
    check(errors.length===0&&failures.length===0,'no browser or asset errors '+JSON.stringify({errors,failures}));
    console.log(JSON.stringify({checks,errors:0,views:results}));
  }finally{await browser.close();}
})().catch(e=>{console.error(stage+': '+e.message);process.exitCode=1;});
