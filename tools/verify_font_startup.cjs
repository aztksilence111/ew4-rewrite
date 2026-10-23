'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.env.EW4_TEST_URL||'http://127.0.0.1:5196',folder=path.resolve('build/apk-font-fix-20261021/browser');
(async()=>{
 await fs.mkdir(folder,{recursive:true});
 const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
 const report={checks:[],scenarios:[],errors:[]};
 function check(value,label){assert.ok(value,label);report.checks.push(label);}
 async function scenario(name,routeTest,action){
  const context=await browser.newContext({viewport:{width:1136,height:640}}),page=await context.newPage(),errors=[],requests=[];
  page.on('pageerror',error=>errors.push(error.message));page.on('request',request=>{if(request.resourceType()==='font')requests.push(request.url());});
  if(routeTest)await page.route('**/*',route=>routeTest(route));
  try{await page.goto(base);await action(page,requests);check(errors.length===0,name+' has no uncaught errors');report.scenarios.push({name,fontRequests:requests.length,errors});}
  finally{await context.close();}
 }
 const ready=page=>page.waitForFunction(()=>typeof startupReady!=='undefined'&&startupReady);
 const hq=async page=>{
  await page.locator('#headquarters').click();await page.waitForFunction(()=>!document.querySelector('#hq-screen').hidden&&document.querySelectorAll('.commander-card').length>0);
  await page.locator('#hq-recruit').click();
  await page.waitForFunction(()=>{const portraits=[...document.querySelectorAll('.commander-card:not(.empty-commander)>img:first-child')];return portraits.length>0&&portraits.every(img=>img.complete&&img.naturalWidth>0);});
  check(true,'headquarters recruitment displays real commander portraits');
 };
 try{
  await scenario('normal',null,async(page,requests)=>{
   await ready(page);check(requests.length<7,'startup does not load every complete font');
   check(await page.locator('#startup-status').isHidden(),'normal startup status clears');
   const before=requests.length;
   await page.evaluate(async()=>{const node=originalTextRenderer.element('𠮷𰀀𲎰','font_text_4_cn_hd');node.id='rare-probe';document.body.append(node);await Ew4OriginalUI.prepareText('𠮷𰀀𲎰');});
   check(requests.length>before,'rare characters load subsets on demand');
   check(await page.evaluate(()=>Ew4OriginalUI.availableText('𠮷𰀀𲎰')==='𠮷𰀀𲎰'),'rare characters become available after deferred load');
   await page.locator('#rare-probe').evaluate(node=>node.remove());await hq(page);await page.screenshot({path:folder+'/normal-hq.png'});
  });
  await scenario('slow combat font preserves menu labels',async route=>{
   if(route.request().url().endsWith('/LiberationSans-BoldItalic-core.woff2'))await new Promise(resolve=>setTimeout(resolve,300));
   await route.continue();
  },async page=>{
   await ready(page);
   const labels=await page.locator('.main-entry').evaluateAll(nodes=>nodes.map(n=>{const c=n.querySelector('canvas'),p=c?.getContext('2d').getImageData(0,0,c.width,c.height).data;return {label:n.getAttribute('aria-label'),ink:p?[...p].filter((v,i)=>i%4===3&&v>128).length:0};}));
   check(labels.length===5&&labels.every(n=>n.label&&n.ink>100),'delayed font cannot erase original menu text during startup');
  });
  let primaryFailed=true;
  await scenario('primary failure and recovery',route=>primaryFailed&&route.request().url().endsWith('/ARPLUKaiCN-core.woff2')?route.fulfill({status:404,body:'injected primary font failure'}):route.continue(),async page=>{
   await ready(page);check((await page.locator('#startup-message').innerText()).includes('ARPLUKaiCN-core.woff2'),'failure names exact font file');
   check(await page.evaluate(()=>document.fonts.check('32px "EW4 Kai"','中文城市')),'primary failure uses bundled WenKai fallback');
   await hq(page);await page.screenshot({path:folder+'/fallback-hq.png'});
   check(await page.evaluate(()=>document.querySelector('#menu-stage').getBoundingClientRect().bottom<document.querySelector('#startup-status').getBoundingClientRect().top),'font failure status does not cover headquarters controls');
   primaryFailed=false;await page.locator('#startup-retry').click();await page.waitForFunction(()=>!Ew4OriginalUI.fontStatus.failures.length&&!startupRunning);
   check(await page.evaluate(()=>document.fonts.check('32px "EW4 UI Kai"','中文城市')),'retry loads restored primary font');
  });
  await scenario('primary decoder rejects font',route=>route.request().url().endsWith('/ARPLUKaiCN-core.woff2')?route.fulfill({contentType:'font/woff2',body:'invalid WOFF2 bytes'}):route.continue(),async page=>{
   await ready(page);check((await page.locator('#startup-message').innerText()).includes('ARPLUKaiCN-core.woff2'),'font decoder failure identifies file');
   await hq(page);check(true,'font decoding failure still reaches populated headquarters');
  });
  let allFailed=true;
  await scenario('all common fonts fail then retry',route=>allFailed&&route.request().url().includes('/fonts/parts/')?route.fulfill({status:404,body:'injected all-font failure'}):route.continue(),async page=>{
   await page.waitForFunction(()=>typeof startupError!=='undefined'&&startupError&&!startupRunning);
   check(await page.locator('#headquarters').isDisabled(),'headquarters stays disabled after failed startup');
   check(await page.evaluate(()=>typeof manifest==='undefined'),'failed startup does not publish partial state');
   await page.locator('#headquarters').dispatchEvent('click');
   check(await page.locator('#hq-screen').isHidden(),'failed startup cannot open blank headquarters');
   await page.screenshot({path:folder+'/all-fonts-failed.png'});
   for(const viewport of [{width:844,height:390},{width:390,height:844}]){await page.setViewportSize(viewport);check(await page.locator('#startup-status').evaluate(node=>{const r=node.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight;}),'error status fits '+viewport.width);await page.screenshot({path:folder+'/failed-'+viewport.width+'.png'});}
   allFailed=false;await page.locator('#startup-retry').click();await ready(page);await hq(page);check(true,'all-font failure can recover without reload');
  });
  let indexFailed=true;
  await scenario('coverage index fails then retry',route=>indexFailed&&route.request().url().includes('/fonts/coverage.json')?route.fulfill({status:500,body:'injected coverage failure'}):route.continue(),async page=>{
   await page.waitForFunction(()=>typeof startupError!=='undefined'&&startupError&&!startupRunning);
   check((await page.locator('#startup-message').innerText()).includes('coverage.json'),'index failure identifies coverage metadata');
   indexFailed=false;await page.locator('#startup-retry').click();await ready(page);check(true,'failed font preparation promise can be retried');
  });
  let releaseCatalog;const catalogGate=new Promise(resolve=>{releaseCatalog=resolve;});
  await scenario('pending startup',async route=>{if(new URL(route.request().url()).pathname==='/api/catalog')await catalogGate;await route.continue();},async page=>{
   check(await page.locator('#headquarters').isDisabled(),'headquarters disabled while catalog is pending');
   await page.locator('#headquarters').dispatchEvent('click');check(await page.locator('#hq-screen').isHidden(),'pending startup ignores direct entry event');
   releaseCatalog();await ready(page);await hq(page);
  });
 }catch(error){report.errors.push(error.message);throw error;}
 finally{await fs.writeFile(folder+'/results.json',JSON.stringify(report,null,2)+'\n');await browser.close();}
 console.log(JSON.stringify({checks:report.checks.length,scenarios:report.scenarios.length,errors:report.errors}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
