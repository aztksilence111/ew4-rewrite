'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5585';
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--mute-audio']});
  const report={checks:0,errors:[],missing:[],maps:[],requests:[]};
  const check=(v,message)=>{assert.ok(v,message);report.checks++;};
  try{
    for(const renderer of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width:1440,height:900}});
      page.on('pageerror',e=>report.errors.push(e.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.route('**/fonts/parts/*.woff2',async route=>{
        report.requests.push(new URL(route.request().url()).pathname);
        if(!route.request().url().includes('-core.woff2'))await new Promise(resolve=>setTimeout(resolve,250));
        await route.continue();
      });
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));
      await page.waitForFunction(()=>startupReady,null,{timeout:180000});
      await page.evaluate(()=>{settings.cityLanguage='zh';settings.geographicLanguage='zh';window.prematureMapGlyphs=[];
        const element=originalTextRenderer.element.bind(originalTextRenderer);
        originalTextRenderer.element=(text,font,...args)=>{
          if(font==='font_city_labels_hd'&&Ew4OriginalUI.availableText(text)!==String(text).normalize('NFC'))prematureMapGlyphs.push(text);
          return element(text,font,...args);
        };
      });
      for(const scenario of ['conquest8','conquest9','conquest7']){
        await page.evaluate(scenario=>start(scenario,'fra'),scenario);
        await page.waitForFunction(()=>!!state&&!busy&&!sessionTransition&&!aiRunning,null,{timeout:180000});
        const stats=await page.evaluate(()=>{
          const names=state.cells.flatMap((cell,index)=>cell.name?[cityLabelText(cell.name,index,state.map,settings.cityLanguage)]:[]);
          const labels=manifest.maps[state.map].labels.map(l=>l.textZh).filter(Boolean);
          const unavailable=[...names,...labels].filter(n=>Ew4OriginalUI.availableText(n)!==n.normalize('NFC'));
          return {map:state.map,names:names.length,labels:labels.length,unavailable,failures:Ew4OriginalUI.fontStatus.failures};
        });
        check(stats.names>250&&!stats.unavailable.length&&!stats.failures.length,'all current map names ready before entry '+JSON.stringify(stats));
        if(scenario==='conquest8'){
          const rare=await page.evaluate(()=>['诹访','关锻冶','西乡隐岐','飞驒山脉'].map(text=>{
            const actual=originalTextRenderer.element(text,'font_city_labels_hd').firstChild;
            const replacement=originalTextRenderer.element(text.replace(/[诹冶乡驒]/g,'\ufffd'),'font_city_labels_hd').firstChild;
            const data=actual.getContext('2d').getImageData(0,0,actual.width,actual.height).data;
            return {text,available:Ew4OriginalUI.availableText(text),ink:data.filter((v,i)=>i%4===3&&v>128).length,distinct:actual.toDataURL()!==replacement.toDataURL()};
          }));
          for(const row of rare)check(row.available===row.text&&row.ink>50&&row.distinct,'real rare glyph pixels '+JSON.stringify(row));
          await page.evaluate(()=>{const l=manifest.maps[state.map].labels.find(l=>l.textZh==='飞驒山脉');view={x:l.tx,y:l.ty,zoom:.7};clampView();requestRender();});
          await page.waitForTimeout(700);
          if(renderer==='pixi')await page.screenshot({path:path.resolve('maps/japan_hre_source_20261026/game-japan.jpg'),type:'jpeg',quality:90});
        }
        report.maps.push({renderer,...stats});
      }
      check(!(await page.evaluate(()=>prematureMapGlyphs)).length,'no city canvas contains a pending-font replacement glyph');
      await page.close();
    }
    check(!report.errors.length&&!report.missing.length,'no script or font resource errors');
  }finally{
    await fs.writeFile(path.resolve('maps/japan_hre_source_20261026/game-font-verification.json'),JSON.stringify(report,null,2)+'\n');
    await browser.close();
  }
  console.log(JSON.stringify({checks:report.checks,maps:report.maps.length,errors:report.errors,missing:report.missing}));
})().catch(error=>{console.error(error);process.exitCode=1;});
