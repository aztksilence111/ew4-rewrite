'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,presentations:0,errors:[],missing:[],views:[]};
  const check=(ok,label)=>{assert.ok(ok,label);report.checks++;};
  try{
    const page=await browser.newPage();
    page.on('pageerror',e=>report.errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await page.evaluate(()=>{mode=1;});
    for(const viewport of [{width:2376,height:1080},{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(viewport);
      await page.evaluate(()=>menus.openChapter(catalog.chapters[5]));
      await page.waitForTimeout(400);
      const measurements=await page.evaluate(async()=>{
        const intro=$('campaign-intro'),description=$('campaign-description'),rows=$('campaign-battles').children;
        const rect=()=>intro.getBoundingClientRect();
        const before=rect().toJSON();rows[1].click();
        const samples=[];
        for(const wait of [0,65,150]){
          if(wait)await new Promise(r=>setTimeout(r,wait));
          samples.push({rect:rect().toJSON(),animations:intro.getAnimations({subtree:true}).length});
        }
        const canvas=description.querySelector('canvas').getBoundingClientRect(),box=description.getBoundingClientRect();
        return {before,samples,canvas:canvas.toJSON(),box:box.toJSON(),scrollWidth:description.scrollWidth,width:description.clientWidth};
      });
      check(measurements.samples.every(s=>!s.animations&&Math.abs(s.rect.left-measurements.before.left)<.1&&Math.abs(s.rect.top-measurements.before.top)<.1),'description stays still throughout switch '+viewport.width);
      check(measurements.canvas.right<=measurements.box.right+.1&&measurements.scrollWidth<=measurements.width,'description canvas fits actual width '+viewport.width+' '+JSON.stringify(measurements));
      await page.evaluate(()=>$('campaign-battles').firstElementChild.click());
      await page.screenshot({path:path.join(out,'campaign-description-'+viewport.width+'.png')});
      for(let chapter=0;chapter<6;chapter++){
        await page.evaluate(chapter=>menus.openChapter(catalog.chapters[chapter]),chapter);
        await page.locator('#campaign-battles').evaluate(el=>Promise.all(el.getAnimations({subtree:true}).map(a=>a.finished.catch(()=>{}))));
        const results=await page.evaluate(()=>[...$('campaign-battles').children].map(row=>{
          row.click();const description=$('campaign-description'),label=description.firstElementChild,canvas=description.querySelector('canvas');
          const c=canvas.getBoundingClientRect(),r=description.getBoundingClientRect(),list=$('campaign-battles').getBoundingClientRect();
          const intro=$('campaign-intro'),box=intro.getBoundingClientRect(),frame=getComputedStyle(intro,'::before'),scale=box.width/parseFloat(getComputedStyle(intro).width);
          const expected=menus.battle.description.startsWith('desc_')?'':menus.battle.description;
          description.scrollTop=description.scrollHeight;
          return {id:menus.battle.id,fits:c.left>=r.left-.1&&c.right<=r.right+.1&&r.right<=list.left+.1&&description.scrollWidth<=description.clientWidth,
            geometry:{gap:list.left-box.right,scale,right:r.right-box.right,left:frame.left,width:frame.width,border:frame.borderLeftWidth,boxWidth:box.width/scale},
            fills:Math.abs(list.left-box.right-8*scale)<.2&&Math.abs(r.right-box.right)<.1&&Math.abs(parseFloat(frame.left)+parseFloat(frame.width)+parseFloat(frame.borderLeftWidth)+parseFloat(frame.borderRightWidth)-box.width/scale)<.1,
            text:label.getAttribute('aria-label')===expected,still:$('campaign-intro').getAnimations({subtree:true}).length===0,
            scroll:description.scrollTop+description.clientHeight>=description.scrollHeight-1};
        }));
        for(const result of results){check(result.fits&&result.fills&&result.text&&result.still&&result.scroll,'complete readable static description '+viewport.width+' '+JSON.stringify(result));report.presentations++;}
      }
      report.views.push({viewport,measurements});
    }
    await page.setViewportSize({width:2376,height:1080});await page.waitForTimeout(80);
    const wide=await page.locator('#campaign-description canvas').evaluate(el=>parseFloat(el.style.width));
    await page.setViewportSize({width:1280,height:720});await page.waitForTimeout(80);
    check(await page.locator('#campaign-description').evaluate((el,wide)=>parseFloat(el.querySelector('canvas').style.width)<wide&&el.scrollWidth<=el.clientWidth,wide),'resize reflows current text without switching battle');
    check(!report.errors.length,'no browser errors '+JSON.stringify(report.errors));
    check(!report.missing.length,'no missing assets '+JSON.stringify(report.missing));
    await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify({checks:report.checks,presentations:report.presentations,views:report.views.length,errors:report.errors.length,missing:report.missing.length}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
