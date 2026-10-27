'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5585',output='build/map-review-tests-20261027/water';
(async()=>{
  await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  const report={checks:0,errors:[],missing:[],maps:[],views:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  try{
    for(const renderer of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width:1440,height:900}});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:180000});
      for(const [key,scenario] of [['japan','conquest8'],['hre','conquest9']]){
        const sites=JSON.parse(await fs.readFile('maps/'+key+'/buildings.json','utf8'));
        const farms=JSON.parse(await fs.readFile('maps/'+key+'/farmland.json','utf8')).records;
        const water=JSON.parse(await fs.readFile('maps/japan_hre_source_20261026/'+key+'-water.json','utf8'));
        await page.evaluate(s=>start(s,'fra'),scenario);await page.waitForFunction(()=>state&&!busy&&!sessionTransition,null,{timeout:180000});
        const stats=await page.evaluate(()=>{
          const farms=state.cells.filter(c=>c.buildingKind==='farmland'),ports=state.cells.filter(c=>c.buildingKind==='port');
          return {map:state.map,levels:farms.reduce((o,c)=>(o[c.buildingLevel]=(o[c.buildingLevel]||0)+1,o),{}),
            farms:farms.length,validImages:farms.every(c=>c.buildingImage==='farmland'+c.buildingLevel+'.png'),dry:farms.every(c=>!c.sea),
            styles:[...new Set(ports.map(c=>c.buildingStyle))].sort(),loadedDetails:[...images.keys()].filter(p=>p.includes('/'+state.map+'/water-detail/')).length,
            detailTiles:manifest.maps[state.map].detailTiles.length,detailZoom:manifest.maps[state.map].waterDetailZoom};
        });
        check(stats.farms===farms.length&&stats.validImages&&stats.dry,'actual three-tier native farmland '+JSON.stringify(stats));
        check(JSON.stringify(Object.keys(stats.levels))===JSON.stringify(['1','2','3']),'all three farmland levels');
        check(JSON.stringify(stats.styles)===JSON.stringify([1,2,3,4]),'all four shore-facing native ports');
        check(stats.detailTiles>150&&stats.loadedDetails<stats.detailTiles/4,'initial camera only loads local near-water tiles');
        report.maps.push({renderer,...stats,mouths:water.mouths.length,unresolved:water.unresolvedMouths});
        const points=[];
        for(const style of [1,2,3,4]){const s=sites.find(s=>s.kind==='port'&&s.building_style===style&&!s.river_berth);points.push({name:s.name,point:s.world_pixel,type:'port-'+style});}
        const major=key==='japan'?'吉野川':'波河';const mouth=water.mouths.find(m=>m.name===major)||water.mouths.find(m=>m.name!=='主要支流水系');
        if(mouth)points.push({name:mouth.name,point:mouth.endpoint,type:'mouth'});
        if(key==='hre')points.push({name:'意大利北部',point:[5600,11700],type:'italy'});
        for(const size of [[1440,900],[390,844]]){
          await page.setViewportSize({width:size[0],height:size[1]});
          for(const p of points){
            await page.evaluate(({point,type})=>{view={x:point[0],y:point[1],zoom:type==='italy'?.5:1.8};clampView();battlefield.invalidateGround();requestRender();},p);
            await page.waitForTimeout(300);
            await page.waitForFunction(()=>[...images.values()].every(i=>i.complete&&i.naturalWidth>0),null,{timeout:60000});
            await page.waitForTimeout(100);
            const pixels=await page.evaluate(()=>{
              render(performance.now(),true);const data=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;
              const colors=new Set();for(let i=0;i<data.length;i+=64)colors.add(data[i]+','+data[i+1]+','+data[i+2]);
              return {colors:colors.size,retained:waterImages.size,details:[...images.keys()].filter(p=>p.includes('/'+state.map+'/water-detail/')).length,overflow:document.documentElement.scrollWidth>innerWidth+1};
            });
            check(pixels.colors>100&&pixels.retained<=64&&!pixels.overflow,'rendered native harbour / smooth river '+JSON.stringify(pixels));
            if(p.type!=='italy')check(pixels.details>0&&pixels.details<stats.detailTiles,'water image load remains local');
            report.views.push({renderer,key,width:size[0],height:size[1],name:p.name,...pixels});
            if(renderer==='pixi')await page.screenshot({path:path.join(output,key+'-'+p.type+'-'+size[0]+'.jpg'),type:'jpeg',quality:90});
          }
        }
        if(key==='hre'){
          const labels=await page.evaluate(()=>manifest.maps.hre.labels.map(l=>l.textZh));
          for(const name of ['西阿尔卑斯山脉','奔宁阿尔卑斯山脉','雷蒂亚阿尔卑斯山脉','多洛米蒂山脉','利古里亚亚平宁山脉'])check(labels.includes(name),'Italian mountain subregion '+name);
        }
        await page.setViewportSize({width:1440,height:900});
      }
      await page.close();
    }
    check(!report.errors.length&&!report.missing.length,'no script errors or missing tiles '+JSON.stringify({errors:report.errors,missing:report.missing}));
    console.log(JSON.stringify({checks:report.checks,errors:report.errors,missing:report.missing,maps:report.maps,views:report.views.length}));
  }finally{
    await fs.writeFile('maps/japan_hre_source_20261026/water-farm-browser-verification.json',JSON.stringify(report,null,2)+'\n');
    await browser.close();
  }
})().catch(e=>{console.error(e);process.exitCode=1;});
