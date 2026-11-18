'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {chromium}=require('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(out,{recursive:true});let checks=0;const errors=[],missing=[];const check=(yes,label)=>{assert(yes,label);checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:720}});page.on('pageerror',e=>errors.push(e.message));page.on('response',r=>{if(r.status()>=400)missing.push(r.url());});
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    check(await page.evaluate(()=>ew4Portraits.entries.length>100),'all commander definitions listed');
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.locator('#main-settings').click();await page.locator('#portrait-gallery-open').click();await page.locator('#portrait-gallery').waitFor({state:'visible'});
      check(await page.locator('#portrait-requirements').textContent().then(s=>['PNG','JPG','WebP','10 MiB','4096','512'].every(v=>s.includes(v))),'format, size, resolution and recommended dimensions visible');
      const bounds=await page.locator('.portrait-dialog').boundingBox();check(bounds.x>=0&&bounds.y>=0&&bounds.x+bounds.width<=width+1&&bounds.y+bounds.height<=height+1,'gallery fits viewport');
      check(await page.locator('#portrait-gallery').evaluate(n=>n.scrollWidth<=n.clientWidth+1),'gallery has no horizontal overflow');
      await page.locator('#portrait-search').fill('not-a-commander-xxxxx');check(await page.locator('.portrait-tile').count()===0,'empty search state');
      await page.locator('#portrait-search').fill('');await page.locator('#portrait-sort').selectOption('rating');
      check(await page.locator('.portrait-tile').count()>0,'sort repopulates grid');
      const country=await page.locator('#portrait-country option').nth(1).getAttribute('value');await page.locator('#portrait-country').selectOption(country);
      check(await page.locator('.portrait-tile').count()>0,'country filter keeps matching commanders');await page.locator('#portrait-country').selectOption('');
      await page.screenshot({path:path.join(out,'gallery-'+width+'x'+height+'.png')});
      await page.locator('#portrait-gallery-close').click();await page.locator('#close-settings').click();
    }
    await page.setViewportSize({width:1280,height:720});await page.locator('#main-settings').click();await page.locator('#portrait-gallery-open').click();await page.locator('#portrait-gallery').waitFor({state:'visible'});
    const id=await page.locator('.portrait-tile').first().getAttribute('data-commander');await page.locator('.portrait-tile').first().click();
    const imageName=await page.evaluate(id=>ew4Portraits.entries.find(e=>e.id===id).image,id);
    const data=await page.evaluate(()=>{const c=document.createElement('canvas');c.width=640;c.height=960;const d=c.getContext('2d');d.fillStyle='#c22237';d.fillRect(0,0,640,960);d.fillStyle='#2277c2';d.fillRect(0,480,640,480);return c.toDataURL('image/png').split(',')[1];});
    for(const type of ['image/jpeg','image/webp']){
      const crop=await page.evaluate(async type=>{const c=document.createElement('canvas');c.width=c.height=512;const base64=c.toDataURL(type).split(',')[1];await ew4Portraits.readFile(ew4Portraits.dataFile({base64,type}));return !document.getElementById('portrait-crop').hidden;},type);
      check(crop,type+' is accepted and decoded');await page.locator('#portrait-crop-cancel').click();
    }
    await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=4097;c.height=1;await ew4Portraits.readFile(ew4Portraits.dataFile({base64:c.toDataURL().split(',')[1],type:'image/png'}));});
    check(await page.locator('#portrait-crop').isHidden()&&await page.locator('#portrait-message').textContent().then(s=>s.includes('4096')),'oversized dimensions rejected');
    await page.evaluate(()=>ew4Portraits.readFile(new File([new Uint8Array(10*1024*1024+1)],'large.png',{type:'image/png'})));
    check(await page.locator('#portrait-message').textContent().then(s=>s.includes('10 MiB')),'oversized file rejected');
    await page.evaluate(()=>ew4Portraits.message(''));
    await page.locator('#portrait-file').setInputFiles({name:'test.png',mimeType:'image/png',buffer:Buffer.from(data,'base64')});
    await page.waitForFunction(()=>!document.getElementById('portrait-crop').hidden||document.getElementById('portrait-message').textContent.length>0);
    check(await page.locator('#portrait-crop').isVisible(),'upload opens crop: '+await page.locator('#portrait-message').textContent()+'; '+errors.join(';'));
    await page.locator('#portrait-y').fill('35');await page.locator('#portrait-y').dispatchEvent('input');
    check(await page.locator('#portrait-marker-preview').evaluate(n=>n.width===128&&n.height===128),'circle preview derivative dimensions');
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.locator('#portrait-crop-save').scrollIntoViewIfNeeded();
      const bounds=await page.locator('#portrait-crop-save').boundingBox();check(bounds.x>=0&&bounds.y>=0&&bounds.x+bounds.width<=width&&bounds.y+bounds.height<=height,'crop save reachable at '+width+'x'+height);
      check(await page.locator('#portrait-editor').evaluate(n=>n.scrollWidth<=n.clientWidth+1),'crop editor has no horizontal overflow');
      await page.screenshot({path:path.join(out,'crop-'+width+'x'+height+'.png')});
    }
    await page.setViewportSize({width:1280,height:720});
    await page.locator('#portrait-crop-save').click();await page.waitForFunction(()=>!ew4Portraits.working&&document.getElementById('portrait-message').textContent.includes('保存'));
    check(await page.evaluate(name=>ew4Portraits.url(name).includes('/portraits/'),imageName),'upload selected by original sprite identity');
    const url=await page.evaluate(name=>ew4Portraits.url(name),imageName);check((await page.request.get(new URL(url,base).href)).ok(),'saved image route works');
    const pixelCheck=await page.evaluate(async name=>{
      await loadImage('images/commandermark_hd.png');ew4Portraits.marker(name);await new Promise((resolve,reject)=>{let tries=0;const wait=()=>{if(ew4Portraits.marker(name))resolve();else if(tries++>100)reject(Error('marker load timeout'));else setTimeout(wait,30);};wait();});
      const r=new Ew4Battlefield.Renderer({manifest,image,imageRevision,portrait:n=>ew4Portraits.marker(n),sprite}),canvas=document.createElement('canvas');canvas.width=canvas.height=200;r.draw=canvas.getContext('2d');r.commanderMarker(name,100,150);
      const rect=manifest.atlases.commandermark_hd['board_smallgenerals.png'],ratio=Ew4Battlefield.spritePixelRatio(rect,2),x=100-rect.refx/ratio+rect.w/ratio/2,y=150-rect.refy/ratio+13.25;
      const pixel=[...r.draw.getImageData(Math.round(x),Math.round(y),1,1).data];return {pixel,composed:r.commanderMarkers.size};
    },imageName);
    check(pixelCheck.composed===1&&pixelCheck.pixel[3]===255&&pixelCheck.pixel[2]>120,'battle marker displays uploaded crop inside original pointer');
    check(await page.evaluate(name=>menus.portrait(name).src.includes('/portraits/'),imageName),'headquarters portrait uses uploaded image');
    await page.reload();await page.waitForFunction(()=>startupReady);check(await page.evaluate(name=>!!ew4Portraits.url(name),imageName),'persists across reload');
    await page.evaluate(id=>ew4Portraits.open(id),id);check(await page.locator('#portrait-gallery').getAttribute('class').then(s=>s.includes('single-commander')),'detail skin picker targets one commander');
    await page.getByRole('button',{name:'原始头像',exact:true}).click();await page.waitForFunction(()=>!ew4Portraits.working);check(await page.evaluate(name=>!ew4Portraits.url(name),imageName),'restore original takes effect immediately');
    await page.getByRole('button',{name:'我的上传',exact:true}).click();await page.waitForFunction(()=>!ew4Portraits.working);check(await page.evaluate(name=>!!ew4Portraits.url(name),imageName),'uploaded appearance can be reused');
    await page.locator('#portrait-remove').click();await page.waitForFunction(()=>!ew4Portraits.working);check(await page.locator('#portrait-remove').isHidden(),'remove uploaded image');
    await page.locator('#portrait-file').setInputFiles({name:'bad.png',mimeType:'image/png',buffer:Buffer.from('not an image')});await page.waitForFunction(()=>document.getElementById('portrait-message').textContent.includes('仅支持'));check(await page.locator('#portrait-crop').isHidden(),'invalid image rejected without crop');
    // Android picker callback uses the same validation/crop path, without requiring a file input.
    await page.evaluate(base64=>{window.Ew4ModImport={choosePortrait(){window.ew4PortraitFileSelected({base64,type:'image/png'});}};},data);
    await page.locator('#portrait-upload').click();await page.locator('#portrait-crop').waitFor({state:'visible'});check(true,'native selection callback opens crop preview');await page.locator('#portrait-crop-cancel').click();
    await page.route('**/api/portraits',route=>route.fulfill({contentType:'application/json',body:JSON.stringify({ok:false,message:'测试损坏的图库清单'})}));
    await page.reload();await page.waitForFunction(()=>startupReady);check(true,'unavailable cosmetic library does not prevent game startup or save export');await page.unroute('**/api/portraits');
    check(errors.length===0,'no browser exceptions: '+errors.join(';'));check(missing.length===0,'no missing resources: '+missing.join(';'));
    await fs.writeFile(path.join(out,'results.json'),JSON.stringify({checks,errors,missing},null,2));console.log(JSON.stringify({checks,errors,missing}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
