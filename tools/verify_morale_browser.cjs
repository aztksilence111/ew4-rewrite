'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5401',folder=path.resolve(process.argv[3]||'build/morale-restoration-20261022/browser');
const report={checks:0,errors:[],missing:[],screenshots:[],scope:'Native state marker restored; transition fixtures use projected effective morale, not a gameplay rule change.'};
const check=(v,label)=>{assert.ok(v,label);report.checks++;};
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});await page.evaluate(()=>start('campaign1_02b',null));
    report.transitions=await page.evaluate(async()=>{
      const original=state,unit=state.units.find(u=>u.owner===state.playerFaction),calls=[],sprite=battlefield.api.sprite;
      const run=async(morale,center=false,notices=[])=>{
        await installState({...state,revision:state.revision+1,notices,combat:null,movements:[],recruited:[],units:state.units.map(u=>u.id===unit.id?{...u,morale}:u)},center);
        view.x=point(unit.cell).x;view.y=point(unit.cell).y;clampView();calls.length=0;renderDirty=true;
        battlefield.api.sprite=(atlas,name,x,y,...rest)=>{if(name.startsWith('morale_'))calls.push({name,x,y});return sprite(atlas,name,x,y,...rest);};
        try{render(performance.now());}finally{battlefield.api.sprite=sprite;}
        const p=point(unit.cell);return calls.filter(c=>c.x===p.x+15&&c.y===p.y-25).map(c=>c.name);
      };
      try{return {low:await run(-1),surrounded:await run(-2),confused:await run(-3),high:await run(1,false,[{kind:'battle.morale_set',subject:unit.id,value:1},{kind:'battle.morale_added',subject:unit.id,value:1}]),same:await run(1),normal:await run(0),reload:await run(-2,true),skipped:await(async()=>{skipAi=true;try{return await run(-1);}finally{skipAi=false;}})()};}
      finally{await installState(original,true);}
    });
    for(const [key,name] of Object.entries({low:'down1',surrounded:'down2',confused:'down3',high:'up',same:'up',reload:'down2',skipped:'down1'}))check(report.transitions[key].join(',')==='morale_'+name+'.png',key+' effective state appears once');
    check(report.transitions.normal.length===0,'normal morale clears marker');
    report.layer=await page.evaluate(()=>{
      const u=state.units[0],r=new Ew4Battlefield.Renderer({sprite:(a,n)=>calls.push(n)}),calls=[];
      r.state=state;r.factions=new Map(state.factions.map(f=>[f.id,f]));r.time=0;r.draw=ctx;
      r.visibleUnitMarkers=[{unit:{...u,morale:-1},p:{x:100,y:100}}];r.unitMarkers();
      return calls;
    });
    check(report.layer[0]==='morale_down1.png','status drawn before flags and commander portraits');
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});
      await page.evaluate(()=>Promise.all(['buildings','buildings_hd'].map(n=>image('images/'+n+'.png').decode())));
      report['pixels'+width]=await page.evaluate(()=>{
        const unit=state.units.find(u=>u.owner===state.playerFaction),p=point(unit.cell),now=performance.now();state={...state,units:state.units.map(u=>u.id===unit.id?{...u,morale:1}:u)};view.x=p.x;view.y=p.y;view.zoom=1;clampView();renderDirty=true;render(now);
        const before=ctx.getImageData(0,0,canvas.width,canvas.height),sprite=battlefield.api.sprite;
        battlefield.api.sprite=(atlas,name,...args)=>{if(!name.startsWith('morale_'))sprite(atlas,name,...args);};
        try{renderDirty=true;render(now);}finally{battlefield.api.sprite=sprite;}
        const after=ctx.getImageData(0,0,canvas.width,canvas.height);let changed=0;for(let i=0;i<before.data.length;i+=4)if(before.data[i]!==after.data[i]||before.data[i+1]!==after.data[i+1]||before.data[i+2]!==after.data[i+2])changed++;
        renderDirty=true;render(now);return changed;
      });
      check(report['pixels'+width]>50,'visible morale pixels '+width);
      const name='morale-'+width+'.png';await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);
    }
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing resources');
  }finally{await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e);process.exitCode=1;});
