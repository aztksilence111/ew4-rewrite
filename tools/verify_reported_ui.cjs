'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5584',folder=path.resolve(process.argv[3]||'build/ui-feedback-fix-20261027/browser');
const report={checks:0,errors:[],missing:[],views:[],scope:'Current frontend and isolated profile, Edge desktop/touch; no physical Android device'};
const check=(value,label)=>{assert.ok(value,label);report.checks++;};
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio','--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  try{
    for(const renderer of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width:844,height:390},hasTouch:true});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+'/?renderer='+renderer);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(()=>start('conquest1','fra'));await page.waitForFunction(()=>state&&!busy&&!aiRunning);
      check(await page.evaluate(want=>!!ctx.gpu===want,renderer==='pixi'),'requested renderer '+renderer);
      const selections=await page.evaluate(async()=>{
        const cells=state.cells.map((s,i)=>({s,i})),empty=c=>c.s.active&&!state.units.some(u=>u.cell===c.i);
        const unit=state.units.find(u=>u.owner===state.playerFaction),cases=[unit.cell,
          cells.find(c=>empty(c)&&c.s.buildingKind==='city').i,cells.find(c=>empty(c)&&c.s.buildingKind==='farmland').i,
          cells.find(c=>empty(c)&&!c.s.buildingKind&&Ew4Battlefield.terrainRows(c.s).length).i];
        const result=[];
        for(const cell of cases){
          await selectCell(cell,true,true);const column=$('terrain-info').firstElementChild,resources=[...document.querySelectorAll('#resources canvas')];
          await selectCell(cell,true,true);
          const restored=selection?.cell===cell&&column===$('terrain-info').firstElementChild;
          await selectCell(cell);const cancelled=!selection&&!selected&&$('selection').hidden&&$('undo-move').disabled;
          await selectCell(cell);
          result.push({cell,restored,cancelled,reselected:selection?.cell===cell,stableInfo:column===$('terrain-info').firstElementChild,
            stableResources:resources.every((el,i)=>el===document.querySelectorAll('#resources canvas')[i])});
          await selectCell(cell);
        }
        return result;
      });
      for(const item of selections)check(item.restored&&item.cancelled&&item.reselected&&item.stableInfo&&item.stableResources,'selection/restore/cache '+renderer+' '+JSON.stringify(item));
      check(await page.evaluate(async()=>{
        const original=rpc,cells=state.cells.map((site,cell)=>({site,cell})).filter(c=>c.site.active).slice(0,2);
        let release,seen;const gate=new Promise(r=>release=r),requested=new Promise(r=>seen=r);
        rpc=async function(route,args){const result=await original(route,args);if(route==='action'&&args.op==='select'&&args.cell===cells[0].cell){seen();await gate;}return result;};
        try{
          const earlier=selectCell(cells[0].cell,true,true);await requested;await selectCell(cells[1].cell,true,true);release();await earlier;
          return selection.cell===cells[1].cell;
        }finally{release();rpc=original;}
      }),'late cell response cannot overwrite newer selection '+renderer);
      check(await page.evaluate(async()=>{
        const unit=state.units.find(u=>u.owner===state.playerFaction&&u.category==='infantry');await selectCell(unit.cell,true,true);
        const p=point(unit.cell);view.x=p.x;view.y=p.y;clampView();requestRender();return true;
      }),'center real touch target');
      const hit=await page.evaluate(()=>{const p=point(selected.cell);return {x:(p.x-view.x)*view.zoom+viewport.w/2,y:(p.y-view.y)*view.zoom+viewport.h/2};});
      await page.touchscreen.tap(hit.x,hit.y);check(await page.evaluate(()=>!selection&&$('selection').hidden),'actual map tap cancels unit '+renderer);
      await page.touchscreen.tap(hit.x,hit.y);await page.waitForFunction(()=>!!selection);check(await page.evaluate(()=>!!selected),'actual map tap reselects '+renderer);
      check(await page.evaluate(async()=>{
        const unit=selected,origin=unit.cell,destination=selection.range.find(cell=>!state.units.some(u=>u.cell===cell));
        if(destination==null)return false;
        await selectCell(destination);const moved=state.units.find(u=>u.id===unit.id).cell===destination;
        await act('undo',{unit:unit.id});return moved&&state.units.find(u=>u.id===unit.id).cell===origin;
      }),'normal move and undo survive selection toggle '+renderer);
      for(const [width,height] of [[1280,720],[844,390],[390,844],[568,320]]){
        await page.setViewportSize({width,height});await page.locator('#menu-button').tap();
        await page.waitForFunction(()=>!$('pause').classList.contains('pause-sidebars-pending'));await page.waitForTimeout(300);
        const geometry=await page.evaluate(()=>{
          const menu=document.querySelector('.pause-dialog'),bar=menu.querySelector('.menu-bar'),title=bar.querySelector('h2 canvas');
          const m=menu.getBoundingClientRect(),b=bar.getBoundingClientRect(),t=title.getBoundingClientRect(),before=getComputedStyle(bar,'::before'),after=getComputedStyle(bar,'::after');
          const scale=b.width/bar.offsetWidth,left=b.left+(parseFloat(before.left)+parseFloat(before.width))*scale,right=b.right-(parseFloat(after.right)+parseFloat(after.width))*scale;
          return {inside:m.left>=0&&m.right<=innerWidth+1&&m.top>=0&&m.bottom<=innerHeight+1,gapDifference:Math.abs((t.left-left)-(right-t.right)),center:Math.abs((m.left+m.right)/2-innerWidth/2)};
        });
        check(geometry.inside&&geometry.center<1&&geometry.gapDifference<2,'pause fits with balanced header '+renderer+' '+width+' '+JSON.stringify(geometry));
        await page.screenshot({path:path.join(folder,renderer+'-pause-'+width+'.png')});report.views.push({renderer,width,height,...geometry});
        await page.locator('#resume').tap();
      }
      await page.setViewportSize({width:844,height:390});
      const menu=page.locator('#menu-button');await menu.hover();check(await menu.evaluate(el=>getComputedStyle(el).filter==='none'),'pause hover has no brightness jump '+renderer);
      const box=await menu.boundingBox();await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();
      check(await menu.evaluate(el=>getComputedStyle(el).filter==='none'),'pause press has no brightness jump '+renderer);await page.mouse.up();
      await page.waitForFunction(()=>!$('pause').classList.contains('pause-sidebars-pending'));await page.locator('#resume').click();
      check(await page.evaluate(async()=>{
        mapViews.setOpen(true);const before=[...mapViews.options.children],image=mapViews.main.querySelector('img');mapViews.close();
        await new Promise(r=>setTimeout(r,300));mapViews.setOpen(true);
        const retained=before.every((el,i)=>el===mapViews.options.children[i])&&image===mapViews.main.querySelector('img');mapViews.close();return retained;
      }),'map dropdown retains nodes on reopen '+renderer);
      await page.locator('#map-mode').tap();await page.waitForTimeout(280);
      await page.locator('#map-mode-options button').first().tap();await page.waitForTimeout(300);
      check(await page.evaluate(()=>!mapViews.open&&!mapViews.main.matches(':focus-visible')),'touch map option closes without focus border '+renderer);
      await page.locator('#map-mode').focus();await page.keyboard.press('ArrowDown');await page.waitForTimeout(280);await page.keyboard.press('Enter');
      check(await page.evaluate(()=>document.activeElement===mapViews.main),'keyboard map selection returns focus '+renderer);
      await page.waitForTimeout(300);
      let releasePortrait,portraitRequested;const requested=new Promise(r=>portraitRequested=r),gate=new Promise(r=>releasePortrait=r);
      await page.route('**/ui/commanders2_hd/**',async route=>{portraitRequested();await gate;await route.continue();});
      await page.evaluate(()=>{
        const portrait=Object.keys(manifest.atlases.commanders2_hd)[0];
        const original=menus.portrait;menus.portrait=function(name){const img=original.call(this,name);if(name===portrait)img.src+='?ui-feedback-cold=1';return img;};
        state={...state,dialogue:'将军，请下达命令。',dialoguePresentation:{name:'拿破仑',portrait,left:true}};refreshHud();
      });
      try{
        await Promise.race([requested,new Promise((_,reject)=>{const timer=setTimeout(()=>reject(Error('Cold portrait request did not arrive')),10000);timer.unref();})]);
        check(await page.locator('#dialogue').isHidden(),'cold portrait keeps whole dialogue hidden '+renderer);
      }finally{releasePortrait();}
      await page.waitForSelector('#dialogue:not([hidden])');
      check(await page.evaluate(()=>!$('dialogue').hidden&&$('dialogue-portrait').complete&&$('dialogue-portrait').naturalWidth>0&&!!$('dialogue-text').firstElementChild),'portrait/text appear together '+renderer);
      check(await page.evaluate(()=>{
        const image=$('dialogue-portrait'),text=$('dialogue-text').firstElementChild;for(let i=0;i<8;i++)refreshHud();
        return image===$('dialogue-portrait')&&text===$('dialogue-text').firstElementChild;
      }),'unchanged dialogue keeps decoded image/text '+renderer);
      await page.screenshot({path:path.join(folder,renderer+'-dialogue.png')});
      check(await page.evaluate(async()=>{
        const slowText='旧对白';let release;const prepare=Ew4OriginalUI.prepareText;
        Ew4OriginalUI.prepareText=text=>text.includes(slowText)?new Promise(r=>release=r):prepare(text);
        state={...state,dialogue:slowText};refreshHud();state={...state,dialogue:'新对白'};refreshHud();
        await dialoguePresentationTask;release();await Promise.resolve();Ew4OriginalUI.prepareText=prepare;
        return $('dialogue-text').firstElementChild.getAttribute('aria-label')==='新对白';
      }),'late dialogue preparation cannot restore obsolete text '+renderer);
      await page.evaluate(async()=>{await showMain();await menus.headquarters();});
      await page.evaluate(()=>{menus.recruiting=true;menus.rating=0;menus.drawRoster();});
      const refresh=await page.evaluate(async()=>{
        const ids=['hq-roster','hq-recruit-band','hq-refresh','hq-filters'],before=new Map(ids.map(id=>[id,document.getElementById(id)]));
        const title=document.querySelector('#hq-screen h1').firstElementChild,tabs=[...$('hq-filters').children],scroll=$('hq-roster').scrollLeft;
        const old=menus.data.refresh[0].remaining;await menus.transact({op:'refresh',group:0});
        const remaining=menus.data.refresh[0].remaining,countUpdated=remaining==null?$('hq-refresh').title==='刷新将领'&&$('hq-refresh').querySelector('.hq-refresh-count').hidden&&!$('hq-refresh').querySelector('.hq-refresh-count').childElementCount:
          remaining===old-1&&$('hq-refresh').title.includes(String(remaining));
        return {fixed:ids.every(id=>before.get(id)===$(id))&&title===document.querySelector('#hq-screen h1').firstElementChild&&tabs.every((el,i)=>el===$('hq-filters').children[i]),
          countUpdated,cards:$('hq-roster').children.length,scrollKept:scroll===$('hq-roster').scrollLeft};
      });check(refresh.fixed&&refresh.countUpdated&&refresh.cards===6&&refresh.scrollKept,'recruit refresh updates only prepared cards/count '+renderer+' '+JSON.stringify(refresh));
      check(await page.evaluate(async()=>{
        const original=menus.api.rpc;let calls=0;
        menus.api.rpc=async function(route,args){if(route==='commander'&&args.op==='refresh')calls++;return original(route,args);};
        try{$('hq-refresh').click();$('hq-refresh').click();while(menus.refreshPending)await new Promise(r=>setTimeout(r,10));return calls===1;}
        finally{menus.api.rpc=original;}
      }),'rapid refresh clicks submit exactly one transaction '+renderer);
      check(await page.evaluate(()=>{
        const source=menus.data.refresh[menus.rating],old=source.remaining;source.remaining=2;menus.updateRefreshCount();
        const displayed=$('hq-refresh').title==='剩余 2 次'&&!$('hq-refresh').querySelector('.hq-refresh-count').hidden&&$('hq-refresh').querySelector('.hq-refresh-count .original-text').getAttribute('aria-label')==='2';
        source.remaining=old;menus.updateRefreshCount();return displayed;
      }),'finite refresh count updates without replacing frame '+renderer);
      const card=page.locator('#hq-roster .commander-open').first();await card.tap();
      check(await page.locator('#hq-buy-buttons button').count()>0,'refreshed cards still select recruitment offer '+renderer);
      await page.screenshot({path:path.join(folder,renderer+'-recruit.png')});
      await page.evaluate(()=>showMain());await page.locator('#main-settings').tap();await page.locator('#music-volume').fill('50');
      await page.waitForFunction(()=>!music.audio.paused&&music.audio.currentTime>.05,null,{timeout:20000});
      check(await page.evaluate(()=>music.audio.volume===.5),'music plays on user volume change '+renderer);
      await page.evaluate(()=>ew4SetAppVisible(false));check(await page.evaluate(()=>music.audio.paused),'music pauses in background '+renderer);
      await page.evaluate(()=>ew4SetAppVisible(true));await page.waitForFunction(()=>!music.audio.paused);check(true,'music resumes in foreground '+renderer);
      await page.close();
    }
    check(report.errors.length===0&&report.missing.length===0,'no script exceptions or failed resources');
  }finally{await browser.close();await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2)+'\n');}
  console.log(JSON.stringify({checks:report.checks,views:report.views.length,errors:report.errors.length,missing:report.missing.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
