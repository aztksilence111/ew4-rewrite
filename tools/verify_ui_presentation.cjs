'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56251',folder=path.resolve(process.argv[3]||'build/ui-presentation-20261113/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,errors:[],missing:[],views:[]};
  const check=(condition,label)=>{assert.ok(condition,label);report.checks++;};
  try{
    for(const view of [{width:1280,height:720,touch:false},{width:844,height:390,touch:true},{width:390,height:844,touch:true}]){
      const context=await browser.newContext({viewport:{width:view.width,height:view.height},hasTouch:view.touch,isMobile:view.touch});
      const page=await context.newPage();page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
      const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition,null,{timeout:90000});
      await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.locator('#main-settings').click();check(await page.locator('#save-export').isVisible(),'main menu settings retains save export');await page.locator('#close-settings').click();
      const measure=await page.evaluate(()=>Ew4ScreenLayout.measure({width:844,height:390,touch:true,insets:{left:30,right:20,top:4,bottom:10}}));
      check(measure.cornerInset===6&&measure.menuSafeLeft>0&&measure.menuSafeRight>0,'original controls slightly enlarged with modest safe padding');
      // Skin existing controls, then use their original handlers and restore exact inline styles.
      const originalStyle=await page.locator('#main-menu nav').getAttribute('style');
      await page.evaluate(()=>{window.testSkin=new Ew4UiPresentation.Skin({...manifest,uiPresentation:{schema:1,screens:{main:{controls:{entries:{right:24,y:100},campaign:{image:'btn_common_blue.png',pressedImage:'btn_common_red.png'}},decorations:[{x:20,y:230,w:80,h:6,background:'#88aacc'}]}}}});testSkin.apply();});
      check(await page.locator('#main-menu nav').evaluate(e=>parseFloat(e.style.right)===24&&parseFloat(e.style.top)===100),'configured menu position');
      check(await page.locator('[data-mode="1"]').evaluate(e=>getComputedStyle(e).backgroundImage.includes('btn_common_blue.png')),'configured sprite');
      check(await page.locator('.ui-skin-decoration').evaluate(e=>getComputedStyle(e).pointerEvents==='none'),'decoration cannot intercept taps');
      await page.locator('[data-mode="2"]').click();await page.waitForFunction(()=>!document.getElementById('scenario-menu').hidden);
      await page.evaluate(()=>testSkin.dispose());
      check(await page.locator('#main-menu nav').getAttribute('style')===originalStyle,'skin disposal restores previous layout');
      const scenarioHeader=await page.locator('#scenario-menu>.menu-bar').boundingBox(),load=await page.locator('#continue').boundingBox();
      check(Math.abs(scenarioHeader.y-load.y)<.1&&Math.abs(scenarioHeader.height-load.height)<.1,'top bar and enlarged load button stay flush');
      await page.locator('#scenarios button').first().click();
      await page.waitForFunction(()=>!document.getElementById('countries').hidden);
      const header=await page.locator('#countries>.menu-bar').boundingBox(),back=await page.locator('#cancel-country').boundingBox();
      check(Math.abs(header.y-back.y)<.1&&Math.abs(header.height-back.height)<.1,'top bar and enlarged back button stay flush');
      const row=page.locator('#country-list .country-entry').first(),before=await row.boundingBox();
      await row.click();await page.waitForTimeout(200);const after=await row.boundingBox();
      const stage=await page.locator('#menu-stage').boundingBox();
      check(Math.abs(before.width-after.width)<.1&&Math.abs(before.height-after.height)<.1,'selection keeps original tag size');
      check(after.x<before.x,'selected tag only slides left');
      check(after.x+after.width>=stage.x+stage.width-1,'selected tag covers right edge');
      const menuScale=await page.evaluate(()=>screenLayout.menuScale);
      check(Math.abs(after.width/menuScale-160)<.1&&Math.abs(after.height/menuScale-45)<.1,'country sidebar keeps original logical size');
      check(before.y>=header.y+header.height-.1,'country sidebar starts below enlarged header');
      const flag=await row.locator('.country-flag').boundingBox();
      check((flag.x-after.x)/after.width>=18/160,'flag stays beyond original gray chevron');
      await page.evaluate(()=>{document.documentElement.style.setProperty('--screen-safe-right','20px');sizeUi();});
      const safeRow=await row.boundingBox(),safeStage=await page.locator('#menu-stage').boundingBox();
      check(safeRow.x+safeRow.width>=safeStage.x+safeStage.width-1,'safe inset preserves full bleed tag art');
      await page.evaluate(()=>{document.documentElement.style.removeProperty('--screen-safe-right');sizeUi();});
      if(view.width===844)await page.screenshot({path:path.join(folder,'country-mobile.png')});
      await page.evaluate(async()=>{document.getElementById('countries').hidden=true;await menus.openChapter(catalog.chapters[0]);});
      const campaignHeader=await page.locator('#campaign-screen>.menu-bar').boundingBox(),upgrade=await page.locator('#campaign-upgrade').boundingBox();
      check(Math.abs(campaignHeader.y-upgrade.y)<.1&&Math.abs(campaignHeader.height-upgrade.height)<.1,'campaign upgrade button stays flush with enlarged header');
      const campaignRow=page.locator('#campaign-battles .country-entry[aria-pressed="false"]').first(),campaignBefore=await campaignRow.boundingBox();
      await campaignRow.click();await page.waitForTimeout(200);const campaignAfter=await campaignRow.boundingBox();
      check(Math.abs(campaignBefore.width-campaignAfter.width)<.1&&Math.abs(campaignBefore.height-campaignAfter.height)<.1,'campaign selection keeps tag dimensions');
      check(campaignAfter.x<campaignBefore.x,'campaign tag slides left');
      check(campaignAfter.x+campaignAfter.width>=stage.x+stage.width-1,'campaign tag covers right edge');
      const campaignSize=await campaignRow.evaluate(e=>({width:parseFloat(getComputedStyle(e).width),height:parseFloat(getComputedStyle(e).height)}));
      check(Math.abs(campaignAfter.width/menuScale-campaignSize.width)<.1&&Math.abs(campaignAfter.height/menuScale-campaignSize.height)<.1,'campaign sidebar has no extra enlargement');
      const campaignConfirm=await page.locator('#campaign-confirm').boundingBox(),campaignList=await page.locator('#campaign-battles').boundingBox();
      check(campaignList.y+campaignList.height<=campaignConfirm.y,'campaign confirm has separate space');
      await page.evaluate(()=>start('conquest1','fra'));await idle();
      if(await page.evaluate(()=>stageIntroOpen)){await page.locator('#detail-confirm').click();await idle();}
      for(let i=0;i<15;i++){
        const pending=await page.evaluate(()=>({story:!!state.story,dialogue:!!state.dialogue}));if(!pending.story&&!pending.dialogue)break;
        if(pending.story){await page.waitForFunction(()=>!storyPlayer.preparing&&!storyPlayer.commandPending);await page.locator('#story-player button[aria-label="跳过剧情"]').click();await page.getByRole('button',{name:'确认跳过',exact:true}).click();}
        else await page.evaluate(()=>act('dialogue'));await idle();
      }
      const bounds={};for(const id of ['menu-button','end-turn','undo-move','next-unit'])bounds[id]=await page.locator('#'+id).boundingBox();
      for(const [id,b] of Object.entries(bounds)){
        const inset=view.touch?5:3;
        check(b.x>=inset&&b.y>=inset&&b.x+b.width<=view.width-inset&&b.y+b.height<=view.height-inset,'button stays inside screen '+id);
        check(b.width>=35&&b.height>=35,'button touch size '+id);
      }
      await page.evaluate(()=>{document.documentElement.style.setProperty('--screen-safe-right','20px');sizeUi();});
      const safePause=await page.locator('#menu-button').boundingBox();
      check(safePause.x+safePause.width<=view.width-(view.touch?25:23),'battle buttons respect simulated safe inset');
      await page.evaluate(()=>{document.documentElement.style.removeProperty('--screen-safe-right');sizeUi();});
      await page.locator('#menu-button').click();await page.locator('#pause-settings').click();
      check(await page.locator('#save-export').isVisible(),'pause settings retains save export');
      await page.locator('#close-settings').click();
      if(await page.locator('#resume').isVisible())await page.locator('#resume').click();
      await page.evaluate(()=>{
        const uiPresentation={schema:1,screens:{battle:{controls:{pause:{right:20,y:20,w:28,h:28,image:'button_pause.png'}}},settings:{panel:{background:'#303840'}}}};
        window.testSkin=new Ew4UiPresentation.Skin({...manifest,uiPresentation});testSkin.apply();
      });
      await page.locator('#menu-button').click();await page.locator('#pause-settings').click();
      check(await page.locator('#save-export').isVisible(),'skinned pause keeps original click actions');
      await page.locator('#close-settings').click();if(await page.locator('#resume').isVisible())await page.locator('#resume').click();
      await page.evaluate(()=>testSkin.dispose());
      // Exercise an actual native tmp_button and its pressed border, not a parallel DOM skin button.
      await page.evaluate(()=>{
        const host=document.createElement('div');host.id='native-skin-check';Object.assign(host.style,{position:'fixed',left:'100px',top:'100px',width:'100px',height:'50px',zIndex:20000});document.body.append(host);
        const api={manifest,button:(label,action,parent)=>{const b=document.createElement('button');b.textContent=label;b.onclick=action;parent.append(b);return b;},text:value=>Object.assign(document.createElement('span'),{textContent:value}),notice:message=>{throw Error(message);}};
        const definition={attributes:{w:100,h:50},children:[{attributes:{id:'test_action',type:'tmp_button',frm1:'btn_common_red.png',frm2:'btn_common_blue.png',x:0,y:0,w:80,h:35,text:'测试'},children:[]}]};
        window.nativeSkinClicks=0;window.nativeSkinLayout=new Ew4NativeUI.Layout(api,'test',null,{body:host,definition,current:()=>true});nativeSkinLayout.action('test_action','测试',()=>{nativeSkinClicks++;});
      });
      const nativeButton=page.locator('#native-skin-check button');
      check(await nativeButton.evaluate(e=>getComputedStyle(e).borderImageSource.includes('btn_common_red.png')),'native normal border art');
      const nativeBox=await nativeButton.boundingBox();await page.mouse.move(nativeBox.x+10,nativeBox.y+10);await page.mouse.down();
      check(await nativeButton.evaluate(e=>getComputedStyle(e).borderImageSource.includes('btn_common_blue.png')),'native pressed border art');
      await page.mouse.up();check(await page.evaluate(()=>nativeSkinClicks===1),'native action runs once');
      await page.locator('#native-skin-check').evaluate(e=>e.remove());
      if(view.width===844)await page.screenshot({path:path.join(folder,'battle-mobile.png')});
      report.views.push({view,bounds});await context.close();
    }
    check(report.errors.length===0,'no browser errors '+JSON.stringify(report.errors));
    check(report.missing.length===0,'no missing resources '+JSON.stringify(report.missing));
  }finally{await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,views:report.views.length}));
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
