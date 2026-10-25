'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const dependencies=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=dependencies('playwright'),{PNG}=dependencies('pngjs');
const base=process.argv[2]||'http://127.0.0.1:5551',folder=path.resolve(process.argv[3]||'build/tutorial-pointer-fix-20261025/map-arrows');
const report={checks:0,errors:[],missing:[],samples:[]};
function check(value,label){assert.ok(value,label);report.checks++;}
async function renderSample(page,hidden=false){
  return page.evaluate(hidden=>{
    suspendRendering();appVisible=true;const original=battlefield.tutorialArrow,calls=[],sprite=battlefield.api.sprite;
    battlefield.api.sprite=(...args)=>{if(args[1].startsWith('arrow_'))calls.push(args.slice(0,5));return sprite(...args);};
    if(hidden)battlefield.tutorialArrow=()=>{};
    try{lastRenderTime=-Infinity;renderDirty=true;render(1000);appVisible=false;suspendRendering();}
    finally{battlefield.tutorialArrow=original;battlefield.api.sprite=sprite;}
    const p=point(Number(tutorial.wait.id)),bounds=canvas.getBoundingClientRect();
    return {wait:tutorial.wait,cell:battlefield.tutorialCell,calls,
      x:bounds.left+(p.x-view.x)*view.zoom+viewport.w/2,y:bounds.top+(p.y-view.y)*view.zoom+viewport.h/2,zoom:view.zoom};
  },hidden);
}
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  require('../rewrite/Ew4.Web/wwwroot/battlefield.js');
  const native=JSON.parse(await fs.readFile(path.join(folder,'../native.json'),'utf8'));
  for(const vector of native.mapArrows.motion.vectors){
    const result=globalThis.Ew4Battlefield.advanceTutorialArrow(vector,vector.seconds);
    check(Math.abs(result.offset-vector.expected[0])<1e-5&&result.speed===vector.expected[1],'original ARM64 arrow motion vector');
  }
  const arrow=globalThis.Ew4Battlefield.Renderer.prototype.tutorialArrow;
  for(const [owner,unitOwner,color] of [[null,null,'blue'],['france',null,'blue'],['britain',null,'red'],['france','britain','blue'],['britain','france','red']]){
    const calls=[],draw={};arrow.call({tutorialCell:0,state:{width:1,playerFaction:'france',cells:[{active:true,owner}]},stationed:new Map(unitOwner?[[0,{owner:unitOwner}]]:[]),visible:()=>true,draw,api:{sprite:(...args)=>calls.push(args)}});
    check(calls.length===2&&calls[0][1]==='arrow_shadow.png'&&calls[0][4]===.7&&calls[1][1]==='arrow_'+color+'.png','native ownership color and shadow scale');
  }
  for(const cell of [null,undefined,-1,1,NaN]){
    arrow.call({tutorialCell:cell,state:{cells:[{active:true}]},api:{sprite:()=>assert.fail('invalid target drew arrow')}});
    report.checks++;
  }
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1425,height:1177},hasTouch:true});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    for(const [width,height] of [[1425,1177],[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});
      for(const scenario of ['tutorials1','tutorials2']){
        await page.evaluate(id=>{appVisible=true;return start(id,null);},scenario);
        await page.touchscreen.tap(width/2,height/3);
        if(scenario==='tutorials1')await page.touchscreen.tap(width/2,height/3);
        const expected=scenario==='tutorials1'?2783:2633;
        await page.waitForFunction(cell=>tutorial.wait?.name==='wait area'&&Number(tutorial.wait.id)===cell,expected);
        await page.waitForTimeout(400);
        const sample=await renderSample(page);
        check(sample.cell===expected,'real touch flow reaches map target');
        check(sample.calls.length===2&&sample.calls[0][1]==='arrow_shadow.png'&&sample.calls[1][1]==='arrow_blue.png','source map arrow and shadow submitted');
        check(sample.x>20&&sample.x<width-20&&sample.y>65*sample.zoom&&sample.y<height,'full arrow bounce inside viewport');
        const file=scenario+'-'+width+'.png';await page.screenshot({path:path.join(folder,file)});
        const before=PNG.sync.read(await page.screenshot());
        await renderSample(page,true);const after=PNG.sync.read(await page.screenshot());
        let pixels=0;
        for(let y=Math.max(0,Math.floor(sample.y-65*sample.zoom));y<Math.min(height,Math.ceil(sample.y+5*sample.zoom));y++)
          for(let x=Math.max(0,Math.floor(sample.x-15*sample.zoom));x<Math.min(width,Math.ceil(sample.x+25*sample.zoom));x++){
            const i=(y*before.width+x)*4;
            if(before.data[i]!==after.data[i]||before.data[i+1]!==after.data[i+1]||before.data[i+2]!==after.data[i+2])pixels++;
          }
        check(pixels>100,'map arrow pixels actually rendered');
        await renderSample(page);
        await page.evaluate(()=>{
          window.tutorialArrowFrames=[];window.tutorialArrowSprite=battlefield.api.sprite;
          battlefield.api.sprite=(...args)=>{if(args[1].startsWith('arrow_'))tutorialArrowFrames.push({time:battlefield.time,args:args.slice(0,5)});return tutorialArrowSprite(...args);};
          appVisible=true;requestRender();
        });
        await page.waitForTimeout(350);
        const animation=await page.evaluate(()=>{
          appVisible=false;suspendRendering();battlefield.api.sprite=tutorialArrowSprite;
          return tutorialArrowFrames;
        });
        const arrows=animation.filter(f=>f.args[1]!=='arrow_shadow.png'),shadows=animation.filter(f=>f.args[1]==='arrow_shadow.png');
        // Use the cell anchor rather than the current animation phase.
        const anchor=await page.evaluate(()=>point(Number(tutorial.wait.id)));
        check(arrows.length>2&&new Set(arrows.map(f=>f.args[3])).size>1,'map arrow moves across actual scheduled frames');
        check(shadows.length===arrows.length&&arrows.every((f,i)=>{
          const offset=f.args[3]-anchor.y,s=shadows[i].args;
          return offset>=-20&&offset<=0&&f.args[2]===anchor.x&&Math.abs(s[2]-anchor.x+offset*.5)<1e-5&&Math.abs(s[3]-anchor.y-offset*.5)<1e-5;
        }),'native bounce range and linked shadow displacement');
        check(arrows.some((f,i)=>i>0&&Math.abs(f.args[3]-arrows[i-1].args[3])>0),'continuous redraw without input');
        const saved=await page.evaluate(()=>tutorial.checkpoint());
        await page.touchscreen.tap(sample.x+64*sample.zoom,sample.y);
        check(await page.evaluate(cell=>Number(tutorial.wait.id)===cell,expected),'wrong target keeps arrow and tutorial wait');
        await page.evaluate(saved=>beginTutorial(saved),saved);
        check((await renderSample(page)).cell===expected,'restored wait-area arrow');
        await page.touchscreen.tap(sample.x,sample.y);
        await page.waitForFunction(()=>tutorial.wait?.name!=='wait area');
        const next=await renderSample(page);
        check(next.cell===null&&!next.calls.length,'correct map tap advances and removes arrow');
        report.samples.push({scenario,width,height,...sample,pixels,file,animation});
      }
    }
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing assets');
  }finally{await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors,missing:report.missing,samples:report.samples.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
