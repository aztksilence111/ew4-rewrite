'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]);
const native=process.argv[4]==='native';
const report={checks:0,errors:[],missing:[],views:[],animations:[],commands:0};
const check=(value,label)=>{assert.ok(value,label);report.checks++;};
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720},hasTouch:true});
    page.on('pageerror',e=>report.errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400)report.missing.push(r.status()+' '+r.url());});
    page.on('request',r=>{if(r.url().includes('/api/commander?'))report.commands++;});
    if(native)await page.route('https://ew4.local/**',async route=>{
      const url=new URL(route.request().url()),headers={...route.request().headers()};if(headers.origin)headers.origin=new URL(base).origin;
      const asset=/^\/(?:vendor\/)?[^/]+\.(?:js|css)$/.test(url.pathname)?'/compat'+url.pathname:url.pathname;
      const response=await route.fetch({url:new URL(asset+url.search,base).href,headers});await route.fulfill({response});
    });
    await page.goto(native?'https://ew4.local/index.html':base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await page.locator('#headquarters').click();await page.waitForFunction(()=>menus.data?.roster);
    await page.locator('#hq-recruit').click();
    await page.evaluate(async()=>{
      for(let group=0;group<3;group++)if(menus.data.offers[group].length<[6,4,2][group])await menus.updateAccount({op:'refresh',group});
    });
    const ready=()=>page.waitForFunction(()=>[...document.querySelectorAll('#hq-screen img')].every(i=>i.complete&&i.naturalWidth));
    for(const viewport of [{width:2376,height:1080},{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(viewport);
      for(let group=0;group<3;group++){
        await page.locator('#hq-filters [role=tab]').nth(group).click();await ready();
        const state=await page.evaluate(()=>{
          const roster=$('hq-roster'),r=roster.getBoundingClientRect(),cards=[...roster.children],ornaments=[...document.querySelectorAll('#hq-candidate-ornaments img')];
          return {count:cards.length,focus:cards.filter(c=>c.dataset.defaultFocus==='true').length,purchase:!!$('hq-buy-buttons'),
            queued:!!roster.querySelector('.academy-queued,.academy-revealing'),center:Math.abs((r.left+r.right)/2-innerWidth/2),
            cardWidths:cards.map(c=>c.getBoundingClientRect().width),ornaments:ornaments.map(i=>{const r=i.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,width:r.width,height:r.height};})};
        });
        check(state.count===[6,4,2][group],'fixed slot count '+viewport.width+'/'+group);
        check(state.focus===1&&!state.purchase&&!state.queued,'switch shows cached offers and default focus without prices');
        check(state.center<1,'candidate group is centered');
        check(state.ornaments.length===[0,2,4][group],'correct candidate ornaments');
        check(state.ornaments.every(r=>r.left>=0&&r.right<=viewport.width&&r.top>=0&&r.bottom<=viewport.height),'ornaments fit viewport');
        for(const index of [0,state.count-1]){
          await page.locator('#hq-roster .commander-open').nth(index).click();
          await page.locator('#hq-buy-buttons').evaluate(el=>Promise.all(el.getAnimations().map(a=>a.finished.catch(()=>{}))));
          const anchor=await page.evaluate(index=>{
            const screen=$('hq-screen'),s=screen.getBoundingClientRect(),scale=s.width/screen.offsetWidth;
            const c=$('hq-roster').children[index].getBoundingClientRect(),row=$('hq-buy-buttons'),r=row.getBoundingClientRect();
            const expected=Math.max(s.left+4*scale,Math.min((c.left+c.right-r.width)/2,s.right-r.width-4*scale));
            return {error:Math.abs(r.left-expected),gap:(r.top-c.bottom)/scale,inside:r.left>=s.left&&r.right<=s.right,
              buttons:[...row.children].map(b=>{const a=b.getBoundingClientRect();return {w:a.width,h:a.height,label:b.getAttribute('aria-label')};}),
              focused:document.querySelectorAll('#hq-roster [data-default-focus=true]').length};
          },index);
          check(anchor.error<1&&Math.abs(anchor.gap-2)<.2&&anchor.inside,'prices follow selected card '+viewport.width+'/'+group+'/'+index+' '+JSON.stringify(anchor));
          check(anchor.focused===0,'explicit selection replaces default focus');
        }
        await page.screenshot({path:path.join(out,'academy-'+viewport.width+'-'+group+'.png')});
        report.views.push({viewport,group,state});
      }
    }
    for(const viewport of [{width:844,height:390},{width:2376,height:1080}])for(const preference of [80,150]){
      await page.setViewportSize(viewport);
      await page.evaluate(preference=>{settings.screen=preference;sizeUi();},preference);
      await page.locator('#hq-filters [role=tab]').nth(1).click();
      await page.locator('#hq-roster .commander-open').first().click();
      await page.locator('#hq-buy-buttons').evaluate(el=>Promise.all(el.getAnimations().map(a=>a.finished.catch(()=>{}))));
      const fits=await page.evaluate(()=>{
        const c=$('hq-roster').firstElementChild.getBoundingClientRect(),p=$('hq-buy-buttons').getBoundingClientRect();
        return p.left>=0&&p.right<=innerWidth&&p.top>=c.bottom&&p.bottom<=$('hq-recruit-band').getBoundingClientRect().bottom+1&&Math.abs((p.left+p.right-c.left-c.right)/2)<1;
      });
      check(fits,'prices remain attached after UI scale '+preference+' '+viewport.width);
    }
    await page.evaluate(()=>{settings.screen=100;sizeUi();});
    await page.setViewportSize({width:1280,height:720});
    for(let group=0;group<3;group++){
      await page.locator('#hq-filters [role=tab]').nth(group).click();
      const before=report.commands;
      await page.evaluate(()=>{
        window.academyFrame={heading:$('hq-screen').querySelector('h1'),filters:$('hq-filters'),refresh:$('hq-refresh'),band:$('hq-recruit-band')};
        window.academySamples=[];window.academySeen=[];
        window.academySampler=setInterval(()=>{
          const cards=[...$('hq-roster').children],active=cards.findIndex(c=>c.classList.contains('academy-revealing'));
          if(active>=0&&!academySeen.includes(active))academySeen.push(active);
          academySamples.push({time:performance.now(),queued:cards.filter(c=>c.classList.contains('academy-queued')).length,
            active,scales:cards.map(c=>c.firstElementChild.getBoundingClientRect().width/c.getBoundingClientRect().width),
            rects:cards.map(c=>c.getBoundingClientRect().toJSON()),busy:$('hq-refresh').disabled,
            dark:getComputedStyle($('hq-refresh')).filter});
        },20);
        window.academyTask=menus.transact({op:'refresh',group:menus.rating});
        $('hq-refresh').click();$('hq-refresh').click();
      });
      await page.waitForFunction(()=>!!document.querySelector('.academy-revealing'));
      await page.screenshot({path:path.join(out,'reveal-'+group+'.png')});
      const result=await page.evaluate(async()=>{
        await academyTask;clearInterval(academySampler);
        const last=academySamples[academySamples.length-1],first=academySamples.find(s=>s.queued||s.active>=0);
        return {samples:academySamples,order:academySeen,duration:last.time-first.time,
          stable:academyFrame.heading===$('hq-screen').querySelector('h1')&&academyFrame.filters===$('hq-filters')&&academyFrame.refresh===$('hq-refresh')&&academyFrame.band===$('hq-recruit-band'),
          finished:!menus.refreshPending&&!$('hq-refresh').disabled&&!document.querySelector('.academy-queued,.academy-revealing'),
          focus:document.querySelectorAll('#hq-roster [data-default-focus=true]').length,purchase:!!$('hq-buy-buttons')};
      });
      const samples=result.samples.filter(s=>s.queued||s.active>=0),slots=[6,4,2][group];
      check(report.commands-before===1,'repeat refresh submits once');
      check(JSON.stringify(result.order)===JSON.stringify(Array.from({length:slots},(_,i)=>i)),'portraits reveal from left to right');
      check(samples.some(s=>s.queued===slots),'all slots show placeholders before revealing');
      check(samples.every(s=>s.busy&&s.dark.includes('0.65')),'button stays dark and disabled throughout reveal');
      check(samples.some(s=>s.scales.some(v=>v>1.05)),'portrait grows outside fixed slot');
      check(samples.every(s=>s.rects.every((r,i)=>Math.abs(r.left-samples[0].rects[i].left)<.1&&Math.abs(r.width-samples[0].rects[i].width)<.1)),'slots stay still');
      check(result.stable&&result.finished&&result.focus===1&&!result.purchase,'frame stays mounted and settles without prices');
      check(result.duration>slots*110&&result.duration<slots*180+150,'reveal duration follows slot count');
      report.animations.push({group,order:result.order,duration:result.duration});
    }
    // Hold an actual response so navigation races are repeatable without editing account files.
    await page.evaluate(()=>{
      window.academyRpc=menus.api.rpc;
      menus.api.rpc=async(...args)=>{const result=await academyRpc(...args);if(args[0]==='commander')await new Promise(resolve=>{window.academyRelease=resolve;});return result;};
      window.academyTask=menus.transact({op:'refresh',group:menus.rating});
    });
    await page.waitForFunction(()=>!!window.academyRelease);
    await page.locator('#hq-filters [role=tab]').nth(0).click();
    check(await page.locator('#hq-refresh').isDisabled(),'switch cannot submit another transaction while response is pending');
    await page.evaluate(async()=>{academyRelease();await academyTask;menus.api.rpc=academyRpc;});
    check(await page.evaluate(()=>menus.rating===0&&!menus.refreshPending&&!document.querySelector('.academy-revealing,.academy-queued')&&$('hq-roster').children.length===6),'late response keeps current group');
    await page.evaluate(()=>{window.academyTask=menus.transact({op:'refresh',group:0});});
    await page.waitForFunction(()=>!!document.querySelector('.academy-revealing'));
    await page.locator('#hq-filters [role=tab]').nth(1).click();
    await page.evaluate(()=>academyTask);
    check(await page.evaluate(()=>menus.rating===1&&!menus.refreshPending&&!document.querySelector('.academy-revealing,.academy-queued')&&$('hq-roster').children.length===4),'switch cancels reveal');
    await page.evaluate(()=>{window.academyTask=menus.transact({op:'refresh',group:1});});
    await page.waitForFunction(()=>!!document.querySelector('.academy-revealing'));
    await page.locator('#hq-back').click();await page.evaluate(()=>academyTask);
    check(await page.evaluate(()=>!menus.recruiting&&!menus.refreshPending&&!document.querySelector('.academy-revealing,.academy-queued')),'back cancels reveal');
    await page.locator('#hq-recruit').click();
    const failed=await page.evaluate(async()=>{
      const original=menus.api.rpc,ids=[...$('hq-roster').children].map(c=>c.dataset.offer),revision=menus.data.revision;
      menus.api.rpc=async()=>{throw new Error('academy test failure');};
      try{await menus.transact({op:'refresh',group:0});}catch(error){}finally{menus.api.rpc=original;}
      return {unchanged:revision===menus.data.revision&&JSON.stringify(ids)===JSON.stringify([...$('hq-roster').children].map(c=>c.dataset.offer)),released:!menus.refreshPending&&!$('hq-refresh').disabled};
    });
    check(failed.unchanged&&failed.released,'failed refresh keeps offers and unlocks');
    const partial=await page.evaluate(async()=>{
      const original=menus.api.rpc;
      menus.api.rpc=async()=>({...menus.data,offers:{...menus.data.offers,0:menus.data.offers[0].slice(0,1)}});
      try{await menus.transact({op:'refresh',group:0});}finally{menus.api.rpc=original;}
      return {count:$('hq-roster').children.length,focus:document.querySelectorAll('#hq-roster [data-default-focus=true]').length,empty:document.querySelectorAll('#hq-roster .empty-commander').length,busy:menus.refreshPending};
    });
    check(partial.count===6&&partial.empty===5&&partial.focus===1&&!partial.busy,'partly exhausted pool reveals available card and keeps empty slots');
    const empty=await page.evaluate(async()=>{
      const original=menus.api.rpc;
      menus.api.rpc=async()=>({...menus.data,offers:{...menus.data.offers,0:[]}});
      try{await menus.transact({op:'refresh',group:0});}finally{menus.api.rpc=original;}
      return {count:$('hq-roster').children.length,focus:document.querySelectorAll('#hq-roster [data-default-focus=true]').length,empty:document.querySelectorAll('#hq-roster .empty-commander').length,busy:menus.refreshPending};
    });
    check(empty.count===6&&empty.empty===6&&!empty.focus&&!empty.busy,'exhausted pool retains fixed empty slots');
    await page.evaluate(async()=>{menus.data=await menus.api.rpc('headquarters');menus.drawRoster();});
    await page.locator('#hq-roster .commander-open').first().click();
    await page.evaluate(()=>{window.academyPurchaseBalance=[menus.data.medals,menus.data.badges];menus.api.rpc=async()=>{throw new Error('academy rejected purchase');};});
    await page.locator('#hq-buy-buttons button').first().click();
    await page.waitForFunction(()=>!menus.recruitPending);
    check(await page.evaluate(()=>!!$('hq-buy-buttons')&&!$('hq-refresh').disabled&&JSON.stringify(academyPurchaseBalance)===JSON.stringify([menus.data.medals,menus.data.badges])),'rejected purchase keeps prices and balances');
    await page.evaluate(()=>{menus.api.rpc=academyRpc;});
    for(const [group,currency,priceKey] of [[0,'medals','price'],[2,'badges','badgePrice']]){
      await page.locator('#hq-filters [role=tab]').nth(group).click();
      const transaction=await page.evaluate(({currency,priceKey})=>{
        const id=menus.data.offers[menus.rating].find(id=>menus.data.definitions.find(d=>d.id===id)[priceKey]>0);
        const definition=menus.data.definitions.find(d=>d.id===id);menus.selectRecruitmentOffer(definition);
        return {id,currency,price:definition[priceKey],balance:menus.data[currency],count:menus.data.roster.length};
      },{currency,priceKey});
      const before=report.commands;
      await page.locator('#hq-buy-buttons button').filter({has:page.locator('img[src$="/'+currency+'.png"]')}).evaluate(el=>{el.click();el.click();});
      await page.waitForFunction(count=>!menus.recruitPending&&menus.data.roster.length===count+1,transaction.count);
      const authoritative=await page.evaluate(()=>menus.api.rpc('headquarters'));
      check(report.commands-before===1,'repeat purchase submits once '+currency);
      check(authoritative[currency]===transaction.balance-transaction.price&&authoritative.roster.length===transaction.count+1&&authoritative.roster.some(c=>c.definitionId===transaction.id),'purchase debits once and adds commander '+currency);
      check(!authoritative.offers[group].includes(transaction.id)&&!await page.locator('#hq-buy-buttons').count(),'purchased offer and prices disappear');
      check(await page.locator('#hq-roster [data-default-focus=true]').count()===1,'purchase restores default focus');
    }
    await page.emulateMedia({reducedMotion:'reduce'});
    await page.evaluate(()=>menus.transact({op:'refresh',group:menus.rating}));
    check(await page.evaluate(()=>!menus.refreshPending&&!document.querySelector('.academy-revealing,.academy-queued')),'reduced motion finishes immediately');
    check(!report.errors.length&&!report.missing.length,'no script errors or missing assets');
  }finally{
    await browser.close();await fs.writeFile(path.join(out,'verification.json'),JSON.stringify(report,null,2));
  }
  console.log(JSON.stringify({checks:report.checks,views:report.views.length,animations:report.animations,errors:report.errors.length,missing:report.missing.length}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
