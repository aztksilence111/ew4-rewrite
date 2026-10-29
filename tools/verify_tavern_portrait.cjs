'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2],folder=path.resolve(process.argv[3]||'build/tavern-portrait-check-20261029');
(async()=>{
  const fixture=JSON.parse(await fs.readFile(path.join(folder,'fixture.json'),'utf8'));
  const out=path.join(folder,'browser');await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],cases:[]},check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const [renderer,width,height] of [['pixi',1280,720],['canvas',1280,720],['pixi',844,390],['canvas',390,844]]){
      const page=await browser.newPage({viewport:{width,height}});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(async()=>{await installState(await rpc('load',{slot:1,mode:2}),true);stageIntroOpen=false;});
      await page.evaluate(async f=>{await selectCell(f.cell,true,true);await menus.tavern(f.cell);},fixture);
      const button=page.locator('[data-native-id="btn_rec_1"]');
      check(await button.count()===1,'real tavern recruit button present '+renderer+width);
      await button.click();await page.waitForFunction(f=>!busy&&state.units.find(u=>u.id===f.unit)?.commanderId===f.commander,fixture);
      check(await page.evaluate(f=>{const u=state.units.find(u=>u.id===f.unit);return u.commanderImage===f.image&&u.commanderDefinitionId===f.definition;},fixture),'hire installs map image and identity '+renderer+width);
      await page.evaluate(()=>{detailReturn=null;closeDetail();});
      for(const zoom of [1.5,.3]){
        const result=await page.evaluate(async({fixture,zoom})=>{
          const unit=state.units.find(u=>u.id===fixture.unit),p=point(unit.cell);view.x=p.x;view.y=p.y;view.zoom=zoom;clampView();
          window.portraitDraws=[];
          const original=battlefield.api.sprite;
          battlefield.api.sprite=function(atlas,name,...args){if(atlas==='commandermark')portraitDraws.push(name);return original.call(this,atlas,name,...args);};
          render(performance.now(),true);battlefield.api.sprite=original;
          const surface=document.createElement('canvas');surface.width=160;surface.height=180;const draw=surface.getContext('2d'),names=[];
          const marker=new Ew4Battlefield.Renderer({...battlefield.api,sprite:(atlas,name,x,y)=>{names.push(name);sprite(atlas,name,x,y,1,draw);}});
          Object.assign(marker,{draw,state:{...state,units:[unit]},factions:new Map(state.factions.map(f=>[f.id,f])),visibleUnitMarkers:[{unit,p:{x:80,y:130}}],badgeOnly:zoom<.5,markerScale:1});
          marker.unitMarkers();const pixels=draw.getImageData(40,30,80,110).data;let alpha=0,white=0;
          for(let i=0;i<pixels.length;i+=4){if(pixels[i+3]>0)alpha++;if(pixels[i]>220&&pixels[i+1]>220&&pixels[i+2]>220&&pixels[i+3]>100)white++;}
          return {draws:portraitDraws,names,alpha,white,image:unit.commanderImage,badgeOnly:battlefield.badgeOnly};
        },{fixture,zoom});
        check(result.draws.includes(fixture.image+'.png')&&result.draws.includes('board_smallgenerals.png'),'actual map draws portrait and frame '+renderer+width+zoom+' '+JSON.stringify(result));
        check(result.names.includes(fixture.image+'.png')&&result.alpha>300&&result.white>30,'isolated portrait/frame pixels visible '+renderer+width+zoom);
        check(result.badgeOnly===(zoom<.5),'normal and zoomed-out marker paths '+renderer+width+zoom);
        const png=PNG.sync.read(await page.screenshot({path:path.join(out,renderer+'-'+width+'-'+zoom+'.png')}));
        const colors=new Set();for(let i=0;i<png.data.length;i+=64)colors.add(png.data.subarray(i,i+3).toString('hex'));
        check(colors.size>100,'nonblank battlefield '+renderer+width+zoom);report.cases.push({renderer,width,height,zoom,alpha:result.alpha,white:result.white});
      }
      await page.evaluate(async()=>{await installState(await rpc('load',{slot:0,mode:2}),true);});
      check(await page.evaluate(f=>state.units.find(u=>u.id===f.unit)?.commanderImage===f.image,fixture),'autosave reload keeps marker '+renderer+width);
      await page.close();
    }
    check(report.errors.length===0,'no browser exceptions');check(report.missing.length===0,'no missing assets');
    await fs.writeFile(path.join(out,'verification.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
