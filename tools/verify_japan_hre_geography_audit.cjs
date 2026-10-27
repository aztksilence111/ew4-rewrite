'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {pathToFileURL}=require('node:url'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const correction=process.argv.includes('--correction'),folder=correction?'geography_correction':'geography_audit';
const root=path.resolve(__dirname,'..'),output=path.join(root,'build',correction?'japan-hre-geography-correction-20261026':'japan-hre-geography-audit-20261026');
(async()=>{
  await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio','--allow-file-access-from-files']});
  let checks=0;const errors=[];
  const check=(value,message)=>{assert.ok(value,message);checks++;};
  try{
    for(const [width,height]of [[1440,1000],[390,844]]){
      const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1});
      page.on('pageerror',e=>errors.push(e.message));page.on('requestfailed',r=>errors.push(r.url()));
      await page.goto(pathToFileURL(path.join(root,'maps/japan_hre_source_20261026',folder,'index.html')).href);
      check((await page.locator('.conclusion').textContent()).includes(correction?'真实坐标':'未通过真实地理核对'),'geographic review conclusion explicit');
      check(await page.locator('img').count()===10,'ten independent map comparisons');
      await page.locator('details').evaluateAll(nodes=>nodes.forEach(n=>n.open=true));
      await page.locator('img').evaluateAll(nodes=>nodes.forEach(n=>n.loading='eager'));
      await page.waitForFunction(()=>[...document.images].every(i=>i.complete&&i.naturalWidth>0));
      check(await page.locator('img').evaluateAll(nodes=>nodes.every(n=>n.naturalWidth===1464)),'all comparison images load');
      check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'no horizontal overflow');
      await page.screenshot({path:path.join(output,'audit-'+width+'.png')});
      await page.close();
    }
    check(errors.length===0,'no browser or asset failures '+JSON.stringify(errors));
    console.log(JSON.stringify({checks,errors:0,viewports:2,comparisons:10}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
