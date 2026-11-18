'use strict';
// Run only against an isolated profile: this exercises real account transactions.
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]),renderOnly=process.argv.includes('--render-only');
if(!base||!out)throw Error('Expected isolated URL and output directory');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const source of [false,true]){
      const page=await browser.newPage({viewport:{width:1280,height:720}});
      if(source)await page.route(/\/compat\/[^/]+\.js(?:\?|$)/,route=>route.fulfill({path:path.resolve('rewrite/Ew4.Web/wwwroot',new URL(route.request().url()).pathname.split('/').pop())}));
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.evaluate(async()=>{
        await menus.headquarters();
        for(const p of menus.data.princesses)if(menus.data.roster.length<2&&!p.claimed)await menus.updateAccount({op:'claim',offer:p.id});
        window.fixture={data:menus.data,rpc:menus.api.rpc};
      });
      for(const [width,height] of [[1280,720],[844,390],[390,844]]){
        await page.setViewportSize({width,height});
        await page.evaluate(async()=>{
          const data=fixture.data,held=data.roster[0];
          menus.data={...data,roster:[{...held,healthBonus:123,healing:47}]};
          await menus.regroup(held.id);
        });
        check(await page.locator('[data-native-id=text_life]').getAttribute('aria-label')===null,'value is rendered using original text');
        check(await page.locator('[data-native-id=text_life] .original-text').getAttribute('aria-label')==='123','empty preview uses actual receiver health');
        check(await page.locator('[data-native-id=text_apply] .original-text').getAttribute('aria-label')==='47','empty preview uses actual receiver healing');
        check(await page.locator('[data-native-id=btn_regroup]').isDisabled(),'no donor cannot inherit');
        check(await page.locator('[data-native-id=text_notice] .original-text').getAttribute('aria-label').then(s=>s.includes('暂无')),'empty roster explains eligibility');
        check(await page.locator('.rank-gauge').count()===2,'empty preview initializes both ranks');
        check(await page.locator('[data-native-id=group_infos] [data-native-id=text_title]').evaluate(n=>n.nativeAttributes.color==='235,230,225,255'),'preview title has light ink');
        if(!source&&width===1280)await page.screenshot({path:path.join(out,'empty-regroup.png')});
        await page.evaluate(async()=>{
          const data=fixture.data,receiver=data.roster[0],donor=data.roster[1];
          menus.data={...data,roster:[receiver,{...donor,locked:true}]};await menus.regroup(receiver.id);
        });
        check(await page.locator('[data-native-id=lbox_general] .commander-card').count()===0,'deployed donor excluded');
        await page.evaluate(async()=>{
          const data=fixture.data,receiver=data.roster[0],donor=data.roster[1];
          const donors=Array.from({length:15},(_,i)=>({...donor,id:'fixture.donor.'+i,locked:false}));
          menus.data={...data,roster:[receiver,...donors]};
          menus.api.rpc=async(kind,args)=>kind==='commander'&&args.op==='preview_inherit'?{price:0,learning:receiver.learning}:fixture.rpc(kind,args);
          await menus.regroup(receiver.id,donors[0].id);
        });
        const list=page.locator('[data-native-id=lbox_general]');
        await list.evaluate(n=>n.scrollLeft=160);
        await list.locator('.commander-card').nth(3).locator('.commander-open').click();
        check(await list.evaluate(n=>n.scrollLeft>=159),'donor selection keeps horizontal position');
        const box=await list.boundingBox();
        await page.mouse.move(box.x+box.width*.8,box.y+box.height*.5);await page.mouse.down();await page.mouse.move(box.x+box.width*.25,box.y+box.height*.5,{steps:8});await page.mouse.up();
        check(await list.evaluate(n=>n.scrollLeft>160),'cards drag horizontally');
        check(await list.locator('[aria-pressed=true]').count()===1,'drag does not select another card');
        await page.evaluate(()=>{menus.api.rpc=fixture.rpc;menus.data=fixture.data;menus.acquiredCommanders(menus.data.roster.slice(1));});
        const popup=page.locator('[data-layout=form_getgeneraltips]');await popup.waitFor();
        check(await popup.locator('.commander-card').getAttribute('aria-label')===await page.evaluate(()=>menus.presentedCommander(menus.definition(menus.data.roster[0])).name),'acquisition shows actual commander');
        check(await page.locator('#detail-confirm').isVisible(),'original confirmation visible');
        if(!source&&width===1280)await page.screenshot({path:path.join(out,'acquisition.png')});
        check(await popup.evaluate(n=>{const r=n.parentElement.getBoundingClientRect();return r.left>=-.5&&r.top>=-.5&&r.right<=innerWidth+.5&&r.bottom<=innerHeight+.5;}),'acquisition fits viewport');
        await popup.locator('.commander-info').click();await page.locator('.commander-detail').waitFor();await page.locator('#close-detail').click();await popup.waitFor();
        check(await popup.isVisible(),'details return to acquisition notice');
        await page.locator('#detail-confirm').click();check(await page.locator('#detail-modal').isHidden(),'acquisition confirms once');
      }
      // A real medal purchase and reward claim, followed by a reload, prove notices
      // are driven by committed acquisitions, rather than a clicked offer.
      if(!renderOnly){
      await page.evaluate(async()=>{
        menus.data=await menus.api.rpc('headquarters');
        const offer=Object.entries(menus.data.offers).flatMap(([group,ids])=>ids.map(id=>({group:+group,id}))).find(o=>!menus.data.roster.some(c=>c.definitionId===o.id)&&menus.data.definitions.find(d=>d.id===o.id)?.price>0);
        if(!offer)throw Error('Isolated fixture needs an available offer');
        menus.recruiting=true;menus.rating=offer.group;menus.drawRoster();
        await menus.transact({op:'recruit',group:offer.group,commander:offer.id});
      });
      await page.locator('[data-layout=form_getgeneraltips]').waitFor();check(true,'actual recruitment opens native notice');
      await page.locator('#detail-confirm').click();
      await page.evaluate(()=>menus.princesses());
      const claim=page.locator('[data-layout=form_princess] [data-native-id^=btn_go_]:not(:disabled)').first();
      await claim.click();await page.locator('[data-layout=form_getgeneraltips]').waitFor();check(true,'actual reward claim opens native notice');
      await page.locator('#detail-confirm').click();await page.locator('[data-layout=form_princess]').waitFor();check(true,'claim returns to reward page');
      }
      if(!source){await page.setViewportSize({width:1280,height:720});await page.evaluate(async()=>{menus.data=await menus.api.rpc('headquarters');await menus.regroup(menus.data.roster[0].id);});await page.screenshot({path:path.join(out,'regroup.png')});}
      await page.reload();await page.waitForFunction(()=>startupReady);check(await page.locator('#detail-modal').isHidden(),'reload does not replay acquisitions');
      await page.close();
    }
    check(!report.errors.length,'no browser exceptions');check(!report.missing.length,'no missing resources');
  }finally{await browser.close();await fs.writeFile(path.join(out,'results.json'),JSON.stringify(report,null,2));}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
