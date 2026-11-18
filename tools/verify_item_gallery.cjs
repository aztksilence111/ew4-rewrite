'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {chromium}=require('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(out,{recursive:true});let checks=0;const errors=[],missing=[];
  const check=(pass,label)=>{assert(pass,label);checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720}});
    page.on('pageerror',e=>errors.push(e.message));page.on('response',r=>{if(r.status()>=400)missing.push(r.url());});
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    check(await page.evaluate(()=>ew4ItemTextures.entries.length===54),'54 inherited item textures loaded');
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.locator('#main-settings').click();
      check(await page.locator('#item-texture-settings-row').getAttribute('aria-label')==='物品贴图'&&await page.locator('#item-gallery-open').getAttribute('aria-label')==='物品图库','requested entry labels');
      const rows=await page.evaluate(()=>['portrait-settings-row','item-texture-settings-row','ui-sizes-row'].map(id=>{const r=document.getElementById(id).getBoundingClientRect();return {y:r.y,bottom:r.bottom};}));
      check(rows[0].bottom<=rows[1].y+1&&rows[1].bottom<=rows[2].y+1,'item textures directly below commander portraits without overlap');
      if(width===1280)await page.screenshot({path:path.join(out,'settings.png')});
      await page.locator('#item-gallery-open').click();await page.locator('#item-gallery').waitFor({state:'visible'});
      if(width===1280)await page.screenshot({path:path.join(out,'gallery.png')});
      const bounds=await page.locator('.item-gallery-dialog').boundingBox();
      check(bounds.x>=0&&bounds.y>=0&&bounds.x+bounds.width<=width+1&&bounds.y+bounds.height<=height+1,'gallery fits '+width+'x'+height);
      check(await page.locator('#item-gallery').evaluate(n=>n.scrollWidth<=n.clientWidth+1),'no horizontal gallery overflow');
      await page.locator('#item-gallery-search').fill('10001');check(await page.locator('.item-gallery-tile').count()===1,'legacy duplicate ID finds shared inherited texture');
      await page.locator('.item-gallery-tile').first().click();
      check(await page.locator('#item-gallery-preview').evaluate(n=>n.complete&&n.naturalWidth>0),'default texture resolves');
      await page.screenshot({path:path.join(out,'gallery-'+width+'.png')});
      await page.locator('#item-gallery-close').click();await page.locator('#close-settings').click();
    }
    if(process.argv.includes('--read-only')){check(errors.length===0,'no browser errors');check(missing.length===0,'no missing content');await fs.writeFile(path.join(out,'result.json'),JSON.stringify({checks,errors,missing},null,2));console.log(JSON.stringify({checks,errors,missing,readOnly:true}));return;}
    await page.setViewportSize({width:1280,height:720});await page.locator('#main-settings').click();await page.locator('#item-gallery-open').click();
    for(const type of ['image/jpeg','image/webp']){
      await page.evaluate(async type=>{const c=document.createElement('canvas');c.width=c.height=256;await ew4ItemTextures.readFile(new File([await new Promise(resolve=>c.toBlob(resolve,type))],'input',{type}));},type);
      check(await page.locator('#item-gallery-crop').isVisible(),type+' upload supported');await page.locator('#item-gallery-cancel').click();
    }
    await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=4097;c.height=1;await ew4ItemTextures.readFile(new File([await new Promise(resolve=>c.toBlob(resolve))],'large.png',{type:'image/png'}));});
    check(await page.locator('#item-gallery-message').innerText().then(t=>t.includes('4096')),'oversized dimensions rejected');
    await page.evaluate(()=>ew4ItemTextures.readFile(new File([new Uint8Array(10*1024*1024+1)],'large.png',{type:'image/png'})));
    check(await page.locator('#item-gallery-message').innerText().then(t=>t.includes('10 MiB')),'oversized files rejected');
    const data=await page.evaluate(()=>{const c=document.createElement('canvas');c.width=c.height=256;const d=c.getContext('2d');d.fillStyle='#e91e63';d.fillRect(128,0,128,256);return c.toDataURL().split(',')[1];});
    await page.locator('#item-gallery-file').setInputFiles({name:'item.png',mimeType:'image/png',buffer:Buffer.from(data,'base64')});await page.locator('#item-gallery-crop').waitFor({state:'visible'});
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.locator('#item-gallery-save').scrollIntoViewIfNeeded();const r=await page.locator('#item-gallery-save').boundingBox();
      check(r.x>=0&&r.y>=0&&r.x+r.width<=width+1&&r.y+r.height<=height+1,'save reachable '+width+'x'+height);
      check(await page.locator('#item-gallery-editor').evaluate(n=>n.scrollWidth<=n.clientWidth+1),'editor has no horizontal overflow');
    }
    await page.locator('#item-gallery-save').click();await page.waitForFunction(()=>!ew4ItemTextures.working&&document.getElementById('item-gallery-message').textContent.includes('保存'));
    const uploaded=await page.evaluate(()=>ew4ItemTextures.url('War Horse.png'));
    check(!!uploaded,'uploaded texture selected');check((await page.request.get(new URL(uploaded,base).href)).ok(),'content route serves saved image');
    check(await page.evaluate(()=>uiImage('War Horse.png',45).src.includes('/item-textures/')),'shop and equipment images use uploaded texture');
    check(await page.evaluate(async()=>{const image=new Image();image.src=ew4ItemTextures.url('War Horse.png');await image.decode();const c=document.createElement('canvas');c.width=c.height=128;const d=c.getContext('2d');d.drawImage(image,0,0);return d.getImageData(8,8,1,1).data[3]===0&&d.getImageData(120,8,1,1).data[3]===255;}),'transparency preserved end to end');
    await page.locator('#item-gallery-filter').selectOption('custom');check(await page.locator('.item-gallery-tile').count()===1,'uploaded filter');
    await page.reload();await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    check(await page.evaluate(()=>!!ew4ItemTextures.url('War Horse.png')),'upload survives document reload');
    await page.locator('#main-settings').click();await page.locator('#item-gallery-open').click();await page.locator('#item-gallery-search').fill('10001');await page.locator('.item-gallery-tile').click();await page.locator('#item-gallery-restore').click();
    await page.waitForFunction(()=>!ew4ItemTextures.working&&!ew4ItemTextures.url('War Horse.png'));
    check(await page.evaluate(()=>!uiImage('War Horse.png',45).src.includes('/item-textures/')),'restore default updates game item images');
    check(errors.length===0,'no browser errors: '+errors.join(';'));check(missing.length===0,'no missing content: '+missing.join(';'));
    await fs.writeFile(path.join(out,'result.json'),JSON.stringify({checks,errors,missing},null,2));console.log(JSON.stringify({checks,errors,missing}));
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
