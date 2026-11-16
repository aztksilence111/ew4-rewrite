'use strict';
const assert=require('node:assert/strict'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
 let checks=0;const errors=[];const check=(v,label)=>{assert.ok(v,label);checks++;};
 try{
  for(const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
   const page=await browser.newPage({viewport:size});page.on('pageerror',e=>errors.push(e.message));
   await page.setContent('<!doctype html><html><body></body></html>');
   await page.addStyleTag({path:path.resolve('rewrite/Ew4.Web/wwwroot/story-player.css')});
   await page.addStyleTag({content:'[hidden]{display:none!important}button{font:inherit}'});
   await page.addScriptTag({path:path.resolve('rewrite/Ew4.Web/wwwroot/story-player.js')});
   await page.evaluate(()=>{
    window.calls=[];window.player=new Ew4StoryUI.Player({prepare:async()=>{},notice:message=>{throw Error(message);},act:async(op,args)=>{calls.push({op,...args});}});
    player.preferences.auto=false;player.preferences.typewriter=false;player.preferences.skipOpening=false;
    window.scene=id=>({id,scene:id,node:'first',kind:'event',text:'通用Mod剧情测试。',name:'',actors:[],animations:[],choices:[],history:[],
      theme:{panel:'#10191b',text:'#f2eee3',accent:'#d6bd85',opacity:.9,dialoguePercent:30,fontSize:24}});
   });
   for(const id of ['sample.other-mod','sample.second-mod']){
    await page.evaluate(async id=>{player.reset();calls.length=0;await player.sync(scene(id));},id);
    await page.getByRole('button',{name:'跳过剧情',exact:true}).click();
    const dialog=page.getByRole('dialog',{name:'确认跳过剧情'});check(await dialog.isVisible(),'generic confirmation '+id);
    check(await page.evaluate(()=>calls.length===0&&player.story.node==='first'),'open does not send story_skip');
    check(await page.getByRole('button',{name:'继续阅读',exact:true}).evaluate(el=>el===document.activeElement),'safe default focus');
    const b=await dialog.boundingBox();check(b.x>=0&&b.y>=0&&b.x+b.width<=size.width+1&&b.y+b.height<=size.height+1,'dialog fits viewport');
    await page.getByRole('button',{name:'继续阅读',exact:true}).click();
    check(await page.evaluate(()=>!player.overlay&&calls.length===0&&player.story.node==='first'),'cancel preserves node');
    await page.getByRole('button',{name:'跳过剧情',exact:true}).click();await page.keyboard.press('Escape');
    check(await dialog.count()===0&&await page.evaluate(()=>calls.length===0),'Escape cancels without effects');
    await page.getByRole('button',{name:'跳过剧情',exact:true}).click();await page.evaluate(()=>player.back());
    check(await dialog.count()===0&&await page.evaluate(()=>calls.length===0),'host back cancels');
    await page.getByRole('button',{name:'跳过剧情',exact:true}).click();
    await page.evaluate(()=>{player.preferences.auto=true;player.schedule();});await page.waitForTimeout(1100);
    check(await page.evaluate(()=>calls.length===0&&player.story.node==='first'),'autoplay suspended while confirming');
    await page.evaluate(()=>{player.preferences.auto=false;player.schedule();});
    await page.getByRole('button',{name:'确认跳过',exact:true}).click();
    check(await page.evaluate(id=>calls.length===1&&calls[0].op==='story_skip'&&calls[0].offer===id&&!player.overlay,id),'confirm sends one skip for correct story');
   }
   await page.close();
  }
  check(errors.length===0,'no generic player errors');console.log(JSON.stringify({checks,errors,viewports:3,mods:2}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
