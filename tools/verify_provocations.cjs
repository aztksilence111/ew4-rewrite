'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root=path.resolve(__dirname,'..'),folder=path.join(root,'build/provocations-20261025');
const url=process.argv[2]||'http://127.0.0.1:5534',runtime=process.argv[3]||path.join(folder,'runtime');
(async()=>{
  const fixture=JSON.parse(await fs.readFile(path.join(folder,'client-fixtures.json'),'utf8'));
  for(let slot=2;slot<=5;slot++)await fs.copyFile(path.join(fixture.folder,`slot-${slot}.json`),path.join(runtime,`slot-${slot}.json`));
  const report={checks:0,errors:[],missing:[],screenshots:[]},check=(pass,label)=>{assert(pass,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1136,height:640}});
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(url);
    await page.waitForFunction(()=>catalog&&startupReady&&document.documentElement.dataset.fontsReady==='true',null,{timeout:90000});
    for(const [width,height]of[[1136,640],[844,390],[390,844]]){
      await page.setViewportSize({width,height});
      for(let slot=2;slot<=5;slot++){
        await page.evaluate(slot=>load(slot,0),slot);
        await page.waitForFunction(()=>state?.dialogue&&!busy&&!aiRunning);
        check(await page.locator('#dialogue-ok').isVisible(),'dialogue visible '+slot);
        const talk=await page.evaluate(()=>({text:state.dialogue,presentation:state.dialoguePresentation,
          portrait:document.querySelector('#dialogue-portrait').naturalWidth,
          bounds:(()=>{const b=document.querySelector('#dialogue-ok').getBoundingClientRect();return {x:b.x,y:b.y,right:b.right,bottom:b.bottom};})()}));
        check(talk.text.length>5&&!talk.text.startsWith('diplomacy.'),'localized original text '+slot);
        check(talk.portrait>0&&talk.presentation.name.length>0,'commander portrait and name '+slot);
        check(talk.bounds.x>=-1&&talk.bounds.y>=-1&&talk.bounds.right<=width+1&&talk.bounds.bottom<=height+1,'dialogue fits viewport '+slot);
        const name=`dialogue-${slot}-${width}x${height}.png`;
        await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);
        await page.locator('#dialogue-ok').click();
        await page.waitForFunction(()=>state&&!state.dialogue&&!busy);
        check(await page.evaluate(()=>!state.dialogue),'acknowledgement resumes battle '+slot);
      }
    }
    check(!report.errors.length,'no browser errors');check(!report.missing.length,'no missing assets');
  }finally{
    await fs.writeFile(path.join(folder,'browser.json'),JSON.stringify(report,null,2)+'\n');await browser.close();
  }
  console.log(JSON.stringify(report));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
