'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),{execFileSync}=require('node:child_process'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5607',folder=path.resolve(process.argv[3]||'build/save-export-fixed-20261029/browser');
const profile=process.argv[4]&&path.resolve(process.argv[4]);
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,errors:[],views:[]};const check=(ok,label)=>{assert(ok,label);report.checks++;};
  try{
    for(const viewport of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      const page=await browser.newPage({viewport,deviceScaleFactor:2});page.on('pageerror',e=>report.errors.push(e.message));
      await page.addInitScript(()=>{window.exportCalls=0;window.Ew4ModImport={exportSaves(){window.exportCalls++;}};});
      await page.goto(base);await page.waitForFunction(()=>startupReady);
      await page.locator('#main-settings').click();
      check(await page.locator('#save-export').evaluate(button=>{const b=button.getBoundingClientRect(),r=document.getElementById('settings-content').getBoundingClientRect();return b.top>=r.top&&b.bottom<=r.bottom;}),'native entry immediately visible without scrolling');
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
    const web=await browser.newPage({viewport:{width:844,height:390},hasTouch:true});web.on('pageerror',e=>report.errors.push(e.message));
    await web.goto(base);await web.waitForFunction(()=>startupReady);
    check(await web.evaluate(()=>!window.Ew4ModImport),'actual browser export has no native bridge');
    await web.locator('#main-settings').click();
    check(await web.locator('#save-export').isVisible(),'Web host always exposes permanent export');
    async function download(name){
      const [file]=await Promise.all([web.waitForEvent('download'),web.locator('#save-export').click()]);
      check(/^ew4-saves-\d{8}-\d{6}\.zip$/.test(file.suggestedFilename()),'timestamped archive name');
      const destination=path.join(folder,name);await file.saveAs(destination);
      check(await file.failure()===null,'browser ZIP download succeeds');
      await web.waitForFunction(()=>!document.getElementById('save-export').disabled);
      const manifest=JSON.parse(execFileSync('python',['-c',
        'import sys,json,zipfile,hashlib; z=zipfile.ZipFile(sys.argv[1]); assert z.testzip() is None; m=json.loads(z.read("export-info.json")); assert len(z.namelist())==len(m["files"])+1; assert all(len(z.read(f["path"]))==f["bytes"] and hashlib.sha256(z.read(f["path"])).hexdigest()==f["sha256"] for f in m["files"]); print(json.dumps(m))',destination],{encoding:'utf8'}));
      check(manifest.format==='ew4-save-export'&&manifest.platform==='web'&&!!manifest.applicationVersion&&/^[a-f0-9]{64}$/.test(manifest.frontendSource),'ZIP includes version, platform and frontend identity');
      for(const file of manifest.files){
        check(!file.path.includes('/content-mods/')&&!file.path.includes('/startup-assets/'),'ZIP excludes installed resources and caches');
        if(profile){
          const bytes=await fs.readFile(path.join(profile,file.path.slice('saves/'.length)));
          check(bytes.length===file.bytes&&crypto.createHash('sha256').update(bytes).digest('hex')===file.sha256,'export leaves original bytes unchanged');
        }
      }
      return manifest;
    }
    await download('menu.zip');
    await web.locator('#close-settings').click();await web.evaluate(()=>start('conquest1','fra'));
    await web.waitForFunction(()=>!busy&&!sessionTransition);
    if(await web.evaluate(()=>stageIntroOpen))await web.locator('#detail-confirm').click();
    for(let i=0;i<12;i++){
      await web.waitForFunction(()=>!busy&&!sessionTransition);
      const pending=await web.evaluate(()=>({story:!!state.story,dialogue:!!state.dialogue}));if(!pending.story&&!pending.dialogue)break;
      if(pending.story){await web.waitForFunction(()=>!storyPlayer.preparing&&!storyPlayer.commandPending);await web.locator('#story-player button[aria-label="跳过剧情"]').click();}
      else await web.evaluate(()=>act('dialogue'));
    }
    await web.evaluate(()=>rpc('save',{mode:2,slot:1}));
    await web.locator('#menu-button').click();await web.locator('#pause-settings').click();
    const stateBefore=await web.evaluate(()=>JSON.stringify({revision:state.revision,round:state.round,units:state.units,factions:state.factions}));
    const populated=await download('in-game.zip');
    check(populated.files.some(f=>f.path.endsWith('/account.json'))&&populated.files.some(f=>f.path.endsWith('/saves/mode-2/slot-1.json')),'real account and manual game slot exported');
    check(stateBefore===await web.evaluate(()=>JSON.stringify({revision:state.revision,round:state.round,units:state.units,factions:state.factions})),'export preserves current game state');
    const denySite=await web.request.post(base+'/api/save-export',{headers:{'Sec-Fetch-Site':'cross-site'}});
    const denyOrigin=await web.request.post(base+'/api/save-export',{headers:{Origin:'https://example.invalid'}});
    check(denySite.status()===403&&denyOrigin.status()===403,'cross-origin export requests rejected');
    await web.route('**/api/save-export',route=>route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({error:'导出测试失败'})}));
    await web.locator('#save-export').click();
    await web.waitForFunction(()=>!document.getElementById('save-export').disabled&&(document.getElementById('notice').querySelector('.original-text')?.getAttribute('aria-label')||document.getElementById('notice').textContent)==='导出测试失败');
    check(await web.locator('#save-export').isEnabled(),'Web error resets control and reports failure');
    await web.unroute('**/api/save-export');await download('retry.zip');
    await web.screenshot({path:path.join(folder,'web-in-game.png')});await web.close();
    check(!report.errors.length,'no browser exceptions');await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
