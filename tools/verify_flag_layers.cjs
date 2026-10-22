'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),cp=require('node:child_process'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5371',folder=process.argv[3]||'build/flag-layer-20261022';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}}),report={checks:0,errors:[],missing:[],cases:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    await page.evaluate(()=>start('campaign1_02b',null));
    const baseline=cp.execFileSync('git',['show','HEAD:rewrite/Ew4.Web/wwwroot/battlefield.js'],{encoding:'utf8'});
    const results=await page.evaluate(async source=>{
      const current=Ew4Battlefield;(0,eval)(source);const Previous=Ew4Battlefield.Renderer;globalThis.Ew4Battlefield=current;
      const ship=state.units.find(u=>u.category==='naval'),soldier=state.units.find(u=>u.category==='infantry');
      if(!ship||!soldier)throw new Error('Naval and infantry fixtures missing');
      for(const unit of [ship,soldier])await loadLibrary(visualProfile(unit).library);
      await Promise.all(['buildings','buildings_hd'].map(name=>image('images/'+name+'.png').decode()));
      const faction={...state.factions.find(f=>f.id===ship.owner),flag:'tur',flagImage:null};
      const cases=[];
      for(const mode of ['naval-right','naval-left','transport','moving','dying']){
        const units=[18,19,26].map((cell,i)=>({...(mode==='transport'?soldier:ship),id:'flag.'+i,cell,owner:faction.id,
          facing:mode==='naval-left'?0:1,commanderImage:null,burning:0,veterancy:0,constructionTurns:0,transport:mode==='transport'}));
        const cells=Array.from({length:64},()=>({active:true,sea:true,owner:null,buildingKind:null}));
        cells[27]={...cells[27],owner:faction.id,buildingKind:'port'};
        const fixture={...state,width:8,height:8,units,cells,factions:[faction]},motions=new Map(),deaths=new Map();
        if(mode==='moving')motions.set(units[1].id,{path:[19,20],started:0,stepMs:300});
        if(mode==='dying')deaths.set(units[1].id,{started:0,duration:500});
        function paint(Renderer){
          const surface=document.createElement('canvas');surface.width=640;surface.height=420;
          const d=surface.getContext('2d'),events=[];
          const api={...battlefield.api,effects:{profile:(...args)=>effects.profile(...args),motion:(...args)=>effects.motion(...args),frame(){},trail(){}},
            sprite:(atlas,name,x,y,scale=1,draw=d)=>sprite(atlas,name,x,y,scale,draw),
            animation:(lib,item,tick,depth=0,draw=d)=>animation(lib,item,tick,depth,draw)};
          const r=new Renderer(api);for(const name of ['staticGround','objectives','labels','highlights'])r[name]=()=>{};
          for(const name of ['units','flags','unitMarkers'])if(r[name]){const original=r[name];r[name]=function(...args){events.push(name);return original.apply(this,args);};}
          d.fillStyle='#688e91';d.fillRect(0,0,640,420);
          r.frame({draw:d,state:fixture,map:{},visibleRect:{left:0,top:0,width:640,height:420},zoom:1,
            motions,deaths,facings:new Map(),arrivals:new Map(),combatMotions:new Map()},150);
          const before=d.getImageData(0,0,640,420).data;
          // An opaque flag pixel must be unchanged when its fabric is drawn again on top.
          const mask=document.createElement('canvas');mask.width=640;mask.height=420;const m=mask.getContext('2d');
          for(const {unit,p} of r.visibleUnitMarkers||units.filter(u=>!deaths.has(u.id)).map(unit=>({unit,p:motions.has(unit.id)?current.pose(motions.get(unit.id),8,150):current.point(unit.cell,8)}))){
            const anchor=current.flagPosition(p);sprite('buildings',faction.flag+(current.flagAnimated(unit,fixture)?current.flagFrame(150):1)+'.png',anchor.x,anchor.y,1,m);
          }
          const anchor=current.flagPosition(current.point(27,8));sprite('buildings',faction.flag+current.flagFrame(150)+'.png',anchor.x,anchor.y,1,m);
          const flags=m.getImageData(0,0,640,420).data;let opaque=0,covered=0;
          for(let i=0;i<flags.length;i+=4)if(flags[i+3]===255){opaque++;if([0,1,2].some(c=>Math.abs(flags[i+c]-before[i+c])>3))covered++;}
          return {opaque,covered,events,markers:r.visibleUnitMarkers?.map(v=>v.unit.id),png:surface.toDataURL('image/png')};
        }
        cases.push({mode,before:paint(Previous),after:paint(current.Renderer)});
      }
      return cases;
    },baseline);
    for(const result of results){
      for(const version of ['before','after']){const sample=result[version];await fs.writeFile(folder+'/'+result.mode+'-'+version+'.png',Buffer.from(sample.png.split(',')[1],'base64'));delete sample.png;}
      report.cases.push(result);
      check(result.after.opaque>100,result.mode+' has visible flag fabric');
      check(result.before.covered>0,result.mode+' reproduces occlusion before fix');
      check(result.after.covered===0,result.mode+' flag fabric is completely above models');
      check(result.after.events.join(',')==='units,flags,unitMarkers',result.mode+' overlay order');
      check(result.after.markers.length===(result.mode==='dying'?2:3),result.mode+' living markers only');
    }
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});
      await page.evaluate(()=>{const u=state.units.find(u=>u.category==='naval'),p=point(u.cell);view.x=p.x;view.y=p.y;view.zoom=2;clampView();requestRender();});
      await page.waitForTimeout(150);await page.screenshot({path:folder+'/naval-'+width+'.png'});
      check(await page.locator('#map').evaluate(c=>{const d=c.getContext('2d').getImageData(0,0,c.width,c.height).data,s=new Set();for(let i=0;i<d.length;i+=4*397)s.add(d[i]+','+d[i+1]+','+d[i+2]);return s.size>20;}),'nonblank naval map '+width);
    }
    check(!report.errors.length&&!report.missing.length,'no script errors or missing assets');
  }catch(e){report.failure=e.message;throw e;}
  finally{await fs.writeFile(folder+'/verification.json',JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
