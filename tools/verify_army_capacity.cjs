'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright'),{PNG}=deps('pngjs');
const base=process.argv[2]||'http://127.0.0.1:5577',folder=path.resolve(process.argv[3]||'build/army-capacity-20261026/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,errors:[],missing:[],views:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  try{
    for(const renderer of ['canvas','pixi']){
      const page=await browser.newPage({viewport:{width:1280,height:720},deviceScaleFactor:1});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push([r.status(),r.url()]);});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));
      await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>!busy&&!aiRunning&&state?.units?.length,null,{timeout:90000});
      await page.evaluate(()=>{stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();requestRender();});
      check(await page.evaluate(()=>!!ctx.gpu)===(renderer==='pixi'),'actual '+renderer+' backend');
      check(await page.evaluate(()=>{const p=state.factions.find(f=>f.id===state.playerFaction);return p.unitCount===32&&p.unitLimit===49;}),'server reports actual 32/49');
      check(await page.evaluate(()=>[...$('army-capacity').querySelectorAll('img')].every(img=>img.complete&&img.naturalWidth>0)),'reference icons loaded');
      const site=await page.evaluate(()=>state.cells.findIndex(c=>c.owner===state.playerFaction&&c.buildingKind));
      await page.evaluate(cell=>selectCell(cell),site);
      for(const [name,width,height] of [['desktop',1280,720],['landscape',844,390],['portrait',390,844],['small',320,568],['wide',1920,1080],['tablet',1024,768]]){
        await page.setViewportSize({width,height});
        await page.evaluate(()=>{refreshHud();requestRender();});
        await page.waitForTimeout(100);
        const bounds=await page.evaluate(()=>{
          const box=id=>{const r=$(id).getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,width:r.width,height:r.height};};
          const host=box('army-capacity'),menu=box('menu-button'),terrain=box('terrain-info'),resources=document.querySelector('.battle-resources').getBoundingClientRect();
          const overlap=r=>host.left<r.right-.5&&host.right>r.left+.5&&host.top<r.bottom-.5&&host.bottom>r.top+.5;
          const value=$('army-capacity-value'),glyphs=value.firstElementChild?.getBoundingClientRect();
          return {host,menu,terrain,resources:{left:resources.left,right:resources.right,top:resources.top,bottom:resources.bottom},center:(host.left+host.right)/2,overlap:overlap(menu)||overlap(resources)||(!$('selection').hidden&&overlap(terrain)),
            clipped:glyphs&&(glyphs.width>value.getBoundingClientRect().width+.5),label:$('army-capacity').getAttribute('aria-label'),status:$('army-capacity').dataset.status};
        });
        check(bounds.host.left>=-.5&&bounds.host.right<=width+.5&&bounds.host.top>=0&&bounds.host.bottom<height,'HUD stays inside '+renderer+'/'+name);
        check(Math.abs(bounds.center-width/2)<1,'HUD stays centered '+renderer+'/'+name);
        check(!bounds.overlap,'HUD avoids resources/pause/terrain '+renderer+'/'+name+' '+JSON.stringify(bounds));
        check(!bounds.clipped,'digits fit '+renderer+'/'+name);
        check(bounds.label.includes('32/49')&&bounds.status==='normal','normal capacity '+renderer+'/'+name);
        check(await page.evaluate(()=>{
          const host=$('army-capacity'),round=$('army-capacity-round'),glyphs=round.firstElementChild.getBoundingClientRect(),box=round.getBoundingClientRect();
          const bar=getComputedStyle(host,'::before');
          return getComputedStyle(host).backgroundImage==='none'&&round.ew4HudText===String(state.round)&&host.ariaLabel.includes('回合 '+state.round)
            &&glyphs.width<=box.width+.5&&getComputedStyle(host,'::after').content==='none'
            &&bar.backgroundImage.includes('linear-gradient')&&bar.clipPath==='polygon(0px 0px, 100% 0px, 92% 100%, 8% 100%)'
            &&[...host.children].every(child=>getComputedStyle(child).filter==='none');
        }),'symmetric fading shadow bar without glyph shadows and current round '+renderer+'/'+name);
        check(await page.evaluate(()=>{
          const host=$('army-capacity'),bar=host.getBoundingClientRect(),inset=bar.width*.08,scale=bar.width/host.offsetWidth;
          return [...host.children].every(child=>{const r=child.getBoundingClientRect();
            return r.left>=bar.left+inset&&r.right<=bar.right-inset&&r.top>=bar.top&&r.bottom<=bar.bottom-3*scale;});
        }),'shadow bar fully covers all icons and values '+renderer+'/'+name);
        const screenshot=await page.screenshot({path:path.join(folder,renderer+'-'+name+'.png')});
        const png=PNG.sync.read(screenshot),colors=new Set();
        for(let y=Math.floor(png.height*.3);y<png.height*.8;y+=11)for(let x=20;x<png.width-20;x+=13){const i=(y*png.width+x)*4;colors.add([png.data[i],png.data[i+1],png.data[i+2]].join(','));}
        check(colors.size>32,'battlefield renders '+renderer+'/'+name);
        report.views.push({renderer,name,...bounds});
      }
      for(const [count,limit,status] of [[49,49,'full'],[50,49,'over'],[2147483647,2147483647,'full']]){
        const actual=await page.evaluate(({count,limit})=>{const p=state.factions.find(f=>f.id===state.playerFaction);p.unitCount=count;p.unitLimit=limit;refreshHud();
          const host=$('army-capacity'),value=$('army-capacity-value'),text=value.firstElementChild?.getBoundingClientRect();return {status:host.dataset.status,label:host.ariaLabel,fits:!text||text.width<=value.getBoundingClientRect().width+.5};},{count,limit});
        check(actual.status===status&&actual.label.includes(count+'/'+limit),'full/over status '+count);
        check(actual.fits,'long counters fit '+renderer);
      }
      for(const [name,width,height] of [['desktop',1280,720],['portrait',390,844]]){
        await page.setViewportSize({width,height});
        await page.evaluate(()=>{const p=state.factions.find(f=>f.id===state.playerFaction);p.unitCount=49;p.unitLimit=49;refreshHud();
          document.querySelector('.army-capacity-hourglass').style.backgroundColor='#000';$('army-capacity-round').style.backgroundColor='#000';});
        await page.waitForTimeout(100);
        const hourglass=await page.locator('.army-capacity-hourglass').screenshot(),round=await page.locator('#army-capacity-round').screenshot();
        await page.evaluate(()=>{const p=state.factions.find(f=>f.id===state.playerFaction);p.unitCount=50;refreshHud();});
        const redPixels=buffer=>{const png=PNG.sync.read(buffer);let count=0;for(let i=0;i<png.data.length;i+=4)if(png.data[i]>120&&png.data[i]>png.data[i+1]*1.4&&png.data[i]>png.data[i+2]*1.4)count++;return count;};
        check(redPixels(await page.locator('.army-capacity-icon').screenshot())>4,'over capacity icon is visibly red '+renderer+'/'+name);
        check(redPixels(await page.locator('#army-capacity-value').screenshot())>8,'over capacity count and limit are visibly red '+renderer+'/'+name);
        check(hourglass.equals(await page.locator('.army-capacity-hourglass').screenshot()),'hourglass pixels unchanged '+renderer+'/'+name);
        check(round.equals(await page.locator('#army-capacity-round').screenshot()),'round pixels unchanged '+renderer+'/'+name);
        await page.evaluate(()=>{document.querySelector('.army-capacity-hourglass').style.backgroundColor='';$('army-capacity-round').style.backgroundColor='';});
        check(await page.evaluate(()=>$('army-capacity').title.includes('资源产出减半，粮食消耗翻倍')),'penalty tooltip '+renderer+'/'+name);
        await page.screenshot({path:path.join(folder,renderer+'-'+name+'-over.png')});
        await page.evaluate(()=>{const p=state.factions.find(f=>f.id===state.playerFaction);p.unitCount=48;refreshHud();});
        check(await page.evaluate(()=>getComputedStyle(document.querySelector('.army-capacity-icon')).filter==='none'&&$('army-capacity').dataset.status==='normal'),'icon restores after returning below capacity '+renderer+'/'+name);
      }
      check(await page.evaluate(()=>{state.round=12345;refreshHud();const round=$('army-capacity-round');return round.ew4HudText==='12345'&&round.firstElementChild.getBoundingClientRect().width<=round.getBoundingClientRect().width+.5;}),'round updates and long round fits '+renderer);
      await page.close();
    }
    check(report.errors.length===0,'no page errors: '+report.errors.join(', '));
    check(report.missing.length===0,'no missing resources');
    await fs.writeFile(path.join(folder,'checks.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify({checks:report.checks,views:report.views.length,errors:report.errors.length,missing:report.missing.length}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
