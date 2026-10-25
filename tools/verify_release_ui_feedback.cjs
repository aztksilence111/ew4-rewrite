'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5572',out=path.resolve(process.argv[3]||'build/release-ui-feedback-20261025/browser');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const report={checks:0,errors:[],missing:[],layouts:0},check=(ok,label)=>{assert.ok(ok,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:process.env.EW4_BROWSER||'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    const page=await browser.newPage({viewport:{width:1920,height:873},hasTouch:true});
    page.on('pageerror',e=>report.errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400)report.missing.push(r.status()+' '+r.url());});
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    for(const [width,height] of [[568,320],[1920,873],[844,390],[1280,800],[768,1024],[390,844]]){
      await page.setViewportSize({width,height});
      for(const screen of [80,100,150]){
        await page.evaluate(screen=>{settings.screen=screen;sizeUi();},screen);
        await page.locator('[data-mode="1"]').click();
        const layout=await page.evaluate(()=>{
          const list=$('scenarios'),r=list.getBoundingClientRect(),stage=$('scenario-menu').getBoundingClientRect();
          return {fills:Math.abs(r.width-stage.width)<.1&&Math.abs(r.height-stage.height)<.1,
            background:getComputedStyle($('scenario-menu')).backgroundSize,
            markers:[...list.querySelectorAll('.chapter-entry')].map(b=>{
              const chapter=catalog.chapters.find(c=>c.name===b.getAttribute('aria-label'));
              const a=manifest.layouts.form_selcampaign.children.find(e=>e.attributes.id==='btn_selcamp_'+chapter.id).attributes;
              const rect=b.getBoundingClientRect(),scale=screenLayout.menuScale;
              return {errorX:Math.abs((rect.left+25*scale-r.left)/r.width-(Number(a.x)+25)/568),
                errorY:Math.abs((rect.top+54*scale-r.top)/r.height-(Number(a.y)+54)/320),
                fixedSize:Math.abs(rect.width/scale-Number(a.w))<.1&&Math.abs(rect.height/scale-Number(a.h))<.1,
                fits:rect.left>=-.1&&rect.top>=-.1&&rect.right<=innerWidth+.1&&rect.bottom<=innerHeight+.1};
            })};
        });
        check(layout.fills&&layout.background==='100% 100%','chapter anchors share the full map rectangle');
        check(layout.markers.length===6&&layout.markers.every(m=>m.errorX<.0001&&m.errorY<.0001&&m.fixedSize&&m.fits),'six chapter tips track map coordinates and keep original artwork size');
        for(let i=0;i<6;i++){
          await page.locator('.chapter-entry').nth(i).tap();await page.waitForTimeout(240);
          check(await page.locator('.chapter-preview').evaluate(el=>{
            const r=el.getBoundingClientRect(),list=el.parentElement.getBoundingClientRect(),s=list.width/el.parentElement.clientWidth;
            const anchor=[...el.parentElement.querySelectorAll('.chapter-entry')].find(b=>el.getAttribute('aria-label').startsWith(b.getAttribute('aria-label')+' '));
            const a=anchor.getBoundingClientRect(),arrow=parseFloat(el.style.getPropertyValue('--chapter-arrow-left'));
            return r.left>=-.1&&r.top>=-.1&&r.right<=innerWidth+.1&&r.bottom<=innerHeight+.1
              &&Math.abs(r.left+(arrow+16.5)*s-(a.left+25*s))<1.6*s
              &&Math.abs(r.top+69.5*s-(a.top+54*s))<1.6*s;
          }),'chapter preview arrow stays attached after resizing');
        }
        await page.evaluate(()=>document.querySelector('.chapter-preview')?.remove());
        if(screen===100)await page.screenshot({path:path.join(out,`chapters-${width}x${height}.png`)});
        for(const mode of [1,2]){
          if(mode===2){await page.evaluate(()=>showMain());await page.locator('[data-mode="2"]').click();await page.waitForFunction(()=>!conquestEntering);}
          check(await page.locator('#continue').evaluate(el=>{
            const text=el.querySelector('.original-text'),canvas=text?.querySelector('canvas');if(!canvas)return false;
            const r=el.getBoundingClientRect(),t=text.getBoundingClientRect(),pixels=canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
            return el.getAttribute('aria-label')==='载入'&&!el.querySelector('img')&&pixels.some((v,i)=>i%4===3&&v>0)
              &&t.left>=r.left+23*(r.width/82)-1&&t.right<=r.right+.1&&t.top>=r.top-.1&&t.bottom<=r.bottom+.1;
          }),'load label renders nonblank bundled glyphs within its text area');
          await page.locator('#continue').tap();await page.locator('#detail-modal').waitFor({state:'visible'});
          check(await page.locator('.save-slots').count()===1,'load opens the real save slots');
          await page.locator('#close-detail').tap();
        }
        await page.evaluate(()=>showMain());report.layouts++;
      }
    }
    for(const id of ['conquest1','campaign1_01']){
      await page.setViewportSize({width:1920,height:873});
      await page.evaluate(id=>start(id,id==='conquest1'?'fra':null),id);await page.waitForFunction(()=>!busy);
      await page.evaluate(()=>{$('detail-modal').hidden=true;$('menu-button').click();});
      await page.waitForFunction(()=>!$('pause-objectives').hidden);
      check(await page.evaluate(()=>!$('performance-caption')&&![...document.querySelectorAll('#pause small')].length&&!!ew4Performance.build),'pause omits test sequence while diagnostic data remains available');
      check(await page.locator('#turn .original-text').count()===1,'pause still renders round label');
      for(const button of ['save','pause-settings','restart','return-menu'])check(await page.locator('#'+button).isVisible(),'pause command remains visible: '+button);
      await page.screenshot({path:path.join(out,`pause-${id}.png`)});await page.evaluate(()=>showMain());
    }
    check(report.errors.length===0&&report.missing.length===0,'no browser errors or missing resources');
    await fs.writeFile(path.join(out,'verification.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
