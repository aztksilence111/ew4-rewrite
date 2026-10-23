'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5476',folder=process.argv[3]||'build/damage-bitmap-20261023/browser';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}}),report={checks:0,errors:[],missing:[],screenshots:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    report.pixels=await page.evaluate(()=>{
      const ui=()=>originalTextRenderer.element('0123456789','font_char_2_hd',undefined,undefined,'left',{numeric:true}).firstChild.toDataURL();
      const before=ui(),results=[];
      for(const amount of [1,20,345,6789]){
        damageNumbers.clear();damageNumbers.add(amount,false,0,0);damageNumbers.add(amount,true,0,0);
        const [normal,critical]=damageNumbers.instances.map(i=>i.canvas),expected=document.createElement('canvas');
        expected.width=normal.width;expected.height=48;const draw=expected.getContext('2d');let cursor=0;
        for(const char of '-'+amount){const [x,y,w,h,dx,dy,advance]=damageNumbers.font.glyphs[char.codePointAt(0)];draw.drawImage(damageNumbers.sheet,x,y,w,h,cursor-dx,-dy,w,h);cursor+=advance;}
        const a=normal.getContext('2d').getImageData(0,0,normal.width,normal.height).data,b=critical.getContext('2d').getImageData(0,0,critical.width,critical.height).data;
        let ink=0,red=0,black=0,tint=true;
        for(let i=0;i<a.length;i+=4){if(a[i+3]>128)ink++;if(b[i+3]>128&&b[i]>180&&b[i+1]<90)red++;if(b[i+3]>40&&b[i]<25&&b[i+1]<25)black++;
          if(a[i+3]!==b[i+3]||a[i+3]>128&&(Math.abs(a[i]-b[i])>2||Math.abs(b[i+1]-a[i+1]*48/255)>2||Math.abs(b[i+2]-a[i+2]*48/255)>2))tint=false;}
        results.push({amount,exact:normal.toDataURL()===expected.toDataURL(),ink,red,black,tint});
      }
      damageNumbers.clear();for(const value of [0,-1,NaN,Infinity])damageNumbers.add(value,false,0,0);
      const ignored=damageNumbers.instances.length===0;damageNumbers.add(123,false,0,0);const canvas=damageNumbers.instances[0].canvas;
      damageNumbers.clear();damageNumbers.add(123,false,0,0,2);const cached=canvas===damageNumbers.instances[0].canvas;
      const hit=damageNumbers.instances[0],draws=[];damageNumbers.frame({drawImage:(...args)=>draws.push(args.slice(1))},hit.started+400);
      damageNumbers.frame({drawImage(){}},hit.started+801);
      return {results,uiUnchanged:before===ui(),uiGlyph:originalTextRenderer.glyph('1','font_char_2_hd'),uiSheets:originalTextRenderer.sheets.size,
        sheet:damageNumbers.sheet.src,height:damageNumbers.font.height,ignored,cached,draws,expired:damageNumbers.instances.length===0};
    });
    for(const result of report.pixels.results){check(result.exact&&result.ink>100,'original atlas pixels for '+result.amount);check(result.tint&&result.red>20&&result.black>20,'critical preserves alpha and outline for '+result.amount);}
    check(report.pixels.uiUnchanged&&report.pixels.uiGlyph===null&&report.pixels.uiSheets===0,'other numeric text retains bundled font rendering');
    check(report.pixels.height===48&&report.pixels.sheet.endsWith('font_fight_num_hd.png'),'HD original atlas loaded');
    check(report.pixels.ignored&&report.pixels.cached&&report.pixels.expired&&report.pixels.draws[0][3]===24,'cache, invalid values, original size and lifetime');
    await page.evaluate(()=>start('campaign1_02b',null));
    await page.evaluate(async()=>{
      settings.speed=1;await selectCell(2544,true);const attacker=selected.id,target=state.units.find(u=>u.cell===selection.targets[0]);
      view.x=point(target.cell).x;view.y=point(target.cell).y;await selectCell(target.cell,true);window.bitmapAttack=act('attack',{unit:attacker,target:target.id});
    });
    await page.waitForFunction(()=>damageNumbers.instances.length>0);
    check(await page.evaluate(()=>damageNumbers.instances[0].canvas.height===48),'real combat uses bitmap damage');
    await page.evaluate(()=>bitmapAttack);
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(150);
      await page.evaluate(()=>{
        effects.clear();damageNumbers.clear();view.zoom=2;
        damageNumbers.add(123,false,view.x-45,view.y);damageNumbers.add(456,true,view.x+45,view.y);requestRender();
      });
      await page.waitForTimeout(80);const name='damage-'+width+'.png';await page.screenshot({path:folder+'/'+name});report.screenshots.push(name);
    }
    await page.route('**/manifest.json',async route=>{const response=await route.fetch(),json=await response.json();delete json.fonts.font_fight_num_hd;await route.fulfill({response,json});});
    await page.reload();await page.waitForFunction(()=>startupReady,null,{timeout:60000});
    check(await page.evaluate(()=>{damageNumbers.add(987,false,0,0);return damageNumbers.font.height===48&&damageNumbers.instances[0].canvas.width>0;}),'legacy manifest uses separate bitmap metrics');
    check(report.errors.length===0&&report.missing.length===0,'no script or asset failures');
  }catch(e){report.failure=e.message;throw e;}
  finally{await fs.writeFile(folder+'/verification.json',JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,screenshots:report.screenshots.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
