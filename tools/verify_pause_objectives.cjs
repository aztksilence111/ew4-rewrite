'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5561',out=path.resolve(process.argv[3]||'build/pause-objectives-20261025/browser');
const profile=path.resolve(process.argv[4]||'build/pause-objectives-20261025/profile');
(async()=>{
  await fs.mkdir(out,{recursive:true});const report={checks:0,errors:[],missing:[],cases:[],scope:'Actual Edge, viewport emulation; not a physical Android device.'};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:process.env.EW4_BROWSER||'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720}});page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.status()+' '+r.url());});
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    for(const scenario of ['campaign1_01','conquest1']){
      await page.evaluate(id=>start(id,id==='conquest1'?'fra':null),scenario);
      await page.evaluate(()=>{$('detail-modal').hidden=true;$('menu-button').click();});
      await page.waitForFunction(()=>!$('pause-objectives').hidden);await page.waitForTimeout(350);
      check(await page.locator('#pause-objectives').innerText().then(s=>s.includes('胜利条件')&&s.includes('胜利评价')),scenario+' real rule sections');
      for(const size of [{width:1280,height:720},{width:912,height:414},{width:829,height:405},{width:640,height:360},{width:568,height:320},{width:405,height:829},{width:360,height:740}]){
        await page.setViewportSize(size);await page.waitForTimeout(250);
        const geometry=await page.evaluate(()=>{
          const rects=()=>['#pause>.pause-dialog','#pause-objectives','.pause-objectives-bar','.pause-objectives-foot'].map(selector=>document.querySelector(selector).getBoundingClientRect().toJSON());
          const baseline=JSON.stringify(rects()),root=document.documentElement;
          const safeNames=['left','right','top','bottom'].map(side=>'--screen-safe-'+side),previous=safeNames.map(name=>[root.style.getPropertyValue(name),root.style.getPropertyPriority(name)]);
          safeNames.forEach((name,i)=>root.style.setProperty(name,[48,32,18,24][i]+'px','important'));pauseObjectives.fit();
          const ignoresCamera=JSON.stringify(rects())===baseline;
          safeNames.forEach((name,i)=>previous[i][0]?root.style.setProperty(name,...previous[i]):root.style.removeProperty(name));pauseObjectives.fit();
          const menu=document.querySelector('#pause>.pause-dialog').getBoundingClientRect(),panel=$('pause-objectives').getBoundingClientRect();
          const scroll=document.querySelector('.pause-objectives-scroll');
          const bars=['.pause-objectives-bar','.pause-objectives-foot'].map(selector=>document.querySelector(selector).getBoundingClientRect());
          return {viewport:[innerWidth,innerHeight],center:[menu.x+menu.width/2,menu.y+menu.height/2],menu:menu.toJSON(),panel:panel.toJSON(),
            ignoresCamera,barsFromEdge:bars.every(r=>r.left===0&&Math.abs(r.right-(panel.right-1))<1),
            fontSize:parseFloat(getComputedStyle(scroll).fontSize),
            inside:[...document.querySelectorAll('#pause>.pause-dialog>button')].every(e=>{const r=e.getBoundingClientRect();return r.top>=menu.top&&r.bottom<=menu.bottom&&r.left>=menu.left&&r.right<=menu.right;}),
            overlap:!(panel.right<=menu.left||panel.bottom<=menu.top||panel.left>=menu.right||panel.top>=menu.bottom),
            scroll:[scroll.clientHeight,scroll.scrollHeight],missing:[...document.querySelectorAll('#pause img')].filter(i=>!i.complete||!i.naturalWidth).length};
        });
        check(Math.abs(geometry.center[0]-size.width/2)<1&&Math.abs(geometry.center[1]-size.height/2)<1,'pause remains screen centered');
        check(geometry.inside&&!geometry.overlap&&geometry.panel.x===0,'left panel yields to complete pause menu: '+JSON.stringify(geometry));
        check(geometry.panel.y>=0&&geometry.panel.bottom<=size.height&&geometry.menu.y>=0&&geometry.menu.bottom<=size.height,'all surfaces on screen');
        check(geometry.scroll[0]>35&&geometry.missing===0,'usable scroll and complete images');
        check(geometry.ignoresCamera&&geometry.barsFromEdge&&geometry.fontSize===16,'camera insets ignored, full width bars and compact text');
        await page.screenshot({path:path.join(out,scenario+'-'+size.width+'x'+size.height+'.png')});report.cases.push({scenario,...geometry});
      }
      await page.evaluate(()=>{window.pauseRow=document.querySelector('.pause-objective-row');window.pauseStats=[pauseObjectives.refreshes,pauseObjectives.rowUpdates];window.pauseMutations=0;window.pauseObserver=new MutationObserver(records=>pauseMutations+=records.length);pauseObserver.observe($('pause-objectives'),{childList:true,subtree:true,characterData:true});document.querySelector('.pause-objectives-scroll').scrollTop=80;});
      await page.locator('#resume').click();await page.locator('#menu-button').click();await page.waitForTimeout(350);
      const unchanged=await page.evaluate(()=>({same:pauseRow===document.querySelector('.pause-objective-row'),stats:JSON.stringify(pauseStats)===JSON.stringify([pauseObjectives.refreshes,pauseObjectives.rowUpdates]),mutations:pauseMutations,scroll:document.querySelector('.pause-objectives-scroll').scrollTop}));
      check(unchanged.same&&unchanged.stats&&unchanged.mutations===0&&unchanged.scroll===80,'reopening reuses content and scroll');
      await page.evaluate(()=>pauseObserver.disconnect());
      await page.locator('#pause-settings').click();check(await page.locator('#settings').isVisible(),'existing settings opens above pause');await page.locator('#close-settings').click();
      await page.locator('#save').click();await page.waitForTimeout(150);check(await page.locator('#pause').isVisible(),'existing save command remains usable');
    }
    for(const slot of [1,2,3])await fs.copyFile(path.resolve('build/pause-objectives-20261025/fixtures/slot-'+slot+'.json'),path.join(profile,'slot-'+slot+'.json'));
    await page.evaluate(async()=>{await installState(await rpc('load',{slot:1}),false);$('detail-modal').hidden=true;});
    await page.waitForFunction(()=>pauseObjectives.snapshot?.context.startsWith('pause-browser/'));await page.waitForTimeout(250);
    await page.evaluate(()=>{window.beforeRound={version:pauseObjectives.version,refreshes:pauseObjectives.refreshes,rows:pauseObjectives.rowUpdates,node:document.querySelector('.pause-objective-row')};});
    await page.evaluate(async()=>{await installState(await rpc('load',{slot:2}),false);});await page.waitForTimeout(350);
    check(await page.evaluate(()=>state.round===2&&pauseObjectives.version===beforeRound.version&&pauseObjectives.refreshes===beforeRound.refreshes&&pauseObjectives.rowUpdates===beforeRound.rows&&document.querySelector('.pause-objective-row')===beforeRound.node),'unchanged new round does not refresh goal content');
    await page.evaluate(async()=>{await installState(await rpc('load',{slot:3}),false);});await page.waitForTimeout(350);
    check(await page.evaluate(()=>state.round===9&&pauseObjectives.version!==beforeRound.version&&document.querySelector('.pause-objective-row')===beforeRound.node&&document.querySelector('.pause-rating-row.expired')),'star expiry updates rating while retaining goal nodes');
    await page.setViewportSize({width:1280,height:720});await page.waitForTimeout(200);await page.screenshot({path:path.join(out,'rating-expired-1280x720.png')});
    check(report.errors.length===0&&report.missing.length===0,'no page errors or failed resources');
    await fs.writeFile(path.join(out,'verification.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,cases:report.cases.length}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
