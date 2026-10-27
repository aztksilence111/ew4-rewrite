'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5588',folder=path.resolve(process.argv[3]||'build/save-export-test-20261027/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,errors:[],views:[]};const check=(ok,label)=>{assert(ok,label);report.checks++;};
  try{
    for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      const page=await browser.newPage({viewport,deviceScaleFactor:2});page.on('pageerror',e=>report.errors.push(e.message));
      await page.addInitScript(()=>{window.exportCalls=0;window.Ew4ModImport={exportSaves(){window.exportCalls++;}};});
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.locator('#main-settings').click();await page.locator('#save-export').scrollIntoViewIfNeeded();
      const layout=await page.locator('#save-export').evaluate(button=>{const b=button.getBoundingClientRect(),r=button.parentElement.getBoundingClientRect();return {fits:b.left>=r.left&&b.right<=r.right+1&&b.top>=r.top&&b.bottom<=r.bottom+1,textFits:button.scrollWidth<=button.clientWidth+1,width:b.width,height:b.height};});
      check(layout.fits&&layout.textFits,'export control fits '+JSON.stringify({viewport,layout}));
      await page.locator('#save-export').click();check(await page.evaluate(()=>window.exportCalls===1),'one native export request');
      check(await page.locator('#save-export').isDisabled(),'busy while native picker/export open');
      await page.evaluate(()=>window.ew4SaveExportFinished({ok:false,message:''}));check(await page.locator('#save-export').isEnabled(),'cancel resets button');
      await page.locator('#save-export').click();await page.evaluate(()=>window.ew4SaveExportFinished({ok:true,message:'已导出 5 个存档及备份文件'}));
      await page.waitForFunction(()=>document.getElementById('notice').querySelector('.original-text')?.getAttribute('aria-label')==='已导出 5 个存档及备份文件');
      const success=await page.locator('#notice').evaluate(e=>e.querySelector('.original-text')?.getAttribute('aria-label')||e.textContent);
      check(await page.locator('#save-export').isEnabled()&&success==='已导出 5 个存档及备份文件','success resets control and shows result: '+success);
      await page.evaluate(()=>{window.Ew4ModImport.exportSaves=()=>{throw Error('导出测试失败');};});await page.locator('#save-export').click();
      await page.waitForFunction(()=>document.getElementById('notice').querySelector('.original-text')?.getAttribute('aria-label')==='导出测试失败');
      check(await page.locator('#save-export').isEnabled()&&await page.locator('#notice').evaluate(e=>(e.querySelector('.original-text')?.getAttribute('aria-label')||e.textContent)==='导出测试失败'),'bridge failure recovers');
      await page.screenshot({path:path.join(folder,viewport.width+'x'+viewport.height+'.png')});report.views.push({viewport,layout});await page.close();
    }
    const web=await browser.newPage();await web.goto(base);await web.waitForFunction(()=>startupReady);
    check(await web.locator('#save-export-row').isHidden(),'unsupported Web host hides native export');await web.close();
    check(!report.errors.length,'no browser exceptions');await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
