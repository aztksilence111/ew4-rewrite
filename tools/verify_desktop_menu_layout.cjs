'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const vm=require('node:vm'),{execFileSync}=require('node:child_process');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5493',folder=path.resolve(process.argv[3]||'build/desktop-menu-20261023/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],layouts:[]};
  const check=(pass,label)=>{assert.ok(pass,label);report.checks++;};
  const current={},baseline={};
  vm.runInNewContext(await fs.readFile('rewrite/Ew4.Web/wwwroot/screen-layout.js','utf8'),current);
  const baselineRef=process.env.EW4_LAYOUT_BASELINE||'HEAD';report.baseline=baselineRef;
  vm.runInNewContext(execFileSync('git',['show',baselineRef+':rewrite/Ew4.Web/wwwroot/screen-layout.js'],{encoding:'utf8'}),baseline);
  for(const [width,height] of [[390,844],[844,390],[768,1024],[1280,800],[2550,1275]])for(const screen of [80,100,150]){
    for(const touch of [false,true])for(const insets of [{},{left:32,right:24,top:12,bottom:24}]){
      const input={width,height,screen,touch,insets,reserve:28},before=baseline.Ew4ScreenLayout.measure(input);
      const layout=current.Ew4ScreenLayout.measure(input);
      check(Object.keys(before).filter(key=>!key.startsWith('menu')).every(key=>before[key]===layout[key]),'battlefield values match baseline '+[width,height,screen,touch]);
      check(layout.menuWidth===568&&layout.menuHeight===320,'all platforms use original menu canvas');
    }
  }
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const page=await browser.newPage();
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  const fits=async selector=>page.locator(selector).evaluateAll(nodes=>nodes.every(el=>{const r=el.getBoundingClientRect();return r.left>=-.1&&r.top>=-.1&&r.right<=innerWidth+.1&&r.bottom<=innerHeight+.1;}));
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    check(await page.locator('#menu').evaluate(el=>getComputedStyle(el).backgroundColor==='rgb(119, 119, 119)'),'unused menu area stays gray');
    for(const [width,height] of [[2550,1275],[3440,1440],[1920,1080],[1366,768],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.evaluate(()=>showMain());
      for(const screen of [80,100,150]){
        await page.evaluate(screen=>{settings.screen=screen;sizeUi();},screen);
        check(await fits('#main-menu .game-title, #main-menu .main-entry'),'main content fits '+[width,height,screen]);
        check(await page.locator('#main-menu').evaluate(el=>getComputedStyle(el).backgroundSize==='100% 100%'),'main art uses complete original composition');
        const layout=await page.evaluate(()=>({width:innerWidth,height:innerHeight,screen:settings.screen,menuScale:screenLayout.menuScale,uiScale:screenLayout.uiScale}));
        const stage=await page.locator('#menu-stage').evaluate(el=>{const r=el.getBoundingClientRect();return {width:r.width,height:r.height,x:r.x,y:r.y};});
        check(Math.abs(stage.width/stage.height-568/320)<.001,'complete menu retains original aspect ratio');
        check(stage.x>=-.1&&stage.y>=-.1&&Math.abs(stage.x*2+stage.width-width)<.1&&Math.abs(stage.y*2+stage.height-height)<.1,'menu fits and is centered');
        check(Math.abs(stage.width-568*layout.menuScale)<.1&&layout.menuScale<=Math.min(width/568,height/320)+.001,'menu scale matches available space');
        if(width/height>568/320)check(stage.x>0,'wide windows retain side bars');
        if(width/height<568/320)check(stage.y>0,'tall windows retain top and bottom bars');
        if(width===2550&&screen===100)await page.screenshot({path:path.join(folder,'main-desktop.png')});
        await page.locator('[data-mode="2"]').click();await page.waitForFunction(()=>!conquestEntering);
        check(await fits('#scenarios .scenario-entry, #conquest-pagination'),'conquest content fits '+[width,height,screen]);
        const grid=await page.locator('#scenarios').evaluate(el=>{
          const r=el.getBoundingClientRect(),cards=[...el.children].map(c=>c.getBoundingClientRect()),pager=document.getElementById('conquest-pagination').getBoundingClientRect();
          return {center:r.left+r.width/2,gap:cards[1].left-cards[0].right,cardWidth:cards[0].width,pagerGap:pager.top-r.bottom};
        });
        check(Math.abs(grid.gap/layout.menuScale-18)<.1,'columns retain original spacing');
        check(Math.abs(grid.center-width/2)<=3*layout.menuScale,'cards remain centered');
        check(grid.pagerGap>0,'pager clears cards');
        check(await page.locator('#scenario-menu').evaluate(el=>getComputedStyle(el).backgroundSize==='100% 100%'),'conquest keeps complete original background');
        if(width===2550&&screen===100)await page.screenshot({path:path.join(folder,'conquest-desktop.png')});
        if(screen===100)await page.screenshot({path:path.join(folder,`conquest-${width}x${height}.png`)});
        await page.locator('#conquest-next').click();await page.waitForFunction(()=>!conquestEntering);
        check(await page.locator('#scenarios .scenario-placeholder').count()===6,'reserved page stays accessible');
        await page.locator('#conquest-prev').click();await page.waitForFunction(()=>!conquestEntering);
        if(screen===100){
          await page.locator('#scenarios button').first().click();await page.locator('#countries').waitFor();
          await page.waitForFunction(()=>!busy);await page.waitForTimeout(500);
          check(await fits('#country-list, #confirm-country'),'country controls fit '+[width,height]);
          const pixels=await page.evaluate(()=>{drawStrategicMap(540);const c=$('strategic-map'),d=c.getContext('2d').getImageData(0,0,c.width,c.height).data;let n=0;for(let i=3;i<d.length;i+=4)if(d[i])n++;return n;});
          check(pixels>10000,'strategic map remains nonblank');
          await page.screenshot({path:path.join(folder,`countries-${width}x${height}.png`)});
        }
        report.layouts.push({...layout,...grid});await page.evaluate(()=>showMain());
      }
    }
    await page.setViewportSize({width:1920,height:1080});
    await page.locator('[data-mode="1"]').click();await page.locator('.chapter-entry').first().click();
    await page.locator('.chapter-preview-confirm').click();await page.locator('#campaign-screen').waitFor();
    await page.waitForTimeout(500);
    check(await fits('#campaign-battles, #campaign-confirm'),'campaign controls fit');
    await page.screenshot({path:path.join(folder,'campaign-desktop.png')});
    await page.evaluate(()=>showMain());await page.locator('#headquarters').click();
    await page.waitForFunction(()=>!busy);await page.waitForTimeout(500);
    check(await fits('#hq-screen, #hq-recruit, #hq-roster'),'headquarters fits original canvas');
    await page.screenshot({path:path.join(folder,'headquarters-desktop.png')});
    await page.evaluate(()=>showMain());await page.locator('[data-mode="4"]').click();
    check(await fits('#menu-stage, .tutorial-menu'),'tutorial menu fits');
    const nativePage=await browser.newPage({hasTouch:true});
    nativePage.on('pageerror',e=>report.errors.push('native: '+e.message));
    nativePage.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
    await nativePage.addInitScript(()=>localStorage.setItem('ew4.settings',JSON.stringify({music:0,sound:0})));
    await nativePage.route('https://ew4.local/**',async route=>{
      const url=new URL(route.request().url());
      const headers={...route.request().headers()};
      if(headers.origin)headers.origin=new URL(base).origin;
      const response=await route.fetch({url:new URL(url.pathname+url.search,base).href,headers});
      await route.fulfill({response});
    });
    await nativePage.goto('https://ew4.local/index.html');
    try{await nativePage.waitForFunction(()=>startupReady,null,{timeout:60000});}
    catch(error){report.nativeStartup=await nativePage.locator('#startup-message').textContent();throw error;}
    for(const [width,height] of [[2376,1080],[1080,491],[844,390],[390,844],[768,1024],[1280,800]]){
      await nativePage.setViewportSize({width,height});
      for(const screen of [80,100,150]){
        await nativePage.evaluate(screen=>{showMain();settings.screen=screen;sizeUi();},screen);
        check(await nativePage.evaluate(()=>{
          const r=$('menu-stage').getBoundingClientRect();
          return Math.abs(r.width/r.height-568/320)<.001&&r.x>=-.1&&r.y>=-.1&&r.right<=innerWidth+.1&&r.bottom<=innerHeight+.1
            &&Math.abs(r.x*2+r.width-innerWidth)<.1&&Math.abs(r.y*2+r.height-innerHeight)<.1;
        }),'Android menu keeps centered original composition '+[width,height,screen]);
        check(await nativePage.locator('#main-menu .game-title, #main-menu .main-entry').evaluateAll(nodes=>nodes.every(el=>{
          const r=el.getBoundingClientRect();return r.x>=-.1&&r.y>=-.1&&r.right<=innerWidth+.1&&r.bottom<=innerHeight+.1;
        })),'Android title and menu buttons fit');
        if(screen===100)await nativePage.screenshot({path:path.join(folder,`native-main-${width}x${height}.png`)});
        await nativePage.locator('[data-mode="2"]').click();await nativePage.waitForFunction(()=>!conquestEntering);
        check(await nativePage.locator('#scenarios').evaluate(el=>{
          const cards=[...el.children].map(c=>c.getBoundingClientRect()),scale=screenLayout.menuScale;
          return cards.length===6&&Math.abs((cards[1].left-cards[0].right)/scale-18)<.1
            &&Math.abs(cards[0].width/scale-222)<.1&&Math.abs(cards[0].height/scale-83)<.1;
        }),'Android conquest retains original card sizes and column gap');
        check(await nativePage.locator('#scenarios .scenario-entry, #conquest-pagination').evaluateAll(nodes=>nodes.every(el=>{
          const r=el.getBoundingClientRect();return r.x>=-.1&&r.y>=-.1&&r.right<=innerWidth+.1&&r.bottom<=innerHeight+.1;
        })),'Android conquest cards and pager fit');
        if(screen===100){
          await nativePage.screenshot({path:path.join(folder,`native-conquest-${width}x${height}.png`)});
          await nativePage.locator('#conquest-next').click();await nativePage.waitForFunction(()=>!conquestEntering);
          check(await nativePage.locator('#scenarios .scenario-placeholder').count()===6,'Android next conquest page accessible');
          await nativePage.locator('#conquest-prev').click();await nativePage.waitForFunction(()=>!conquestEntering);
          await nativePage.locator('#scenarios button').first().click();await nativePage.locator('#countries').waitFor();
          await nativePage.waitForFunction(()=>!busy);await nativePage.waitForTimeout(500);
          check(await nativePage.locator('#country-list, #confirm-country').evaluateAll(nodes=>nodes.every(el=>{
            const r=el.getBoundingClientRect();return r.x>=-.1&&r.y>=-.1&&r.right<=innerWidth+.1&&r.bottom<=innerHeight+.1;
          })),'Android country controls fit');
        }
        report.layouts.push({platform:'Android origin',width,height,screen});
      }
    }
    await nativePage.close();
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing resources');
  }catch(error){report.failure=error.message;await page.screenshot({path:path.join(folder,'failure.png')});throw error;}
  finally{await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,layouts:report.layouts.length,errors:report.errors.length,missing:report.missing.length}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
