'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const url=process.argv[2]||'http://127.0.0.1:5578',folder=path.resolve(process.argv[3]||'build/campaign-technology-20261026/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,errors:[],missing:[],views:[]},check=(v,label)=>{assert.ok(v,label);report.checks++;};
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720}});
    page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
    await page.goto(url);await page.waitForFunction(()=>startupReady,null,{timeout:120000});
    await page.locator('.main-entry[data-mode="1"]').click();
    for(const viewport of [{width:2376,height:1080},{width:1280,height:720},{width:872,height:393},{width:393,height:872},{width:320,height:568}]){
      await page.setViewportSize(viewport);
      for(let chapter=0;chapter<6;chapter++){
        await page.evaluate(i=>menus.openChapter(catalog.chapters[i]),chapter);
        await page.locator('#campaign-upgrade').click();
        const body=page.locator('.campaign-technology');await body.waitFor();
        check(await page.locator('#campaign-upgrade').evaluate(b=>getComputedStyle(b).backgroundImage.includes('/button_upgrade.png')),'original gear/lightning command art');
        check(await body.getAttribute('data-warzone')===String(chapter),'correct warzone');
        check(await body.locator('.technology-tab').count()===5,'original five categories');
        for(let group=0;group<5;group++){
          await body.locator('.technology-tab').nth(group).click();
          const entries=group===0?6:4;
          check(await body.locator('button.technology-cell').count()===entries*4,'complete technology grid');
          check(await body.locator('.technology-tab[aria-selected="true"]').getAttribute('data-group')===String(group),'category selection');
          const slots=await body.locator('button.technology-cell[data-level="0"]').evaluateAll(cells=>cells.map(cell=>cell.dataset.slot));
          for(const slot of slots){
            await body.locator('button.technology-cell[data-slot="'+slot+'"]').first().click();
            check(await body.locator('[data-native-id=image_unit] img').evaluate(async img=>{await img.decode();return img.naturalWidth>0;}),'every technology preview loads');
          }
        }
        await body.locator('.technology-tab').first().click();
        await body.locator('button.technology-cell[data-slot="1"]').first().click();
        const geometry=await body.evaluate(async host=>{
          await Promise.all([...host.querySelectorAll('img')].map(img=>img.decode().catch(()=>{})));
          const panel=host.parentElement.getBoundingClientRect(),buttons=[...host.querySelectorAll('button')].map(b=>b.getBoundingClientRect());
          return {panel:{left:panel.left,top:panel.top,right:panel.right,bottom:panel.bottom},inside:buttons.every(b=>b.left>=panel.left-1&&b.right<=panel.right+1&&b.top>=panel.top-1&&b.bottom<=panel.bottom+1),missing:[...host.querySelectorAll('img')].filter(img=>!img.naturalWidth).map(img=>img.src)};
        });
        check(geometry.inside,'controls stay inside original dialog');check(!geometry.missing.length,'all bitmap assets render');
        check(geometry.panel.left>=-1&&geometry.panel.top>=-1&&geometry.panel.right<=viewport.width+1&&geometry.panel.bottom<=viewport.height+1,'dialog fits viewport');
        check(await body.locator('.technology-cell>img:first-child').first().evaluate(img=>{const r=img.getBoundingClientRect();return Math.abs(r.width/r.height-img.naturalWidth/img.naturalHeight)<.001;}),'badge retains original aspect');
        check(await body.locator('[data-native-id=image_verbar]').evaluate(el=>{const s=getComputedStyle(el);return !el.children.length&&s.borderImageSlice==='27 1 7 fill'&&parseFloat(s.borderBottomWidth)<=3.5;}),'selected column keeps thin orange edge');
        if(chapter===0||chapter===5){
          const shot=await page.screenshot({path:path.join(folder,viewport.width+'x'+viewport.height+'-chapter'+chapter+'.png')});
          const png=PNG.sync.read(shot),colors=new Set();
          for(let y=Math.ceil(geometry.panel.top);y<Math.floor(geometry.panel.bottom);y+=3)for(let x=Math.ceil(geometry.panel.left);x<Math.floor(geometry.panel.right);x+=3){const i=(y*png.width+x)*4;colors.add(png.data.readUInt32BE(i));}
          check(colors.size>80,'nonblank original art');
        }
        report.views.push({viewport,chapter});await page.evaluate(()=>closeDetail());
      }
    }
    await page.setViewportSize({width:1280,height:720});await page.evaluate(()=>menus.openChapter(catalog.chapters[0]));await page.locator('#campaign-upgrade').click();
    const body=page.locator('.campaign-technology');await body.locator('button.technology-cell[data-slot="1"]').first().click();
    const before=await page.evaluate(()=>rpc('technology',{group:0})),entry=before.entries[1];
    check(entry.canUpgrade,'isolated test account can pay');
    await body.locator('[data-technology-upgrade]').click();
    await page.waitForFunction(stars=>document.querySelector('.campaign-technology').dataset.stars===String(stars),before.stars-entry.cost);
    const after=await page.evaluate(()=>rpc('technology',{group:0}));check(after.entries[1].level===entry.level+1&&after.stars===before.stars-entry.cost,'real UI upgrade pays once');
    await page.reload();await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    check((await page.evaluate(()=>rpc('technology',{group:0}))).stars===after.stars,'reload retains payment');
    check(report.errors.length===0,'no browser script errors');check(report.missing.length===0,'no HTTP failures');
    await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify({checks:report.checks,views:report.views.length,errors:report.errors,missing:report.missing}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
