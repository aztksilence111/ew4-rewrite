'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56154',out=path.resolve(process.argv[3]||'build/teaching-preview-check-20261030/browser');
const abilities=['infantry','cavalry','artillery','naval','fort','commerce','movement'];
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],warnings:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try{
    for(const compat of [false,true]){
      const context=await browser.newContext(),page=await context.newPage();
      if(!compat)await page.route(/\/compat\/[^/]+\.js(?:\?|$)/,route=>route.fulfill({path:path.resolve('rewrite/Ew4.Web/wwwroot',new URL(route.request().url()).pathname.split('/').pop())}));
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      page.on('console',m=>{if(m.type()==='warning'&&m.text().includes('Missing original UI image'))report.warnings.push(m.text());});
      await page.goto(base);await page.waitForFunction(()=>typeof menus!=='undefined'&&menus&&document.documentElement.dataset.fontsReady==='true');
      await page.locator('#headquarters').click();await page.waitForFunction(()=>menus.data?.progression);
      await page.evaluate(async()=>{
        for(const p of menus.data.princesses.slice(0,2))if(!p.claimed)await menus.updateAccount({op:'claim',offer:p.id});
        window.teachingFixture={receiver:menus.data.roster[0].id,donor:menus.data.roster[1].id,rpc:menus.api.rpc};
        // Exercise the renderer with backend-shaped previews; real transactions use the unmodified RPC below.
        menus.api.rpc=async(kind,args)=>kind==='commander'&&args.op==='preview_inherit'&&teachingFixture.preview?teachingFixture.preview:teachingFixture.rpc(kind,args);
      });
      const preview=async values=>page.evaluate(async values=>{
        const f=teachingFixture,r=menus.held(f.receiver);
        r.learning.abilities=Object.fromEntries(Object.keys(r.learning.abilities).map(k=>[k,4]));
        f.preview={price:0,learning:{...r.learning,abilities:{...r.learning.abilities,...values}}};
        await menus.regroup(f.receiver,f.donor);
      },values);
      const rows=()=>page.locator('.regroup-ability').evaluateAll(nodes=>nodes.map(n=>{
        const r=n.getBoundingClientRect(),icon=n.querySelector('img'),ir=icon.getBoundingClientRect();
        return {key:n.dataset.ability,before:+n.dataset.before,value:+n.dataset.value,label:n.getAttribute('aria-label'),
          stars:n.querySelectorAll('.regroup-stars img').length,count:n.querySelector('.regroup-stars .original-text')?.getAttribute('aria-label'),
          icon:icon.complete&&icon.naturalWidth>0&&ir.width>0&&ir.height>0,
          fits:[...n.children,...n.querySelectorAll('.regroup-stars>*')].every(c=>{const b=c.getBoundingClientRect();return b.left>=r.left-.5&&b.right<=r.right+.5&&b.top>=r.top-.5&&b.bottom<=r.bottom+.5;})};
      }));
      for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
        await page.setViewportSize(viewport);
        for(const key of abilities){
          await preview({[key]:5});await page.locator('.regroup-ability>img').evaluate(img=>img.decode());
          const data=await rows();check(data.length===1&&data[0].key===key&&data[0].before===4&&data[0].value===5,'only changed ability '+key);
          check(data[0].icon&&data[0].fits,'visible icon and contained content '+key+' '+viewport.width);
          check(data[0].stars===5&&data[0].label.includes('4星 → 5星'),'five original stars and explicit transition '+key);
        }
        await preview({commerce:17});
        await page.locator('.regroup-ability>img').evaluate(img=>img.decode());
        const high=(await rows())[0];check(high.stars===1&&high.count==='×17'&&high.fits,'above five retains actual count');
        await page.screenshot({path:path.join(out,(compat?'compat':'source')+'-'+viewport.width+'.png')});
      }
      for(const value of [6,100,2147483647]){
        await preview({movement:value});check((await rows())[0].count==='×'+value&&(await rows())[0].fits,'large count '+value);
      }
      await preview(Object.fromEntries(abilities.map(k=>[k,5])));
      check((await rows()).length===7,'all seven teaching abilities remain available');
      check(await page.locator('[data-native-id=grid_preview]').evaluate(n=>{n.scrollTop=n.scrollHeight;const r=n.getBoundingClientRect(),last=n.lastElementChild.getBoundingClientRect();return n.scrollHeight>n.clientHeight&&last.bottom<=r.bottom+1;}),'multiple changes scroll within preview');
      await preview({});check((await rows()).length===0,'no unchanged stars presented as gains');
      await page.evaluate(async()=>{const f=teachingFixture;menus.api.rpc=f.rpc;menus.data=await menus.api.rpc('headquarters',{});});
      await page.evaluate(async()=>{const f=teachingFixture;f.actual=await menus.api.rpc('commander',{op:'preview_inherit',revision:menus.data.revision,commander:f.receiver,target:f.donor});await menus.regroup(f.receiver,f.donor);});
      const expected=await page.evaluate(()=>Object.entries(teachingFixture.actual.learning.abilities).filter(([k,v])=>v>(menus.held(teachingFixture.receiver).learning.abilities[k]??0)).map(([k,v])=>[k,v]));
      assert.deepEqual((await rows()).map(r=>[r.key,r.value]).sort(),expected.sort());report.checks++;
      await page.evaluate(async()=>{
        const f=teachingFixture,receiver=menus.held(f.receiver),keys=['infantry','cavalry','artillery','naval','fort','commerce','movement'];
        const offered=Object.entries(menus.data.offers).flatMap(([group,ids])=>ids.map(id=>({group:+group,definition:menus.data.definitions.find(d=>d.id===id)})));
        const teacher=offered.find(o=>o.definition.skills.some(s=>{const index=Number(s.id.split('.').pop())-33;return index>=0&&index<7&&(receiver.learning.abilities[keys[index]]??0)<5;}));
        if(!teacher)throw Error('Isolated profile needs an available teaching commander');
        await menus.updateAccount({op:'recruit',group:teacher.group,commander:teacher.definition.id});
        f.donor=menus.data.roster.find(c=>c.definitionId===teacher.definition.id).id;
        f.actual=await menus.api.rpc('commander',{op:'preview_inherit',revision:menus.data.revision,commander:f.receiver,target:f.donor});
        await menus.regroup(f.receiver,f.donor);
      });
      check((await rows()).length>0,'real teaching donor produces visible gain');
      const actualRows=await rows();
      await page.setViewportSize({width:1280,height:720});
      await page.screenshot({path:path.join(out,(compat?'compat':'source')+'-actual.png')});
      await page.locator('[data-native-id=btn_regroup]').click();await page.locator('#detail-confirm').click();
      await page.waitForFunction(()=>!!document.querySelector('.commander-detail'));
      check(await page.evaluate(()=>{const f=teachingFixture;return JSON.stringify(menus.held(f.receiver).learning)===JSON.stringify(f.actual.learning)&&!menus.held(f.donor);}), 'committed teaching equals actual preview and removes donor');
      await page.reload();await page.waitForFunction(()=>typeof menus!=='undefined'&&menus&&document.documentElement.dataset.fontsReady==='true');
      await page.locator('#headquarters').click();await page.waitForFunction(()=>menus.data?.progression);
      check(await page.evaluate(rows=>rows.every(r=>menus.data.roster[0].learning.abilities[r.key]===r.value),actualRows),'teaching persists across reload');
      await context.close();
    }
    check(report.errors.length===0,'no browser exceptions');check(report.missing.length===0,'no missing resources');check(report.warnings.length===0,'no unknown ability images');
  }finally{await browser.close();await fs.writeFile(path.join(out,'results.json'),JSON.stringify(report,null,2));}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
