'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5403',folder=path.resolve(process.argv[3]||'build/map-movement-20261022/conquest');
(async()=>{
  await fs.mkdir(folder,{recursive:true});const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}}),report={checks:0,errors:[],missing:[],scenarios:[],screenshots:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});await page.locator('[data-mode="2"]').click();
    for(let number=1;number<=6;number++){
      await page.evaluate(number=>chooseScenario(catalog.scenarios.find(s=>s.id==='conquest'+number)),number);
      await page.waitForFunction(()=>[...document.querySelectorAll('#country-list img')].every(i=>i.complete&&i.naturalWidth>0));
      await page.waitForFunction(()=>document.querySelector('#country-list').getAnimations({subtree:true}).every(a=>a.playState==='finished'));
      const sample=await page.evaluate(()=>{
        const surface=$('strategic-map'),draw=surface.getContext('2d'),rows=[...$('country-list').children],images=rows.map(r=>r.querySelector('.country-flag'));
        const sources=images.map(i=>i.src),capture=time=>{drawStrategicMap(time);return draw.getImageData(0,0,surface.width,surface.height).data;};
        const a=capture(0),b=capture(540);let changed=0,opaque=0;
        for(let i=0;i<a.length;i+=4){if(a[i+3])opaque++;if(a[i]!==b[i]||a[i+1]!==b[i+1]||a[i+2]!==b[i+2])changed++;}
        return {scenario:chosenScenario,countries:countryChoices.length,rows:rows.length,opaque,changed,
          stable:rows.every((r,i)=>r===$('country-list').children[i]&&images[i].src===sources[i]),
          staticFlags:images.every(i=>/1\.png(?:\?|$)/.test(i.src)),enabled:!$('confirm-country').disabled};
      });
      check(sample.countries>0&&sample.rows===sample.countries&&sample.enabled,'country selection available '+number);
      check(sample.opaque>10000&&sample.changed>20,'nonblank animated strategic map '+number);
      check(sample.stable&&sample.staticFlags,'list flags remain stable during map animation '+number);report.scenarios.push(sample);
    }
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});
      await page.waitForFunction(()=>{const r=document.querySelector('#menu-stage').getBoundingClientRect();return r.width<=innerWidth+1&&r.height<=innerHeight+1;});
      await page.evaluate(()=>drawStrategicMap(540));
      check(await page.locator('#country-list').evaluate(e=>{
        const r=e.getBoundingClientRect(),images=[...e.querySelectorAll('img')].map(i=>i.getBoundingClientRect()).filter(i=>i.bottom>r.top&&i.top<r.bottom);
        return r.width>0&&r.height>0&&r.x>=0&&r.x<innerWidth&&r.bottom<=innerHeight+1&&images.every(i=>i.x>=0&&i.right<=innerWidth+1);
      }),'visible country labels and flags fit '+width);
      const file='conquest-'+width+'.png';await page.screenshot({path:path.join(folder,file)});report.screenshots.push(file);
    }
    check(!report.errors.length&&!report.missing.length,'no script errors or missing images');
  }catch(e){report.failure=e.message;await page.screenshot({path:path.join(folder,'failure.png')});throw e;}
  finally{await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
