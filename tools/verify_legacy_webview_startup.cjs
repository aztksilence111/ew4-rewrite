'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.env.EW4_TEST_URL||'http://127.0.0.1:5227',baseline=path.resolve('build/android10-startup-20261021/baseline'),folder=path.resolve(process.env.EW4_STARTUP_REPORT||'build/android10-startup-20261021');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const report={checks:[],scenarios:[],errors:[],scope:'Desktop Chromium with removed APIs and injected failures; no Android 10 device execution'};
  const check=(value,label)=>{assert.ok(value,label);report.checks.push(label);};
  async function scenario(name,action){
    const page=await browser.newPage({viewport:{width:844,height:390}}),errors=[];
    page.on('pageerror',error=>errors.push(error.message));
    try{await action(page,errors);report.scenarios.push({name,errors});}
    finally{await page.close();}
  }
  const oldApis=()=>{
    delete window.WeakRef;delete Array.prototype.at;
    for(const prototype of [Element.prototype,Document.prototype,DocumentFragment.prototype])delete prototype.replaceChildren;
    delete Element.prototype.getAnimations;
  };
  const ready=page=>page.waitForFunction(()=>typeof startupReady!=='undefined'&&startupReady);
  try{
    if(!process.argv.includes('--skip-baseline'))await scenario('exact 0.2.6 frontend without modern APIs',async(page,errors)=>{
      await page.addInitScript(oldApis);
      await page.route('**/*',async route=>{
        const name=new URL(route.request().url()).pathname.slice(1);
        if(/^[^/]+\.(js|html)$/.test(name)){
          try{return await route.fulfill({body:await fs.readFile(baseline+'/'+name),contentType:name.endsWith('.js')?'text/javascript':'text/html'});}
          catch(error){if(error.code!=='ENOENT')throw error;}
        }
        await route.continue();
      });
      await page.goto(base+'/index.html');
      await page.waitForFunction(()=>typeof startupError!=='undefined'&&startupError);
      check(!(await page.evaluate(()=>startupReady)),'0.2.6 fails to initialize when legacy APIs are missing');
      const message=await page.locator('#startup-message').innerText();
      check(/WeakRef|at is not a function|replaceChildren/.test(message),'0.2.6 reproduces unsupported platform API failure: '+message);
    });
    await scenario('fixed startup and workflows without modern APIs',async(page,errors)=>{
      await page.addInitScript(oldApis);await page.goto(base);await ready(page);
      check(await page.locator('#startup-status').isHidden(),'compatibility startup reaches main menu');
      check(await page.evaluate(()=>Object.keys(Array.prototype).indexOf('at')<0&&[1,2].at(-1)===2&&[1].at(-2)===undefined),'Array.at fallback is non-enumerable with negative bounds');
      check(await page.evaluate(()=>{const host=document.createElement('div'),child=document.createElement('i');host.appendChild(child);host.replaceChildren('text',child);return host.textContent==='text'&&host.lastChild===child;}),'replaceChildren preserves existing nodes and text');
      await page.locator('#headquarters').click();await page.waitForFunction(()=>document.querySelectorAll('.commander-card').length>0);
      check(true,'headquarters renders commander roster without WeakRef');
      await page.locator('#hq-back').click();await page.locator('[data-mode="1"]').click();
      await page.waitForFunction(()=>document.querySelectorAll('.chapter-entry').length>0);
      check(true,'campaign menu works without getAnimations');
      await page.locator('#menu-back').click();await page.locator('[data-mode="2"]').click();
      await page.waitForFunction(()=>document.querySelectorAll('.scenario-entry').length>0);
      check(true,'conquest menu works without getAnimations');
      const repaint=await page.evaluate(async()=>{
        const label=originalTextRenderer.element('𠮷𰀀','font_text_2_cn_hd'),canvas=label.firstChild;
        let paints=0;const redraw=canvas.ew4Repaint;canvas.ew4Repaint=()=>{paints++;redraw();};
        document.body.appendChild(label);await Ew4OriginalUI.prepareText('𠮷𰀀');
        const valid=Ew4OriginalUI.availableText('𠮷𰀀')==='𠮷𰀀'&&paints>0;label.remove();return valid;
      });
      check(repaint,'deferred rare glyphs repaint without WeakRef');
      await page.evaluate(async()=>{await start('conquest1','fra');});
      check(await page.locator('#play').isVisible(),'battle starts and renders with legacy API fallbacks');
      await page.screenshot({path:folder+'/legacy-battle.png'});
      await page.evaluate(()=>showMain());
      for(const viewport of [{width:844,height:390},{width:390,height:844}]){
        await page.setViewportSize(viewport);await page.screenshot({path:folder+'/legacy-menu-'+viewport.width+'.png'});
      }
      check(errors.length===0,'legacy API workflow has zero uncaught errors');
    });
    await scenario('script parse failure',async(page)=>{
      await page.route('**/app.js?*',route=>route.fulfill({contentType:'text/javascript',body:'const broken = ;'}));
      await page.goto(base);await page.waitForFunction(()=>document.querySelector('#startup-message').textContent.includes('app.js'));
      check(await page.locator('#startup-retry').isVisible(),'parse error exposes reload instead of endless loading');
      check((await page.locator('#startup-message').innerText()).includes('WebView / Chromium'),'parse error includes renderer version');
      await page.screenshot({path:folder+'/parse-error.png'});
    });
    await scenario('missing script',async(page)=>{
      await page.route('**/original-ui.js?*',route=>route.fulfill({status:404,body:'missing'}));
      await page.goto(base);await page.waitForFunction(()=>document.querySelector('#startup-message').textContent.includes('脚本载入失败'));
      check((await page.locator('#startup-message').innerText()).includes('original-ui.js'),'missing script reports original resource before secondary errors');
    });
    await scenario('outdated renderer diagnostic',async(page)=>{
      await page.addInitScript(()=>Object.defineProperty(navigator,'userAgent',{get:()=> 'Mozilla/5.0 (Linux; Android 10; wv) Chrome/69.0.3497.100 Mobile Safari/537.36'}));
      await page.route('**/app.js?*',route=>route.fulfill({contentType:'text/javascript',body:'const broken = ;'}));
      await page.goto(base);await page.waitForFunction(()=>document.querySelector('#startup-message').textContent.includes('69'));
      check((await page.locator('#startup-message').innerText()).includes('更新系统 WebView'),'obsolete renderer gets actionable component update message');
      await page.screenshot({path:folder+'/outdated-webview.png'});
    });
    await scenario('pending catalog diagnostic',async(page)=>{
      let release;const gate=new Promise(resolve=>{release=resolve;});
      await page.addInitScript(()=>{const schedule=window.setTimeout;window.setTimeout=function(fn,delay){return schedule(fn,delay===45000?500:delay);};});
      await page.route('**/api/catalog?*',async route=>{await gate;await route.continue().catch(()=>{});});
      try{
        await page.goto(base);await page.waitForFunction(()=>document.querySelector('#startup-message').textContent.includes('载入超时'));
        check((await page.locator('#startup-message').innerText()).includes('游戏目录与资源索引'),'timeout identifies stalled startup phase');
        check(await page.locator('#startup-retry').isVisible(),'pending startup provides reload');
        await page.screenshot({path:folder+'/timeout.png'});release();await ready(page);
        check(await page.locator('#startup-status').isHidden(),'late success clears timeout diagnostic');
      }finally{release();}
    });
  }catch(error){report.errors.push(error.message);throw error;}
  finally{await fs.writeFile(folder+'/browser-results.json',JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify({checks:report.checks.length,scenarios:report.scenarios.length,errors:report.errors}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
