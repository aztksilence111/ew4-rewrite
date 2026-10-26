'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2]||'http://127.0.0.1:5532',folder=path.resolve('build/eastasia-feedback-20261026/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const cities=JSON.parse(await fs.readFile('maps/eastasia_rewrite/cities.json','utf8'));
  const facilities=JSON.parse(await fs.readFile('maps/eastasia_rewrite/facilities.json','utf8'));
  const coverage=JSON.parse(await fs.readFile('maps/eastasia_rewrite/province_coverage.json','utf8')).provinces;
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,cases:[],errors:[],missing:[]},check=(v,m)=>{assert.ok(v,m);report.checks++;};
  try{
    for(const [name,width,height] of [['desktop',1280,720],['phone',844,390],['portrait',390,844]])for(const backend of ['canvas','pixi']){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      await page.evaluate(()=>start('conquest7','fra'));await page.waitForFunction(()=>ctx&&[...images.values()].every(i=>i.complete));
      const actual=await page.evaluate(async()=>{
        stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();settings.geographicLanguage='zh';settings.cityLanguage='zh';
        await Ew4OriginalUI.prepareMapText('斋桑湖齐齐哈尔马格达加奇唐山衢州银川',true);
        return {map:state.map,gpu:!!ctx.renderer,cells:state.cells.map(c=>({name:c.name,kind:c.buildingKind,level:c.buildingLevel}))};
      });
      check(actual.map==='eastasia'&&actual.gpu===(backend==='pixi'),'actual renderer and map');
      for(const c of cities){const s=actual.cells[c.cell];check(s.name===c.name&&s.kind===c.kind&&s.level===c.building_level,'named site '+c.name);}
      for(const f of facilities){const s=actual.cells[f.cell];check(s.kind===f.kind&&s.level===f.level,'facility '+f.id);}
      for(const p of coverage){check(p.industry.some(i=>actual.cells[i].kind==='industry')&&p.stable.some(i=>actual.cells[i].kind==='stable'),'province '+p.name);}
      const regions=[['zaysan',null,450],['qiqihar','齐齐哈尔',950],['stanovoy','马格达加奇',1550],
        ...(name==='desktop'?[['tangshan','唐山',780],['zhejiang','衢州',1050],['ningxia','中卫',1300]]:[])];
      for(const [region,city,span] of regions){
        const position=city?cities.find(c=>c.name===city).world_pixel:[2173.8,1517.35];
        await page.evaluate(({position,span})=>{
          Object.assign(view,{x:position[0],y:position[1],zoom:Math.min(innerWidth/span,innerHeight/(span*.65))});
          clampView();battlefield.invalidateGround();render(2000);suspendRendering();
        },{position,span});
        const buffer=await page.screenshot({path:path.join(folder,`${name}-${backend}-${region}.png`)}),png=PNG.sync.read(buffer),colors=new Set();
        for(let i=0;i<png.data.length;i+=76)colors.add(png.data.subarray(i,i+3).toString('hex'));
        check(colors.size>100,'nonblank '+region);
      }
      await page.evaluate(()=>{settings.geographicLanguage='en';settings.cityLanguage='en';battlefield.invalidateGround();render(2000);suspendRendering();});
      check(await page.evaluate(()=>manifest.maps.eastasia.cityNames[Object.keys(manifest.maps.eastasia.cityNames).find(i=>manifest.maps.eastasia.cityNames[i].zh==='马格达加奇')].en==='Magdagachi'),'new town bilingual name');
      report.cases.push({name,backend,sites:cities.length+facilities.length});await page.close();
    }
    check(!report.errors.length,'no browser errors');check(!report.missing.length,'no missing resources');
  }finally{await fs.writeFile(path.join(folder,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
