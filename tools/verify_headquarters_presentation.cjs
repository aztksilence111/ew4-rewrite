'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56142',folder=path.resolve(process.argv[3]||'build/hq-presentation-20261029/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  if(process.argv[2]==='--prepare'){
    const root=path.resolve(__dirname,'..'),example=path.join(root,'rewrite/mod-kit/examples/headquarters-layout/rewrite'),fixture=path.join(folder,'rewrite');
    await fs.cp(example,fixture,{recursive:true});await fs.mkdir(path.join(fixture,'images'),{recursive:true});
    await fs.copyFile(path.join(root,'rewrite/data/client/ui/image_ui_hd/medals.png'),path.join(fixture,'images/custom-medal.png'));
    const config=JSON.parse(await fs.readFile(path.join(fixture,'headquarters.json'),'utf8'));
    config.screens.roster.layout.children[0].children.find(n=>n.attributes.id==='medalsIcon').attributes.name='rewrite/images/custom-medal.png';
    await fs.writeFile(path.join(fixture,'headquarters.json'),JSON.stringify(config,null,2)+'\n');
    console.log(JSON.stringify({prepared:folder,files:3}));return;
  }
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720},hasTouch:true});
  if(process.argv.includes('--source'))await page.route(base+'/',async route=>{
    const response=await route.fetch();await route.fulfill({response,body:(await response.text()).replace(/((?:src|href)=")compat\//g,'$1')});
  });
  const report={checks:0,errors:[],missing:[]},check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  const custom=()=>page.locator('.hq-custom-surface'),button=id=>custom().locator('[data-native-id="'+id+'"]');
  const closeDetail=async()=>{await page.locator('#close-detail').click();await page.waitForFunction(()=>document.getElementById('detail-modal').hidden);};
  const shot=async name=>{await page.waitForFunction(()=>[...document.querySelectorAll('.hq-custom-surface img')].every(i=>i.complete&&i.naturalWidth));await page.screenshot({path:path.join(folder,name+'.png')});};
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady);
    check(await page.evaluate(()=>catalog.headquartersPresentation?.screens.roster.layout.attributes.w)==='640','real imported layout arrives through catalog');
    await page.locator('#headquarters').click();
    await page.waitForSelector('.hq-custom-surface');
    check(await custom().count()===1,'one custom screen');
    check(await custom().locator('button.native-node').count()===4,'four authored buttons, no built in toolbar buttons');
    check(await page.locator('#hq-filter-main').isHidden(),'no visible filter button');
    check(await page.locator('#hq-roster .empty-commander:visible').count()===0,'empty slots optional');
    check(await custom().locator('[data-native-id=medalsIcon] img').evaluate(async img=>{await img.decode();return img.src.includes('/extensions/images/custom-medal.png')&&img.naturalWidth>0;}),'package PNG renders through public extension image route');
    await page.evaluate(async()=>{
      for(let group=0;group<3;group++){
        const id=menus.data.offers[group].find(id=>!menus.data.roster.some(c=>c.definitionId===id));
        if(id){const d=menus.data.definitions.find(d=>d.id===id);await menus.updateAccount({op:d.price>0?'recruit':'recruit_badge',commander:id,group});}
      }
    });
    for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844],['wide',1920,800]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(100);
      const geometry=await custom().evaluate(el=>{
        const r=el.getBoundingClientRect();return {ratio:r.width/r.height,visible:r.left>=-1&&r.top>=-1&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1,
          inside:[...el.querySelectorAll('button.native-node')].every(b=>{const q=b.getBoundingClientRect();return q.left>=r.left-1&&q.right<=r.right+1&&q.top>=r.top-1&&q.bottom<=r.bottom+1;})};
      });
      check(Math.abs(geometry.ratio-640/360)<.001,name+' keeps authored aspect ratio');check(geometry.visible&&geometry.inside,name+' controls fit viewport');
      await shot(name);
    }
    await page.setViewportSize({width:1280,height:720});
    await button('tools').click();check(await custom().getAttribute('data-layout')==='headquarters-management','new authored button opens custom page');
    check(await custom().locator('button.native-node').count()===5,'different page can contain different number of buttons');
    for(const [id,kind] of [['tasks','task'],['achievements','achievement']]){
      await button(id).click();await page.waitForSelector('.hq-objectives');check(await page.locator('.hq-objectives').getAttribute('data-kind')===kind,id+' executes existing command');await closeDetail();
    }
    await button('tags').click();check(await button('tags').getAttribute('aria-label')==='确认','author controls editing label');
    const held=await page.locator('#hq-roster [data-commander]').first().getAttribute('data-commander');
    const priorTagged=await page.evaluate(id=>!!menus.data.roster.find(c=>c.id===id).tag,held);
    await page.locator('#hq-roster [data-commander="'+held+'"] .commander-open').click();await button('tags').click();
    await page.waitForFunction(()=>!menus.tagDraft&&!menus.tagSaving);
    check(await page.evaluate(id=>!!menus.data.roster.find(c=>c.id===id).tag,held)!==priorTagged,'tag action saves through existing backend');
    check(await custom().getAttribute('data-layout')==='headquarters-management','editing preserves custom page');
    await shot('management');await button('back').click();
    check(await custom().getAttribute('data-layout')==='headquarters-roster','custom page returns to roster');
    await button('shop').click();await page.waitForSelector('.native-layout[data-layout=form_shop]');check(await page.locator('#detail-modal').isVisible(),'custom shop action opens native workflow');await closeDetail();
    await button('academy').click();check(await custom().getAttribute('data-layout')==='headquarters-academy','academy has separate authored layout');
    check(await button('buy').isDisabled(),'purchase disabled before selection');
    await button('high').click();check(await page.evaluate(()=>menus.rating)===2,'custom star button selects actual academy group');
    await button('low').click();await page.locator('#hq-roster [data-offer] .commander-open').first().click();
    check(await button('buy').isEnabled(),'selecting candidate enables authored purchase button');
    const before=await page.evaluate(()=>({medals:menus.data.medals,count:menus.data.roster.length,price:menus.offer.price,id:menus.offer.id}));
    await button('buy').click();await page.waitForFunction(n=>menus.data.roster.length===n+1,before.count);
    check(await page.evaluate(b=>menus.data.medals===b.medals-b.price&&menus.data.roster.some(c=>c.definitionId===b.id),before),'real purchase charges once and adds selected commander');
    check(await button('buy').isDisabled(),'purchase remains disabled after selection cleared');
    await button('refresh').click();await page.waitForFunction(()=>!menus.refreshPending);check(await button('buy').isDisabled(),'refresh clears selection');
    check(await page.locator('#hq-buy-buttons').isHidden(),'default purchase UI stays hidden');await shot('academy');
    await button('back').click();
    await page.evaluate(()=>{
      const s=catalog.headquartersPresentation.screens.roster;
      s.layout.children.push({attributes:{id:'extraTask',type:'tmp_button',x:'228',y:'308',w:'80',h:'36',text:'新增任务',frm1:'btn_common_blue.png'},children:[]});
      s.actions.extraTask={command:'tasks'};menus.drawRoster();
    });
    check(await custom().locator('button.native-node').count()===5,'adding file node and binding adds a button');
    await button('extraTask').click();await page.waitForSelector('.hq-objectives');await closeDetail();
    await page.evaluate(()=>{const s=catalog.headquartersPresentation.screens.roster;s.layout.children=s.layout.children.filter(n=>!['extraTask','shop'].includes(n.attributes.id));delete s.actions.extraTask;delete s.actions.shop;menus.drawRoster();});
    check(await custom().locator('button.native-node').count()===3,'removing nodes and bindings removes buttons');
    await page.evaluate(()=>{
      const s=catalog.headquartersPresentation.screens.roster;
      for(const [id,section] of [['sectionOne',0],['sectionAll',null]]){
        s.layout.children.push({attributes:{id,type:'tmp_button',x:id==='sectionOne'?'240':'340',y:'308',w:'90',h:'36',text:id},children:[]});s.actions[id]={command:'filter',section};
      }menus.drawRoster();
    });
    await button('sectionOne').click();
    check(await page.evaluate(()=>menus.rosterRating)===0,'author can add a direct section filter without the default menu');
    check(await page.locator('#hq-roster [data-commander]:visible').count()===await page.evaluate(()=>menus.data.roster.filter(c=>menus.data.definitions.find(d=>d.id===c.definitionId).rating<=3).length),'direct filter shows matching owned commanders');
    await button('sectionAll').click();
    await page.evaluate(()=>{
      const s=JSON.parse(JSON.stringify(catalog.headquartersPresentation.screens.roster));s.actions={'header/back':{command:'back'},academy:{command:'info'}};
      catalog.headquartersPresentation.screens.deployment=s;menus.deployment='custom-deployment-test';menus.deploymentQuota={used:0,limit:5};menus.rosterSelected=null;menus.drawRoster();
    });
    check(await custom().getAttribute('data-layout')==='headquarters-deployment','deployment accepts its own full layout');
    check(await button('academy').isDisabled(),'info starts disabled with no selection');
    await page.locator('#hq-roster [data-commander] .commander-open').first().click();
    check(await button('academy').isEnabled(),'deployment selection updates custom command state');
    await button('academy').click();await page.waitForSelector('#detail-modal:not([hidden])');await closeDetail();
    await button('back').click();await page.evaluate(async()=>{await menus.api.main();await menus.headquarters();});
    check(await custom().getAttribute('data-layout')==='headquarters-roster','deployment returns without leaking its layout');
    await page.evaluate(()=>{delete catalog.headquartersPresentation;menus.drawRoster();});
    check(await custom().count()===0&&await page.locator('#hq-filter-main').isVisible(),'missing definition restores default UI');
    check(await page.locator('#hq-roster').evaluate(el=>el.parentElement.id==='hq-screen'),'roster restored to original host');
    await page.locator('#hq-recruit').click();check(await page.locator('#hq-filters [role=tab]').count()===3,'default academy still works');
    check(report.errors.length===0,report.errors.join('; '));check(report.missing.length===0,report.missing.join('; '));
    console.log(JSON.stringify(report));
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(path.join(folder,'result.json'),JSON.stringify(report,null,2));await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
