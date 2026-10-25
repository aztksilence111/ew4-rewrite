'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5533',folder=path.resolve(process.argv[3]||'build/ui-feedback-20261025/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],layouts:[],scope:'Isolated profile, desktop and touch emulation; no physical Android execution'};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:844,height:390},hasTouch:true});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  const shot=async name=>{
    await page.waitForFunction(()=>[...document.querySelectorAll('#hq-screen:not([hidden]) img,#single-action:not([hidden]) img')].every(i=>i.complete&&i.naturalWidth));
    await page.screenshot({path:path.join(folder,name+'.png')});
  };
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    await page.evaluate(async()=>{
      await menus.headquarters();
      if(!menus.data.roster.length)await menus.updateAccount({op:'recruit',commander:menus.data.offers[0][0]});
      await menus.updateAccount({op:'tags',taggedCommanders:[menus.data.roster[0].id]});
      await start('conquest1','fra');
    });
    const unit=await page.evaluate(()=>state.units.find(u=>u.owner===state.playerFaction&&!u.commanderId&&state.cells[u.cell].owner===u.owner).id);
    const sizes=[['phone',844,390],['reported',2376,1080],['portrait',390,844],['desktop',1280,720],['tablet',1024,768]];
    for(const [name,width,height] of sizes){
      await page.setViewportSize({width,height});
      for(const preference of [80,100,150]){
        await page.evaluate(async({unit,preference})=>{
          settings.screen=preference;sizeUi();await selectCell(state.units.find(u=>u.id===unit).cell,true,true);
        },{unit,preference});
        const anchor=page.locator('#actions [data-action="disband"]');
        await anchor.scrollIntoViewIfNeeded();await anchor.click();
        const layout=await page.evaluate(()=>{
          const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom};};
          return {anchor:rect(pendingAction.anchor),confirm:rect($('single-action-confirm')),panel:rect($('single-action-cost')),revision:state.revision};
        });
        const {anchor:a,confirm:b,panel:p}=layout,label=name+' '+preference;
        check(Math.abs(a.width-b.width)<.1&&Math.abs(a.height-b.height)<.1,label+' confirmation matches source size');
        check(Math.abs(a.x-b.x)<.1&&Math.abs(a.y-b.y)<.1,label+' confirmation aligns with source');
        check([b,p].every(r=>r.x>=-.1&&r.y>=-.1&&r.right<=width+.1&&r.bottom<=height+.1),label+' quote and confirmation fit');
        check(Math.abs(p.width/b.width-84/33)<.01&&p.bottom<b.y,label+' compact quote stays above confirmation');
        if(preference===100)await shot(name+'-action');
        await page.evaluate(()=>closeSingleAction());
        check(await page.evaluate(revision=>state.revision===revision,layout.revision),label+' cancel preserves state');
        report.layouts.push({name,preference,...layout});
      }
      await page.evaluate(async unit=>{await menus.deployGeneral(unit,state.revision);},unit);
      check(await page.locator('#hq-deployment-quota').evaluate(e=>{const r=e.getBoundingClientRect();return Math.abs((r.left+r.right)/2-innerWidth/2)<.5;}),name+' entire quota is centered');
      check(await page.locator('#hq-roster .commander-card').count()===60,name+' HQ capacity supplies all slots');
      check(await page.locator('#hq-tags,#hq-princess,#hq-shop,#hq-scrollbar').count()===0,name+' deployment adds no controls');
      check(await page.locator('#hq-roster .commander-card').first().locator('.commander-tag').count()===1,name+' saved HQ tag is pinned first');
      check(await page.locator('#hq-roster').evaluate(e=>{
        const r=e.getBoundingClientRect(),cards=[...e.children].map(c=>c.getBoundingClientRect());
        return e.scrollLeft===0&&e.scrollWidth>e.clientWidth&&cards.every(c=>c.top>=r.top-.5&&c.bottom<=r.bottom+.5)
          &&Math.abs(cards[0].left-cards[1].left)<.5&&cards[0].bottom<=cards[1].top;
      }),name+' two complete rows overflow horizontally');
      const roster=await page.locator('#hq-roster').boundingBox(),x=roster.x+Math.min(roster.width-10,240),y=roster.y+20;
      const cdp=await page.context().newCDPSession(page);
      for(const direction of [-1,1]){
        const start=direction<0?x:x-120;
        await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:start,y}]});
        for(let i=1;i<=10;i++)await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:start+direction*i*12,y}]});
        await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        check(await page.locator('#hq-roster').evaluate((e,d)=>d<0?e.scrollLeft>0:e.scrollLeft<2,direction),name+' touch swipe '+direction);
      }
      await cdp.detach();
      check(await page.locator('#detail-modal').isHidden(),name+' swiping opens no commander');
      await page.locator('#hq-roster').evaluate(e=>{e.scrollLeft=e.scrollWidth;});
      check(await page.locator('#hq-roster').evaluate(e=>{const r=e.getBoundingClientRect(),c=e.lastElementChild.getBoundingClientRect();return c.left>=r.left-1&&c.right<=r.right+1;}),name+' final slot reachable');
      await page.locator('#hq-roster').evaluate(e=>{e.scrollLeft=0;});await shot(name+'-roster');
      await page.evaluate(()=>menus.closeRoster());
    }
    await page.evaluate(async unit=>{
      await menus.deployGeneral(unit,state.revision);
      const icon=manifest.layouts.form_deploygeneral.children.find(e=>e.attributes.id==='image_depnums').attributes;
      window.originalQuotaIcon={...icon};Object.assign(icon,{name:'marker_money.png',w:'32',h:'20'});menus.drawRoster();
    },unit);
    check(await page.locator('#hq-deployment-quota>img').evaluate(e=>e.src.includes('marker_money.png')&&e.width===32&&e.height===20),'layout exposes quota image and dimensions');
    check(await page.locator('#hq-deployment-quota').evaluate(e=>{const r=e.getBoundingClientRect();return Math.abs((r.left+r.right)/2-innerWidth/2)<.5;}),'replacement icon preserves centered group');
    await page.evaluate(()=>{manifest.layouts.form_deploygeneral.children.find(e=>e.attributes.id==='image_depnums').attributes=originalQuotaIcon;menus.drawRoster();});
    const revision=await page.evaluate(()=>state.revision);
    await page.locator('#hq-roster .commander-open').first().click();await page.locator('#hq-roster .deploy-card-confirm').first().click();
    await page.waitForFunction(revision=>!busy&&state.revision>revision,revision);
    await page.evaluate(async unit=>{await menus.deployGeneral(unit,state.revision);},unit);
    check(await page.locator('#hq-deployment-quota .original-text').getAttribute('aria-label')==='1 / 12','real deployment increments authoritative quota');
    await page.evaluate(()=>menus.closeRoster());
    check(report.errors.length===0&&report.missing.length===0,'no browser errors or missing images');
  }finally{await browser.close();await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors,missing:report.missing,folder}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
