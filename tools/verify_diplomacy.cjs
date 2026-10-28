'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5592',folder=path.resolve(process.argv[3]||'build/diplomacy-20261028/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  const report={checks:0,errors:[],missing:[],views:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  const idle=()=>page.waitForFunction(()=>!busy&&!aiRunning&&!sessionTransition,null,{timeout:90000});
  const resultText=()=>page.locator('.diplomacy-result').evaluate(el=>el.textContent+[...el.querySelectorAll('[aria-label]')].map(n=>n.getAttribute('aria-label')).join(''));
  async function start(){
    await page.evaluate(()=>start('conquest1','fra'));await idle();
    if(await page.evaluate(()=>stageIntroOpen)){await page.locator('#detail-confirm').click();await idle();}
    for(let i=0;i<12;i++){
      const pending=await page.evaluate(()=>({story:!!state.story,dialogue:!!state.dialogue}));if(!pending.story&&!pending.dialogue)break;
      if(pending.story){await page.waitForFunction(()=>!storyPlayer.preparing&&!storyPlayer.commandPending);await page.locator('#story-player button[aria-label="跳过剧情"]').click();}
      else await page.evaluate(()=>act('dialogue'));
      await idle();
    }
  }
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});await start();
    check(await page.locator('#open-diplomacy').isVisible(),'conquest exposes entry');
    const entry=await page.locator('#open-diplomacy').boundingBox(),undo=await page.locator('#undo-move').boundingBox();
    check(Math.abs(entry.x-undo.x)<1&&entry.y+entry.height<=undo.y-4,'entry above undo and aligned left');
    check(await page.locator('.diplomacy-entry-face').evaluate(el=>getComputedStyle(el).backgroundImage.includes('/diplomacy/button.png')&&el.firstElementChild.complete&&el.firstElementChild.naturalWidth>0),'gray handshake available before first opening');
    await page.screenshot({path:path.join(folder,'entry.png')});
    await page.locator('#open-diplomacy').click();await page.waitForFunction(()=>diplomacyUi.snapshot&&!diplomacyUi.dialog.hasAttribute('aria-busy'));
    check(await page.locator('.diplomacy-row').count()===await page.evaluate(()=>state.factions.length-1),'every other faction in list');
    check(await page.locator('.diplomacy-row[data-relation="allied"] button').first().getAttribute('data-op')==='declare_war','ally offers war');
    check(await page.locator('.diplomacy-row[data-relation="neutral"] button').first().getAttribute('data-op')==='declare_war','neutral offers war');
    check(await page.locator('.diplomacy-row[data-relation="hostile"] button').first().isDisabled(),'unoccupied enemy demand disabled');
    check(await page.locator('.diplomacy-row:not([data-relation="allied"]) .diplomacy-resource').count()===0,'enemy and neutral resource values absent');
    check(await page.locator('.diplomacy-player .diplomacy-resource').count()===3&&await page.locator('.diplomacy-row[data-relation="allied"]').count()*3===await page.locator('.diplomacy-row[data-relation="allied"] .diplomacy-resource').count(),'own and allied resources visible');
    const buttonGeometry=await page.locator('.diplomacy-action').first().evaluate(el=>({hit:el.getBoundingClientRect().width,face:el.firstElementChild.getBoundingClientRect().width}));
    check(buttonGeometry.hit>=44&&buttonGeometry.face===30,'small 30px face retains 44px touch target');
    check(await page.locator('.diplomacy-dialog').evaluate(el=>getComputedStyle(el).borderImageSource==='none'&&getComputedStyle(el).borderTopWidth==='1px'&&getComputedStyle(el).backgroundImage==='none'),'dialog has thin gray border without white frame or stretched background');
    const validImages=await page.locator('#diplomacy-modal img').evaluateAll(imgs=>imgs.every(i=>i.complete&&i.naturalWidth>0));check(validImages,'flags resources and icons loaded');
    check(await page.locator('.diplomacy-row .diplomacy-capital').count()===await page.locator('.diplomacy-row').count(),'capital details rendered for every faction');
    const lootMessages=await page.evaluate(()=>{
      const messages=[],original=diplomacyUi.api.notice;diplomacyUi.api.notice=text=>messages.push(text);
      try{
        const target=state.factions.find(f=>f.id!==state.playerFaction).id;
        const notices=[{kind:'capital.captured',subject:target,target:state.playerFaction,value:20},
          ...['money','industry','food'].map((r,i)=>({kind:'capital.loot.'+r,subject:state.playerFaction,target,value:[200,100,9][i]}))];
        diplomacyUi.showCapitalNotices({...state,notices});
        diplomacyUi.showCapitalNotices({...state,notices:notices.map(n=>({...n,
          subject:n.kind==='capital.captured'?state.playerFaction:target,
          target:n.kind==='capital.captured'?target:state.playerFaction}))});
      }finally{diplomacyUi.api.notice=original;}
      return messages;
    });
    check(lootMessages.length===2&&lootMessages[0].includes('缴获金币200')&&lootMessages[0].includes('工业100')
      &&lootMessages[1].includes('首都失陷，损失金币200'),'capital capture and loss messages shown to player');
    const before=await page.evaluate(()=>JSON.stringify({revision:state.revision,round:state.round,factions:state.factions}));
    const allyButton=page.locator('.diplomacy-row[data-relation="allied"] button').first(),ally=await allyButton.getAttribute('data-target');
    await allyButton.click();check(await page.locator('.diplomacy-confirm').isVisible(),'war requires confirmation');
    await page.locator('.diplomacy-confirm-actions button').first().click();
    check(before===await page.evaluate(()=>JSON.stringify({revision:state.revision,round:state.round,factions:state.factions})),'cancel has no state effects');
    await page.locator('.diplomacy-row button[data-target="'+ally+'"]').click();await page.locator('.diplomacy-confirm-actions button').last().click();await idle();await page.waitForFunction(()=>!diplomacyUi.submitting);
    check(await page.evaluate(id=>state.factions.find(f=>f.id===id).relation==='hostile',ally),'confirmed ally declaration changes relationship');
    check(await page.locator('.diplomacy-result').isVisible(),'result visible');
    const neutralButton=page.locator('.diplomacy-row[data-relation="neutral"] button').first(),neutral=await neutralButton.getAttribute('data-target');
    await neutralButton.click();await page.locator('.diplomacy-confirm-actions button').last().click();await idle();await page.waitForFunction(()=>!diplomacyUi.submitting);
    check(await page.evaluate(id=>state.factions.find(f=>f.id===id).relation==='hostile',neutral),'confirmed neutral declaration changes relationship');
    check(await page.evaluate(()=>state.round===1),'declarations do not end turn');
    const declared=await page.evaluate(()=>({revision:state.revision,factions:state.factions.map(f=>[f.id,f.relation])}));
    await page.evaluate(()=>rpc('save',{slot:1,mode:2}));await page.evaluate(()=>diplomacyUi.close(false));await page.evaluate(()=>load(1,2));await idle();
    check(JSON.stringify(declared.factions)===await page.evaluate(()=>JSON.stringify(state.factions.map(f=>[f.id,f.relation]))),'declarations survive actual save/load');
    await page.locator('#open-diplomacy').click();await page.waitForFunction(()=>diplomacyUi.snapshot);
    for(const [w,h] of [[1280,720],[844,390],[667,375],[390,844],[320,568]]){
      await page.setViewportSize({width:w,height:h});await page.waitForTimeout(180);
      const geometry=await page.evaluate(()=>{
        const d=diplomacyUi.dialog.getBoundingClientRect(),list=diplomacyUi.list.getBoundingClientRect();
        const rows=[...document.querySelectorAll('.diplomacy-row')],visible=rows.filter(r=>{const b=r.getBoundingClientRect();return b.bottom>list.top&&b.top<list.bottom;});
        const overlap=visible.some(r=>{const a=r.children[0].getBoundingClientRect(),b=r.children[1].getBoundingClientRect(),c=r.children[2].getBoundingClientRect();return a.right>b.left+1||b.right>c.left+1||c.right>d.right+1;});
        return {x:d.x,y:d.y,right:d.right,bottom:d.bottom,scrollWidth:diplomacyUi.dialog.scrollWidth,clientWidth:diplomacyUi.dialog.clientWidth,overlap,rows:visible.length};
      });
      await page.screenshot({path:path.join(folder,'diplomacy-'+w+'x'+h+'.png')});report.views.push({w,h,...geometry});
      check(geometry.x>=0&&geometry.y>=0&&geometry.right<=w+1&&geometry.bottom<=h+1,'dialog fits '+w+'x'+h+': '+JSON.stringify(geometry));
      check(!geometry.overlap&&geometry.scrollWidth<=geometry.clientWidth+1,'columns fit '+w+'x'+h+': '+JSON.stringify(geometry));
      await page.locator('.diplomacy-list').evaluate(el=>el.scrollTop=el.scrollHeight);
      const contentFits=await page.locator('.diplomacy-row').evaluateAll(rows=>rows.every(row=>{
        const details=row.querySelector('.diplomacy-details'),box=details.getBoundingClientRect();
        const lines=[...details.children].filter(el=>!el.hidden&&el.getClientRects().length).map(el=>el.getBoundingClientRect());
        return lines.every((line,i)=>line.top>=box.top-1&&line.bottom<=box.bottom+1&&(!i||line.top>=lines[i-1].bottom-1));
      }));
      check(contentFits,'allied resources and capital lines fit '+w+'x'+h);
      await page.screenshot({path:path.join(folder,'allies-'+w+'x'+h+'.png')});
    }
    await page.setViewportSize({width:1280,height:720});
    await page.waitForFunction(()=>Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--screen-safe-width'))>1000);
    const custom=await page.evaluate(async()=>{const p=diplomacyUi.snapshot.presentation;window.diplomacyDefault=p;diplomacyUi.snapshot.presentation={...p,title:'使节',width:400,rowHeight:96,textColor:'#183d28',entryGap:14};diplomacyUi.render();await Ew4OriginalUI.prepareText('使节');return {width:diplomacyUi.dialog.offsetWidth,color:getComputedStyle(diplomacyUi.dialog).color};});
    check(custom.width===400&&custom.color==='rgb(24, 61, 40)','Mod presentation changes applied: '+JSON.stringify(custom));
    await page.evaluate(()=>{diplomacyUi.snapshot.presentation=window.diplomacyDefault;diplomacyUi.render();});
    await page.keyboard.press('Escape');check(await page.locator('#diplomacy-modal').isHidden(),'escape closes diplomacy');
    await page.evaluate(()=>load(2,0));await idle();await page.locator('#open-diplomacy').click();await page.waitForFunction(()=>diplomacyUi.snapshot);
    const available=page.locator('.diplomacy-action[data-op="demand_surrender"]:enabled').first(),target=await available.getAttribute('data-target');
    await available.click();check(await page.locator('.diplomacy-confirm').isVisible(),'demand confirmation opens');
    check((await page.locator('.diplomacy-confirm').evaluate(el=>el.textContent+[...el.querySelectorAll('[aria-label]')].map(n=>n.ariaLabel).join(''))).includes('重叠核心'),'confirmation exposes core and pair probability details');
    await page.locator('.diplomacy-confirm-actions button').last().click();await idle();await page.waitForFunction(()=>!diplomacyUi.submitting);
    check((await resultText()).includes('拒绝'),'failed demand result');
    check(await page.locator('.diplomacy-action[data-target="'+target+'"]').isDisabled(),'failed demand locks action');
    await page.evaluate(()=>diplomacyUi.close(false));await page.evaluate(()=>load(0,2));await idle();await page.locator('#open-diplomacy').click();await page.waitForFunction(()=>diplomacyUi.snapshot);
    check(await page.locator('.diplomacy-action[data-target="'+target+'"]').isDisabled(),'failure lock survives actual autosave reload');
    await page.evaluate(()=>diplomacyUi.close(false));await page.evaluate(()=>load(3,0));await idle();await page.locator('#open-diplomacy').click();await page.waitForFunction(()=>diplomacyUi.snapshot);
    await page.locator('.diplomacy-action[data-target="'+target+'"]').click();await page.locator('.diplomacy-confirm-actions button').last().click();await idle();await page.waitForFunction(()=>!diplomacyUi.submitting);
    check(await page.evaluate(id=>state.factions.find(f=>f.id===id).defeated,target),'successful demand defeats country');
    check((await resultText()).includes('接受'),'successful demand result');
    await page.evaluate(()=>diplomacyUi.close(false));await page.evaluate(()=>load(4,0));await idle();await page.locator('#open-diplomacy').click();await page.waitForFunction(()=>diplomacyUi.snapshot);
    check(await page.locator('.diplomacy-action[data-op="demand_surrender"]:enabled').count()===0,'legacy save does not fabricate baseline');
    await page.evaluate(()=>diplomacyUi.close(false));
    await page.evaluate(()=>start(catalog.scenarios.find(s=>s.mode===1).id));await idle();
    check(await page.locator('#open-diplomacy').isHidden(),'campaign hides entry');
    const denied=await page.evaluate(async()=>{const r=await fetch('/api/diplomacy',{method:'POST',body:'{}'});return r.json();});check(!denied.ok&&denied.code==='diplomacy.mode','campaign API denied');
    const touchContext=await browser.newContext({viewport:{width:844,height:390},hasTouch:true,isMobile:true});
    const touch=await touchContext.newPage();await touch.goto(base);await touch.waitForFunction(()=>startupReady,null,{timeout:90000});
    await touch.evaluate(()=>load(2,0));await touch.waitForFunction(()=>!busy&&!sessionTransition);
    await touch.locator('#open-diplomacy').tap();await touch.waitForFunction(()=>diplomacyUi.snapshot);
    await touch.locator('.diplomacy-action[data-op="demand_surrender"]:enabled').first().tap();
    check(await touch.locator('.diplomacy-confirm').isVisible(),'touch opens small demand button');
    await touch.locator('.diplomacy-confirm-actions button').first().tap();
    await touch.screenshot({path:path.join(folder,'touch-844x390.png')});
    check(await touch.locator('.diplomacy-list').isVisible(),'touch cancels demand');
    await touch.locator('.diplomacy-dialog .close-icon').tap();check(await touch.locator('#diplomacy-modal').isHidden(),'touch closes dialog');await touchContext.close();
    check(report.errors.length===0,'no script errors '+JSON.stringify(report.errors));check(report.missing.length===0,'no missing assets '+JSON.stringify(report.missing));
  }finally{await fs.writeFile(path.join(folder,'report.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exit(1);});
