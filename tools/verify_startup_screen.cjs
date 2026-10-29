'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2]||'http://127.0.0.1:56147',out=path.resolve(process.argv[3]||'build/startup-screen-check-20261029/browser');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  let checks=0,active='';const errors=[],check=(value,label)=>{assert.ok(value,label);checks++;};
  const ready=page=>page.waitForFunction(()=>typeof startupReady!=='undefined'&&startupReady,null,{timeout:90000});
  try{
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      active='normal '+width;
      const context=await browser.newContext({viewport:{width,height}}),page=await context.newPage();
      page.on('pageerror',e=>errors.push(e.message));
      await page.addInitScript(()=>{
        window.startupReports=[];
        window.Ew4ModImport={startup:(state,message)=>startupReports.push({state,message})};
      });
      let release;
      const barrier=new Promise(resolve=>{release=resolve;});
      await page.route('**/manifest.json',async route=>{await barrier;await route.continue();});
      await page.goto(base,{waitUntil:'domcontentloaded'});
      await page.waitForFunction(()=>document.querySelector('#startup-splash img').naturalWidth>0);
      check(await page.locator('#startup-splash').isVisible(),'original artwork while initializing '+width);
      check(await page.locator('#startup-status').isHidden(),'diagnostics hidden while initializing '+width);
      const artwork=await page.locator('#startup-splash').screenshot({path:path.join(out,'loading-'+width+'.png')});
      const png=PNG.sync.read(artwork);let ink=0;
      for(let i=0;i<png.data.length;i+=4)if(png.data[i]+png.data[i+1]+png.data[i+2]>90)ink++;
      check(ink>1000,'startup artwork has rendered pixels '+width);
      check(await page.locator('#startup-splash img').evaluate(img=>getComputedStyle(img).objectFit==='contain'&&img.naturalWidth/img.naturalHeight===1136/640),'artwork preserves original aspect ratio '+width);
      release();await ready(page);
      check(await page.locator('#startup-splash').isHidden()&&await page.locator('#startup-status').isHidden(),'successful startup clears all overlays '+width);
      check(await page.evaluate(()=>startupReports.some(r=>r.state==='ready')&&!startupReports.some(r=>r.state==='error')),'native bridge reports normal readiness '+width);
      await page.evaluate(()=>window.dispatchEvent(new ErrorEvent('error',{message:'post-startup fixture'})));
      check(await page.locator('#startup-status').isHidden(),'runtime errors do not reopen initialization panel '+width);
      await page.evaluate(async()=>{
        window.loadingRelease=null;
        window.loadingTask=withLoading(()=>new Promise(resolve=>{window.loadingRelease=resolve;}));
      });
      await page.waitForFunction(()=>loadingRelease!==null);
      check(await page.locator('.loading-watermark').isVisible(),'battle loading carries noncommercial watermark '+width);
      await page.evaluate(async()=>{loadingRelease();await loadingTask;});
      await context.close();
    }
    for(const scenario of ['resource','script','timeout','unsupported']){
      active=scenario;
      const context=await browser.newContext({viewport:{width:844,height:390},...(scenario==='unsupported'?{userAgent:'Mozilla/5.0 Android 10 Chrome/69.0.0.0 Mobile'}:{})}),page=await context.newPage();
      const pageErrors=[];page.on('pageerror',e=>pageErrors.push(e.message));
      await page.addInitScript(()=>{window.startupReports=[];window.Ew4ModImport={startup:(state,message)=>startupReports.push({state,message})};});
      if(scenario==='resource')await page.route('**/manifest.json',route=>route.fulfill({status:404,body:'fixture'}));
      if(scenario==='script')await page.route(/\/(?:compat\/)?app\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',body:'const broken = ;'}));
      if(scenario==='timeout'){
        await page.addInitScript(()=>{const native=setTimeout;window.setTimeout=function(callback,delay,...args){return native(callback,delay===45000?1500:delay,...args);};});
        await page.route('**/manifest.json',async route=>{await new Promise(resolve=>setTimeout(resolve,2500));await route.continue();});
      }
      await page.goto(base,{waitUntil:'domcontentloaded'});
      await page.waitForFunction(()=>document.body.classList.contains('startup-failed'),null,{timeout:10000}).catch(async error=>{
        const detail=await page.evaluate(()=>({message:document.querySelector('#startup-message').textContent,failed:Ew4Startup.failed,stage:typeof startupError==='undefined'?'unparsed':startupError,reports:startupReports}));
        throw new Error(error.message+' '+JSON.stringify(detail)+' '+pageErrors.join(';'));
      });
      check(await page.locator('#startup-splash').isHidden(),'failure removes artwork: '+scenario);
      const message=await page.locator('#startup-message').innerText();
      check(/错误类型：/.test(message)&&/Chromium/.test(message)&&/显示：/.test(message)&&/图形架构：/.test(message),'failure has error, platform, display and graphics details: '+scenario);
      check(await page.evaluate(()=>startupReports.some(r=>r.state==='error')),'failure reaches native diagnostics: '+scenario);
      check(await page.locator('#startup-status').evaluate(el=>{const r=el.getBoundingClientRect();return r.x===0&&r.y===0&&r.width===innerWidth&&r.height===innerHeight;}),'full failure panel fits screen: '+scenario);
      await page.screenshot({path:path.join(out,scenario+'-failure.png')});
      if(scenario==='timeout'){
        await page.waitForTimeout(3000);
        check(await page.locator('#startup-status').isVisible(),'late resources do not erase timeout failure');
        check(await page.evaluate(()=>!startupReady&&!startupReports.some(r=>r.state==='ready')),'late completion cannot enable partial menu');
      }
      if(scenario==='resource'){
        await page.unroute('**/manifest.json');
        await page.locator('#startup-retry').click();await ready(page);
        check(await page.locator('#startup-status').isHidden()&&await page.locator('#startup-splash').isHidden(),'retry recovers startup after resource failure');
      }
      await context.close();
    }
    check(errors.length===0,'normal startup has no uncaught errors');
    console.log(JSON.stringify({checks,errors}));
    await fs.writeFile(path.join(out,'results.json'),JSON.stringify({checks,errors},null,2));
  }catch(error){throw new Error(active+': '+error.message+' (checks '+checks+')');}
  finally{await browser.close();}
})().catch(error=>{console.error(error.message);process.exitCode=1;});
