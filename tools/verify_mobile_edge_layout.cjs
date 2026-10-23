'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5490',folder=path.resolve(process.argv[3]||'build/mobile-edge-layout-20261023/edges');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],cases:[],scope:'Browser safe-area emulation and rendered pixels; no physical Android execution'};
  const check=(pass,label)=>{assert.ok(pass,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1215,height:582},hasTouch:true});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  async function capture(name,points){
    const bytes=await page.screenshot({path:path.join(folder,name+'.png')});
    return page.evaluate(async({base64,points})=>{
      const img=new Image();img.src='data:image/png;base64,'+base64;await img.decode();
      const c=document.createElement('canvas');c.width=img.width;c.height=img.height;
      const ctx=c.getContext('2d');ctx.drawImage(img,0,0);
      return points.map(([x,y])=>Array.from(ctx.getImageData(x,y,1,1).data));
    },{base64:bytes.toString('base64'),points});
  }
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    await page.evaluate(()=>start('campaign1_01'));await page.waitForFunction(()=>!busy);
    await page.locator('#close-detail').click();await page.waitForFunction(()=>!$('dialogue').hidden);
    for(const [name,width,height,insets] of [
      ['device',1215,582,{left:0,right:0,top:0,bottom:0}],
      ['left-cutout',1215,582,{left:64,right:0,top:0,bottom:0}],
      ['right-cutout',844,390,{left:0,right:48,top:0,bottom:16}],
      ['user-image',1823,873,{left:64,right:0,top:0,bottom:0}],
      ['portrait',390,844,{left:0,right:0,top:32,bottom:24}],
      ['tablet',1024,768,{left:32,right:24,top:12,bottom:24}]
    ]){
      await page.setViewportSize({width,height});
      await page.evaluate(insets=>{
        for(const [side,value] of Object.entries(insets))document.documentElement.style.setProperty('--screen-safe-'+side,value+'px');
        settings.screen=100;sizeUi();requestRender();
      },insets);await page.waitForTimeout(250);
      const points=[[2,Math.floor(height/3)],[width-3,Math.floor(height/3)],[Math.floor(width/2),2],[Math.floor(width/2),height-3]];
      const before=await capture(name+'-battle',points);
      await page.locator('#menu-button').click();await page.waitForTimeout(250);
      const paused=await capture(name+'-pause',points);
      for(let i=0;i<points.length;i++){
        check(before[i].slice(0,3).some(v=>v>20),name+' edge '+i+' has battlefield pixels');
        check(paused[i].slice(0,3).every((v,j)=>Math.abs(v-before[i][j]*.6)<3),name+' edge '+i+' is uniformly dimmed');
      }
      const geometry=await page.evaluate(()=>{
        const rect=id=>{const r=$(id).getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom};};
        const d=document.querySelector('.pause-dialog').getBoundingClientRect();
        return {modal:rect('pause'),dialog:{x:d.x,y:d.y,right:d.right,bottom:d.bottom},
          end:rect('end-turn'),undo:rect('undo-move'),safe:screenLayout,canvas:[canvas.clientWidth,canvas.clientHeight],
          resourcesLeft:document.querySelector('.battle-resources').getBoundingClientRect().left,
          terrainLeft:$('terrain-info').getBoundingClientRect().left,
          edgeTargets:[[2,innerHeight/3],[innerWidth-3,innerHeight/3],[innerWidth/2,2],[innerWidth/2,innerHeight-3]].map(([x,y])=>document.elementFromPoint(x,y)?.closest('.modal')?.id)};
      });
      check(geometry.modal.x===0&&geometry.modal.y===0&&geometry.modal.width===width&&geometry.modal.height===height,name+' pause covers the entire viewport');
      check(geometry.edgeTargets.every(id=>id==='pause'),name+' pause blocks input at every edge');
      const d=geometry.dialog;
      check(d.x>=insets.left&&d.y>=insets.top&&d.right<=width-insets.right+.1&&d.bottom<=height-insets.bottom+.1,name+' pause stays inside safe content');
      check(Math.abs((d.x+d.right)/2-(insets.left+width-insets.right)/2)<1,name+' pause is centered in safe content');
      check(Math.abs(width-geometry.end.right-2)<.1&&Math.abs(height-geometry.end.bottom-2)<.1&&Math.abs(geometry.undo.x-2)<.1,name+' buttons hug actual viewport edges with two-pixel clearance');
      check(geometry.resourcesLeft===0,name+' resources attach to the left display edge');
      check(geometry.safe.width===width&&geometry.safe.height===height&&geometry.safe.safeWidth===width-insets.left-insets.right,name+' full viewport and floating-content bounds are separate');
      check(geometry.canvas[0]===width&&geometry.canvas[1]===height,name+' map fills the full WebView');
      await page.locator('#pause-settings').click();await page.waitForTimeout(250);
      const settings=await capture(name+'-settings',points);
      // Settings opens above the still-visible pause, so both masks darken every edge.
      for(let i=0;i<points.length;i++)check(settings[i].slice(0,3).every((v,j)=>Math.abs(v-before[i][j]*.36)<3),name+' settings masks edge '+i);
      check(await page.locator('#confirm-settings').evaluate(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;}),name+' settings confirmation is reachable');
      await page.locator('#close-settings').click();await page.locator('#resume').click();
      check(await page.locator('#dialogue').isVisible(),name+' resuming preserves pending dialogue');
      report.cases.push({name,width,height,insets,before,paused,settings,geometry});
    }
    check(report.errors.length===0&&report.missing.length===0,'no browser exceptions or missing assets');
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,cases:report.cases.length,errors:report.errors.length,missing:report.missing.length}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
