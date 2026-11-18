'use strict';
// Isolated QA server only: imports a fixture Mod and claims an actual commander reward.
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {chromium}=require('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const base=process.argv[2],fixture=process.argv[3],out=path.resolve(process.argv[4]);
(async()=>{
  let checks=0;const errors=[],missing=[],check=(ok,label)=>{assert(ok,label);checks++;};await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720}});page.on('pageerror',e=>errors.push(e.message));page.on('response',r=>{if(r.status()>=400)missing.push(r.url());});
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    const imported=await page.request.post(base+'/api/mod-import',{multipart:{file:{name:'skin-demo.ew4mod',mimeType:'application/zip',buffer:await fs.readFile(fixture)},id:'portrait-demo',name:'Portrait demo'}});check(imported.ok(),'fixture import accepted');
    let key;const deadline=Date.now()+180000;
    while(Date.now()<deadline){const s=await (await page.request.get(base+'/api/mods')).json();if(s.import.status==='failed')throw Error(s.import.message);if(s.import.status==='complete'){key=s.mods.find(m=>m.id==='portrait-demo')?.key;if(key)break;}await new Promise(resolve=>setTimeout(resolve,1000));}
    check(typeof key==='string','import completed with Mod key');
    await page.evaluate(key=>fetch('/api/mods',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:'activate',key})}),key);
    await page.reload();await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    const id=await page.evaluate(()=>ew4Portraits.entries.find(e=>e.skins.length).id);await page.evaluate(id=>ew4Portraits.open(id),id);
    check(await page.getByRole('button',{name:'纪念服',exact:false}).isDisabled(),'future skin locked');
    const initiallyUnlocked=await page.evaluate(id=>ew4Portraits.entries.find(e=>e.id===id).skins.find(s=>s.id==='demo.blue').unlocked,id);
    check(await page.getByRole('button',{name:'远征服',exact:false}).isDisabled()===!initiallyUnlocked,'task skin button follows real completion state');
    check(await page.locator('#portrait-choices').textContent().then(s=>s.includes('未来成就尚未完成')),'unlock requirements shown');
    await page.locator('#portrait-gallery-close').click();
    await page.evaluate(async()=>{await menus.headquarters();const p=menus.data.princesses.find(p=>!p.claimed);if(!p)throw Error('No reward available');await menus.updateAccount({op:'claim',offer:p.id});await ew4Portraits.refresh();});
    check(await page.evaluate(id=>ew4Portraits.entries.find(e=>e.id===id).skins.find(s=>s.id==='demo.blue').unlocked,id),'real reward acquisition completes task');
    check(await page.evaluate(id=>ew4Portraits.entries.find(e=>e.id===id).portrait==='rewrite/skins/demo.png',id),'unlocked automatic skin applied');
    await page.evaluate(id=>{const d=menus.data.definitions.find(d=>d.id===id);menus.commanderInfo(d,menus.data.roster.find(r=>r.definitionId===id));},id);
    const button=page.locator('.commander-skin-button');check(await button.isVisible(),'headquarters detail has skin button');
    for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(viewport);await page.waitForFunction(()=>{const n=document.querySelector('.commander-skin-button'),r=n.getBoundingClientRect(),p=n.closest('.dialog').getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight&&r.bottom<=p.bottom;});check(true,'detail skin button fits dialog and viewport');
      await page.screenshot({path:path.join(out,'detail-'+viewport.width+'.png')});
    }
    await button.click();await page.locator('#portrait-gallery').waitFor({state:'visible'});check(true,'detail button opens skin picker');
    await page.getByRole('button',{name:'原始头像',exact:true}).click();await page.waitForFunction(()=>!ew4Portraits.working);
    check(await page.evaluate(id=>!ew4Portraits.entries.find(e=>e.id===id).portrait,id),'manual original overrides automatic');
    await page.reload();await page.waitForFunction(()=>startupReady);check(await page.evaluate(id=>!ew4Portraits.entries.find(e=>e.id===id).portrait,id),'manual choice persists');
    await page.evaluate(id=>ew4Portraits.open(id),id);await page.getByRole('button',{name:'跟随剧情自动切换',exact:false}).click();await page.waitForFunction(()=>!ew4Portraits.working);
    const imageName=await page.evaluate(id=>ew4Portraits.entries.find(e=>e.id===id).image,id);
    const marker=await page.evaluate(async name=>{for(let i=0;i<100;i++){const m=ew4Portraits.marker(name);if(m){const c=document.createElement('canvas');c.width=c.height=1;c.getContext('2d').drawImage(m,0,0,1,1);return [...c.getContext('2d').getImageData(0,0,1,1).data];}await new Promise(r=>setTimeout(r,30));}return [];},imageName);
    check(marker[2]===194&&marker[3]===255,'Mod skin image feeds battlefield thumbnail');
    await page.evaluate(()=>fetch('/api/mods',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:'activate',key:null})}));await page.reload();await page.waitForFunction(()=>startupReady);
    check(await page.evaluate(()=>ew4Portraits.entries.every(e=>e.skins.length===0&&!e.portrait)),'vanilla appearance isolated');
    await page.evaluate(key=>fetch('/api/mods',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:'activate',key})}),key);await page.reload();await page.waitForFunction(()=>startupReady);
    check(await page.evaluate(id=>ew4Portraits.entries.find(e=>e.id===id).portrait==='rewrite/skins/demo.png',id),'Mod appearance restored after switching');
    await page.evaluate(()=>fetch('/api/mods',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:'activate',key:null})}));
    check(!errors.length,'no browser exceptions: '+errors.join(';'));check(!missing.length,'no missing images: '+missing.join(';'));
    await fs.writeFile(path.join(out,'results.json'),JSON.stringify({checks,errors,missing},null,2));console.log(JSON.stringify({checks,errors,missing}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
