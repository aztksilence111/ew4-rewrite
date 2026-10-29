'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2],out=path.resolve(process.argv[3]||'build/shop-commander-check-20261029'),source=process.argv.includes('--source');
(async()=>{
  const fixture=JSON.parse(await fs.readFile(path.join(out,'fixture.json'),'utf8'));
  const report={checks:0,errors:[],missing:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720},deviceScaleFactor:2});
    let activeSnapshot,activeRoute;
    // Replay API-verified fixtures when a detail dialog reopens its service.
    await page.route(/\/api\/(commerce|exchange)(?:\?|$)/,async route=>{
      if(activeSnapshot&&new URL(route.request().url()).pathname==='/api/'+activeRoute)
        await route.fulfill({contentType:'application/json',body:JSON.stringify({ok:true,data:activeSnapshot})});
      else await route.continue();
    });
    page.on('pageerror',e=>report.errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
    if(source)await page.route(/\/(?:compat\/)?commander-workflows\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',path:path.join(__dirname,'../rewrite/Ew4.Web/wwwroot/commander-workflows.js')}));
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await page.evaluate(async()=>{stageIntroOpen=false;await installState(await rpc('load',{mode:1}),true);});
    for(const [name,width,height] of [['desktop',1280,720],['phone',844,390],['portrait',390,844]]){
      await page.setViewportSize({width,height});
      for(const route of ['commerce','exchange'])for(const kind of ['scenario','owned','plain','empty']){
        const snapshot=JSON.parse(await fs.readFile(path.join(out,route+'-'+kind+'.json'),'utf8'));
        activeSnapshot=snapshot;activeRoute=route;
        const cell=fixture[route==='commerce'?'shop':'exchange'],slot=route==='commerce'?'tcmder_buyer':'tcmder_player',hasCommander=['scenario','owned'].includes(kind);
        await page.evaluate(async({route,cell,snapshot})=>{
          closeDetail();menus.data=null;
          await menus[route==='commerce'?'shop':'exchange'](cell,snapshot);
        },{route,cell,snapshot});
        const portrait=page.locator('[data-native-id="'+slot+'"] .commander-card');
        check(await portrait.count()===(hasCommander?1:0),name+' '+route+' '+kind+' portrait presence');
        if(hasCommander){
          const definition=await page.evaluate(id=>menus.data.definitions.find(d=>d.id===id),snapshot.commanderView.definitionId);
          check(await portrait.getAttribute('aria-label')===definition.name,'exact commander name');
          await portrait.locator(':scope > img').first().evaluate(img=>img.decode());
          const image=await portrait.locator(':scope > img').first().getAttribute('src');
          check(image.endsWith('/'+definition.image)&&!image.includes('button_add_generals'),'actual commander image');
          check(await portrait.evaluate(e=>!e.dataset.locked),'portrait remains undimmed');
          await page.waitForTimeout(250);
          check(await portrait.evaluate(e=>{const r=e.getBoundingClientRect(),p=e.parentElement.getBoundingClientRect();return r.width>0&&r.height>0&&r.left>=0&&r.top>=0&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1&&r.width<=p.width+1&&r.height<=p.height+1;}),'portrait fits native slot and viewport');
          const pixels=PNG.sync.read(await portrait.locator(':scope > img').first().screenshot()),colors=new Set();
          for(let i=0;i<pixels.data.length;i+=28)colors.add(pixels.data.subarray(i,i+3).toString('hex'));
          check(colors.size>30,'nonblank portrait pixels');
          if(kind==='owned'){
            const stars=page.locator('[data-native-id="lbox_stars"]');
            check(await stars.getAttribute('aria-label')==='经商 8','uncapped commerce label');
            check(await stars.locator('img').count()===1,'star with explicit count above five');
            check(await stars.evaluate(e=>e.scrollWidth<=e.clientWidth&&e.scrollHeight<=e.clientHeight),'commerce count fits');
          }
          if(kind==='scenario'){
            await page.screenshot({path:path.join(out,name+'-'+route+(source?'-source':'')+'.png')});
            await portrait.locator('.commander-info').click();
            await page.locator('.commander-detail').waitFor();
            check(await page.locator('.commander-detail-card .commander-card').getAttribute('aria-label')===definition.name,'portrait opens exact battle details');
            await page.locator('#close-detail').click();
            await portrait.waitFor();check(await portrait.count()===1,'details return to same service with portrait');
          }
        }
      }
    }
    activeSnapshot=null;
    await page.evaluate(()=>menus.shop(-1));
    check(await page.locator('[data-native-id="tcmder_buyer"] .commander-card').count()===0,'headquarters has no city portrait');
    check(await page.locator('.shop-refresh').count()===1,'headquarters refresh retained');
    check(!report.errors.length,'no script errors');check(!report.missing.length,'no missing resources');
  }finally{await fs.writeFile(path.join(out,'browser'+(source?'-source':'')+'.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
