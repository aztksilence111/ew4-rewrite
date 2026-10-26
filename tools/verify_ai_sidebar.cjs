'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5579',folder=path.resolve(process.argv[3]||'build/ai-sidebar-20261026/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  const page=await browser.newPage({viewport:{width:1280,height:720}}),report={checks:0,errors:[],missing:[],views:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition);
  const settled=()=>page.waitForFunction(()=>!$('pause').classList.contains('pause-sidebars-pending')&&document.querySelectorAll('#pause .ai-control-command').length===4);
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await page.evaluate(()=>start('conquest1','fra'));await idle();
    let release;const gate=new Promise(resolve=>release=resolve);
    await page.route('**/api/pause-objectives?*',async route=>{await gate;await route.continue();});
    await page.locator('#menu-button').click();
    check(await page.evaluate(()=>getComputedStyle($('ai-control')).visibility==='hidden'&&getComputedStyle($('pause-objectives')).visibility==='hidden'),
      'both sidebars wait together for objective data');
    release();await settled();await page.unroute('**/api/pause-objectives?*');
    await page.evaluate(()=>Promise.all([$('pause-objectives'),$('ai-control')].map(el=>el.getAnimations()[0].ready)));
    const animations=await page.evaluate(()=>[$('pause-objectives'),$('ai-control')].map(el=>{const a=el.getAnimations()[0];return {name:a?.animationName,start:a?.startTime,duration:a?.effect.getTiming().duration};}));
    check(animations[0].name==='objective-slide'&&animations[1].name==='ai-sidebar-slide','mirrored entrance animations');
    check(animations.every(a=>a.start!==null)&&Math.abs(animations[0].start-animations[1].start)<1&&animations[0].duration===animations[1].duration,
      'both animations share start time and duration: '+JSON.stringify(animations));
    const positions=await page.evaluate(()=>{
      const elements=[$('pause-objectives'),$('ai-control')],animations=elements.map(el=>el.getAnimations()[0]);
      const sample=time=>{animations.forEach(a=>{a.pause();a.currentTime=time;});return elements.map(el=>el.getBoundingClientRect().toJSON());};
      const start=sample(0),middle=sample(140),end=sample(280);return {start,middle,end,width:innerWidth};
    });
    check(positions.start[0].right<=1&&positions.start[1].left>=positions.width-1,'entrance starts outside both screen edges');
    check(positions.middle[0].left<positions.end[0].left&&positions.middle[1].left>positions.end[1].left,'left and right panels move toward map center');
    check(Math.abs(positions.end[1].right-positions.width)<1,'AI panel ends flush with right edge');
    check(await page.evaluate(()=>getComputedStyle($('ai-control')).boxShadow==='none'&&getComputedStyle(document.querySelector('.ai-control-body')).backgroundImage==='none'),
      'AI panel has flat unshadowed background');
    check(await page.locator('#ai-clear-goal,#ai-cancel-draft').count()===0,'removed standalone clear and reset lines');
    await page.screenshot({path:path.join(folder,'desktop.png')});
    await page.locator('#resume').click();await idle();await page.locator('#menu-button').click();await settled();
    await page.evaluate(()=>Promise.all([$('pause-objectives'),$('ai-control')].map(el=>el.getAnimations()[0].ready)));
    check(await page.evaluate(()=>{const a=$('pause-objectives').getAnimations()[0],b=$('ai-control').getAnimations()[0];return a&&b&&a.startTime===b.startTime&&a.playState==='running';}),
      'cached objective data replays both entrance animations together');
    await page.waitForTimeout(320);
    await page.evaluate(async()=>{const u=state.units.find(u=>u.owner===state.playerFaction&&u.canMove);window.sidebarUnit=u.id;window.sidebarGoal=u.cell;
      await act('player_ai_config',{offer:'policy',policy:'hold'});await act('player_ai_config',{offer:'units',units:[u.id]});await act('player_ai_config',{offer:'goal',cell:u.cell});
      const p=point(u.cell);view.x=p.x;view.y=p.y;view.zoom=1;clampView();requestRender();});
    await page.locator('#ai-goal').click();
    const click=await page.evaluate(()=>{const p=point(window.sidebarGoal);return{x:(p.x-view.x)*view.zoom+viewport.w/2,y:(p.y-view.y)*view.zoom+viewport.h/2};});
    await page.mouse.click(click.x,click.y);
    check(await page.evaluate(()=>aiControl.target===null),'clicking chosen target deselects it');
    await page.locator('#ai-map-confirm').click();await idle();await settled();
    check(await page.evaluate(()=>state.playerAi.draft.goal===null&&!state.playerAi.draft.goalConfirmed&&!state.playerAi.enabled),'confirming no target clears goal without starting AI');
    await page.waitForTimeout(320);await page.locator('#ai-units').click();await page.mouse.click(click.x,click.y);
    check(await page.evaluate(()=>aiControl.chosen.size===0&&!$('ai-map-confirm').disabled),'all units can be deselected and confirmed');
    await page.locator('#ai-map-confirm').click();await idle();await settled();
    check(await page.evaluate(()=>!state.playerAi.draft.policyConfirmed&&!state.playerAi.draft.unitsConfirmed&&!state.playerAi.draft.goalConfirmed&&$('ai-execute').disabled),
      'empty unit confirmation resets draft and locks start');
    await page.waitForTimeout(320);
    for(const [width,height] of [[1392,1174],[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(100);await page.evaluate(()=>{pauseObjectives.fit();aiControl.fit();});
      const layout=await page.evaluate(()=>{const r=$('ai-control').getBoundingClientRect();return {right:r.right,top:r.top,bottom:r.bottom,left:r.left,width:innerWidth,height:innerHeight};});
      check(Math.abs(layout.right-width)<1&&layout.top>=0&&layout.bottom<=height,'flush AI panel within '+width+'x'+height);
      if(width<760||height<430){
        if(!await page.evaluate(()=>$('pause').classList.contains('ai-sidebar-expanded')))await page.locator('.ai-control-title').click();
        check(await page.evaluate(()=>getComputedStyle($('pause-objectives')).display!=='none'&&getComputedStyle($('ai-control')).display!=='none'),'both sidebars remain mounted in compact layout');
      }
      await page.screenshot({path:path.join(folder,'pause-'+width+'x'+height+'.png')});report.views.push([width,height]);
    }
    await page.emulateMedia({reducedMotion:'reduce'});await page.locator('#resume').click();await page.locator('#menu-button').click();await settled();
    check(await page.evaluate(()=>[$('pause-objectives'),$('ai-control')].every(el=>getComputedStyle(el).animationName==='none')),'reduced motion disables both animations');
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing assets');
    await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
