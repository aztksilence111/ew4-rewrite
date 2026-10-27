'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5587',output=path.resolve(process.argv[3]||'build/story-browser-20261027');
(async()=>{
  await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  const context=await browser.newContext({viewport:{width:1280,height:720},hasTouch:true});
  const page=await context.newPage(),errors=[],missing=[];let checks=0;
  page.on('pageerror',e=>errors.push(e.message));page.on('response',r=>{if(r.status()>=400)missing.push(r.url());});
  const check=(pass,label)=>{assert.ok(pass,label);checks++;};
  const ready=()=>page.waitForFunction(()=>!storyPlayer.host.hidden&&!storyPlayer.preparing&&!storyPlayer.waiting&&!busy);
  const node=()=>page.evaluate(()=>state.story?.node);
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady,{},{timeout:90000});
    await page.evaluate(()=>{storyPlayer.preferences.auto=false;storyPlayer.preferences.typewriter=false;storyPlayer.preferences.skipOpening=false;storyPlayer.persist();});
    await page.evaluate(()=>{window.storyFrames=0;const frame=battlefield.frame;battlefield.frame=function(...args){storyFrames++;return frame.apply(this,args);};});
    if(process.argv.includes('--marker-only')){
      for(const [name,width,height] of [['desktop',1280,720],['phone',844,390]]){
        await page.setViewportSize({width,height});await page.evaluate(()=>start('conquest1','fra',null));await ready();
        check(await page.locator('.story-continue').evaluate(el=>!el.textContent&&getComputedStyle(el).webkitMaskImage.includes('play.svg')),name+' triangular continue marker');
        check(await page.getByRole('button',{name:'自动播放',exact:true}).textContent()==='自动',name+' automatic playback label');
        await page.screenshot({path:path.join(output,name+'-marker.png')});
      }
      check(!errors.length&&!missing.length,'no errors or missing icons');
      console.log(JSON.stringify({checks,errors:errors.length,missing:missing.length,viewports:2}));return;
    }
    for(const [name,width,height] of [['desktop',1280,720],['phone',844,390],['portrait',390,844],['small',320,568],['wide',2560,1080]]){
      await page.setViewportSize({width,height});
      await page.evaluate(()=>showMain());
      await page.evaluate(()=>start('conquest1','fra',null));await ready();
      check(await node()==='arrival',name+' starts opening');
      await page.evaluate(()=>{window.storyBattleBefore={cells:state.cells,units:state.units,factions:state.factions,round:state.round};window.storyFrames=0;window.storyLayoutReads=0;
        const rect=storyPlayer.text.getBoundingClientRect.bind(storyPlayer.text);storyPlayer.text.getBoundingClientRect=()=>{storyLayoutReads++;return rect();};});
      const response=page.waitForResponse(r=>r.url().includes('/api/action')&&r.url().includes('story_next'));
      await page.locator('.story-dialogue').click();await ready();
      const payload=(await (await response).json()).data;
      check(payload.presentationOnly&&!('cells' in payload)&&!('units' in payload),name+' dialogue response omits battle projection');
      check(await page.evaluate(async()=>{requestRender();await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
        return state.cells===storyBattleBefore.cells&&state.units===storyBattleBefore.units&&state.factions===storyBattleBefore.factions&&state.round===storyBattleBefore.round&&storyFrames===0;
      }),name+' dialogue preserves battle and skips battlefield drawing');
      check(await page.evaluate(()=>storyLayoutReads===0),name+' dialogue reuses fixed geometry');
      check(await node()==='briefing',name+' one click advances');
      await page.screenshot({path:path.join(output,name+'-dialogue.png')});
      const layout=await page.evaluate(()=>{
        const rect=el=>{const r=el.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom};};
        return {panel:rect(storyPlayer.dialogue),textFits:storyPlayer.text.scrollHeight<=storyPlayer.text.clientHeight+1,
          left:rect(document.querySelector('.story-tools-left')),right:rect(document.querySelector('.story-tools-right')),
          images:Array.from(storyPlayer.cast.querySelectorAll('img')).every(i=>i.complete&&i.naturalWidth>0),actors:storyPlayer.cast.children.length};
      });
      check(layout.panel.left>=0&&layout.panel.right<=width&&layout.panel.bottom<=height,name+' panel contained');
      check(layout.panel.left===0&&layout.panel.right===width&&layout.panel.bottom===height,name+' dialogue flush with sides and bottom');
      check(layout.textFits&&layout.images&&layout.actors===2,name+' text and two actors ready');
      check(layout.left.right<=layout.right.left,name+' toolbars do not overlap');
      check(await page.getByRole('button',{name:'自动播放',exact:true}).textContent()==='自动',name+' automatic playback has visible label');
      await page.evaluate(()=>{window.storyActorBefore=storyPlayer.cast.firstElementChild;});
      await page.locator('.story-dialogue').click();await ready();
      check(await node()==='reply',name+' speaker changes');
      check(await page.evaluate(()=>window.storyActorBefore===storyPlayer.cast.firstElementChild),name+' unchanged actor reused');
      check(await page.evaluate(()=>document.querySelector('.story-actor.speaking').dataset.actor)==='b',name+' speaking actor highlighted');
      await page.getByRole('button',{name:'对话历史',exact:true}).click();
      check(await page.locator('.story-history-row').count()===3,name+' history includes prior text');
      await page.getByRole('button',{name:'关闭',exact:true}).click();
      await page.getByRole('button',{name:'剧情设置',exact:true}).click();
      await page.getByLabel('剧情字号').selectOption('large');
      await page.getByRole('button',{name:'关闭',exact:true}).click();
      check(await page.evaluate(()=>storyPlayer.text.scrollHeight<=storyPlayer.text.clientHeight+1),name+' large text fits');
      await page.getByRole('button',{name:'隐藏界面',exact:true}).click();
      check(await page.locator('#story-player').evaluate(el=>el.classList.contains('story-text-hidden')),name+' interface hidden');
      await page.locator('#story-player').click({position:{x:width/2,y:height/2}});
      check(await node()==='reply',name+' restoring interface does not advance');
      await page.getByRole('button',{name:'跳过剧情',exact:true}).click();await ready();
      for(let guard=0;guard<30&&await page.evaluate(()=>!state.story);guard++)await page.evaluate(()=>act('dialogue'));
      await ready();
      check(await page.evaluate(()=>state.story.scene)==='sample.council',name+' event result invokes council');
      await page.getByRole('button',{name:'跳过剧情',exact:true}).click();await ready();
      check(await node()==='decision',name+' skip stops at choices');
      await page.screenshot({path:path.join(output,name+'-choices.png')});
      check(await page.locator('.story-choice').count()===3,name+' three choices');
      const choiceBounds=await page.locator('.story-choice-list').boundingBox(),panelBounds=await page.locator('.story-dialogue').boundingBox();
      check(choiceBounds.y+choiceBounds.height<=panelBounds.y,name+' choices do not cover dialogue');
      await page.getByRole('button',{name:'播放倍速',exact:true}).click();
      const speedText=await page.getByRole('button',{name:'播放倍速',exact:true}).textContent();
      check(speedText==='2×',name+' speed changes independently: '+speedText);
      await page.getByRole('button',{name:'自动播放',exact:true}).click();
      await page.waitForTimeout(650);check(await node()==='decision',name+' auto cannot choose');
      await page.getByRole('button',{name:'自动播放',exact:true}).click();
      await page.locator('[data-choice="negotiate"]').click();
      await page.waitForFunction(()=>state.story?.kind==='ending');await ready();
      check(await page.evaluate(()=>state.phase==='Finished'&&!state.battleResult),name+' ending before settlement');
      check(await page.locator('.victory-result:visible').count()===0,name+' result window absent during ending');
      await page.getByRole('button',{name:'跳过剧情',exact:true}).click();
      await page.waitForFunction(()=>state.battleResult&&!state.story);
      await page.waitForSelector('.victory-result',{timeout:15000});
      check(true,name+' result displayed after ending');
      await page.evaluate(()=>{storyPlayer.preferences.speed=1;storyPlayer.preferences.font='medium';storyPlayer.persist();});
    }
    // Stress wrapping and pagination without applying any extra game effects.
    await page.evaluate(async()=>{const sample={...catalog, id:'layout',node:'long',kind:'event',name:'很长的人物姓名用于检查换行',speaker:null,background:null,actors:[],animations:[],history:[],waitMs:0,
      theme:{panel:'#0b1013',text:'#f2f1e9',accent:'#dac690',opacity:.88,dialoguePercent:26,fontSize:24},
      text:'这是一段用于检查长台词分页的文字。'.repeat(100),choices:[]};await storyPlayer.sync(sample,false,false);});
    check(await page.evaluate(()=>storyPlayer.pages.length>1&&storyPlayer.text.scrollHeight<=storyPlayer.text.clientHeight+1),'long text paginates');
    await page.evaluate(()=>storyPlayer.sync(null));
    check(errors.length===0,'no browser exceptions: '+errors.join('; '));check(missing.length===0,'no missing resources: '+missing.join('; '));
    console.log(JSON.stringify({checks,errors:errors.length,missing:missing.length,viewports:5}));
  }finally{await browser.close();}
})().catch(error=>{console.error(error.message);process.exitCode=1;});
