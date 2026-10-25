'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {spawn}=require('node:child_process'),WebSocket=require('ws');
const executable=process.argv[2],base=process.argv[3]||'http://127.0.0.1:5483';
const folder=path.resolve(process.argv[4]||'build/android70-20261023/browser'),port=Number(process.env.EW4_CDP_PORT||9485);
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const child=spawn(executable,['--headless','--mute-audio','--no-first-run','--no-default-browser-check',`--remote-debugging-port=${port}`,`--user-data-dir=${folder}/profile`,'about:blank'],{stdio:'ignore',windowsHide:true});
  let socket,serial=0;const pending=new Map(),errors=[],checks=[];
  const check=(value,label)=>{assert.ok(value,label);checks.push(label);};
  const send=(method,params={})=>new Promise((resolve,reject)=>{
    const id=++serial;pending.set(id,{resolve,reject});socket.send(JSON.stringify({id,method,params}));
  });
  const evaluate=async expression=>{
    const reply=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});
    if(reply.exceptionDetails)throw Error(JSON.stringify(reply.exceptionDetails));
    return reply.result.value;
  };
  const until=async(expression)=>{
    const end=Date.now()+90000;let value;
    while(Date.now()<end){try{value=await evaluate(expression);if(value)return value;}catch(_){}await sleep(100);}
    throw Error('Timed out: '+expression+'; '+JSON.stringify(errors));
  };
  const screenshot=async name=>{const {data}=await send('Page.captureScreenshot',{format:'png'});await fs.writeFile(path.join(folder,name+'.png'),Buffer.from(data,'base64'));};
  const click=async selector=>{
    await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    const p=await evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};})()`);
    await send('Input.dispatchMouseEvent',{type:'mousePressed',...p,button:'left',clickCount:1});
    await send('Input.dispatchMouseEvent',{type:'mouseReleased',...p,button:'left',clickCount:1});
  };
  let version;
  try{
    let targets;
    for(let i=0;i<100;i++){try{targets=await (await fetch(`http://127.0.0.1:${port}/json`)).json();break;}catch(_){await sleep(100);}}
    assert.ok(targets?.length,'browser exposes CDP');
    const target=targets.find(target=>target.type==='page'&&target.url==='about:blank')||targets.find(target=>target.type==='page');
    socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise(resolve=>socket.once('open',resolve));
    socket.on('message',data=>{
      const message=JSON.parse(data);
      if(message.id){const task=pending.get(message.id);pending.delete(message.id);if(message.error)task.reject(Error(message.error.message));else task.resolve(message.result);}
      if(message.method==='Runtime.exceptionThrown')errors.push(message.params.exceptionDetails.exception?.description||message.params.exceptionDetails.text);
    });
    await send('Page.enable');await send('Runtime.enable');
    version=await evaluate('navigator.userAgent');
    console.log(JSON.stringify({engine:version}));
    for(const [name,width,height,query] of [['landscape',844,390,''],['portrait-canvas',390,844,'?renderer=canvas'],['desktop',1280,720,''],['no-webgl',844,390,''],['failed-webgl',844,390,'']]){
      let injection;
      if(name==='no-webgl'||name==='failed-webgl')injection=(await send('Page.addScriptToEvaluateOnNewDocument',{source:`(()=>{const get=HTMLCanvasElement.prototype.getContext;HTMLCanvasElement.prototype.getContext=function(type,...args){if(type.indexOf('webgl')>=0){if(${JSON.stringify(name)}==='no-webgl')return null;if(this.id==='map'){get.call(this,type,...args);throw Error('injected GPU initialization failure');}}return get.call(this,type,...args);};})()`})).identifier;
      await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<1000});
      await send('Page.navigate',{url:base+'/'+query});
      await until('typeof startupReady!=="undefined"&&startupReady');
      check(await evaluate('Ew4FrontendTarget===70&&document.getElementById("startup-status").hidden'),name+' compatible startup');
      const dimensions=await evaluate(`(()=>{const ids=['main-menu','headquarters'];return ids.map(id=>{const r=document.getElementById(id).getBoundingClientRect();return {id,x:r.x,y:r.y,w:r.width,h:r.height};});})()`);
      check(dimensions.every(r=>r.w>0&&r.h>0&&r.x>=-1&&r.y>=-1&&r.x+r.w<=width+1&&r.y+r.h<=height+1),name+' menu fits viewport');
      check(await evaluate('(()=>{const r=document.getElementById("menu-stage").getBoundingClientRect();return Math.abs(r.left)<.1&&Math.abs(r.top)<.1&&Math.abs(r.right-innerWidth)<.1&&Math.abs(r.bottom-innerHeight)<.1&&Array.from(document.querySelectorAll(".main-entry")).every(b=>Math.abs(b.getBoundingClientRect().right-innerWidth)<.1);})()'),name+' menu and buttons reach actual screen edges');
      const labels=await evaluate('Array.from(document.querySelectorAll(".main-entry")).map(button=>{const a=button.getBoundingClientRect(),b=button.firstElementChild.getBoundingClientRect();return {button:[a.left,a.top,a.right,a.bottom],label:[b.left,b.top,b.right,b.bottom],inside:b.left>=a.left-1&&b.right<=a.right+1&&b.top>=a.top-1&&b.bottom<=a.bottom+1};})');
      check(labels.every(x=>x.inside),name+' labels stay inside buttons: '+JSON.stringify(labels));
      await screenshot(name+'-menu');
      await click('#main-settings');
      check(await evaluate('!document.getElementById("settings").hidden'),name+' settings opens');
      await screenshot(name+'-settings');await click('#close-settings');
      check(await evaluate('document.getElementById("settings").hidden'),name+' settings closes');
      await until('!document.getElementById("mod-open").hidden');await click('#mod-open');
      check(await evaluate('!document.getElementById("mod-manager").hidden'),name+' Mod manager opens');
      await screenshot(name+'-mods');await click('#mod-close');
      check(await evaluate('document.getElementById("mod-manager").hidden'),name+' Mod manager closes');
      await click('#headquarters');await until('document.querySelectorAll(".commander-card").length>0');
      check(await evaluate('!document.getElementById("hq-screen").hidden'),name+' headquarters');await screenshot(name+'-hq');
      await click('#hq-back');await until('!sessionTransition&&!document.getElementById("main-menu").hidden');await click('[data-mode="2"]');
      await until('document.querySelectorAll(".scenario-entry").length>0');
      check(true,name+' conquest menu');
      await evaluate('(async()=>{await chooseScenario(catalog.scenarios.find(s=>s.id==="conquest1"));})()');
      await until('!busy');
      check(await evaluate('(()=>{const bar=document.querySelector("#countries>.menu-bar").getBoundingClientRect(),back=document.getElementById("cancel-country").getBoundingClientRect(),list=document.getElementById("country-list").getBoundingClientRect();return Math.abs(bar.left)<.1&&Math.abs(bar.top)<.1&&Math.abs(bar.right-innerWidth)<.1&&Math.abs(back.left)<.1&&Math.abs(list.right-innerWidth)<.1;})()'),name+' country header and sidebar reach actual screen edges');
      await evaluate('showMain()');await until('!sessionTransition');await click('[data-mode="1"]');
      await until('document.querySelectorAll(".chapter-entry").length>0');
      await click('.chapter-entry');await until('!!document.querySelector(".chapter-preview")');
      await evaluate('new Promise(resolve=>setTimeout(resolve,250))');await click('.chapter-preview-confirm');
      await until('!document.getElementById("campaign-screen").hidden');await evaluate('new Promise(resolve=>setTimeout(resolve,350))');
      check(await evaluate('(()=>{const bar=document.querySelector("#campaign-screen>.menu-bar").getBoundingClientRect(),back=document.getElementById("campaign-back").getBoundingClientRect(),list=document.getElementById("campaign-battles").getBoundingClientRect();return Math.abs(bar.left)<.1&&Math.abs(bar.top)<.1&&Math.abs(bar.right-innerWidth)<.1&&Math.abs(back.left)<.1&&Math.abs(list.right-innerWidth)<.1;})()'),name+' campaign click navigation preserves actual screen edges');
      await screenshot(name+'-campaign');
      const started=await evaluate('(async()=>{try{await start("conquest1","fra");return {ok:true};}catch(e){return {error:e.stack||e.message||String(e),images:Array.from(images).filter(([n,i])=>!i.complete||!i.naturalWidth).map(([n,i])=>[n,i.complete,i.naturalWidth,i.src])};}})()');
      assert.ok(started.ok,JSON.stringify(started));
      await until('typeof ctx!=="undefined"&&ctx&&battlefield.state===state&&Array.from(images.values()).every(i=>i.complete)&&!document.getElementById("loading-screen").hidden===false');
      await until('(()=>{if(ctx.gpu)ctx.end();const p=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;let colors=new Set();for(let i=0;i<p.length;i+=64)colors.add(p[i]+","+p[i+1]+","+p[i+2]);return colors.size>50;})()');
      check(true,name+' nonblank battlefield pixels');
      check(await evaluate('!document.getElementById("play").hidden'),name+' battle visible');
      if(query||injection)check(await evaluate('!ctx.gpu'),name+' Canvas 2D fallback');
      await screenshot(name+'-battle');
      const before=await evaluate('view.zoom');await send('Input.dispatchMouseEvent',{type:'mouseWheel',x:width/2,y:height/2,deltaX:0,deltaY:-100});
      check(await evaluate('view.zoom')!==before,name+' wheel input');
      const center=await evaluate('({x:view.x,y:view.y})');
      await send('Emulation.setTouchEmulationEnabled',{enabled:true});
      await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:width/2,y:height/2}]});
      await send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:width/2+40,y:height/2+10}]});
      await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
      check(await evaluate(`view.x!==${center.x}||view.y!==${center.y}`),name+' touch drag');
      await send('Emulation.setTouchEmulationEnabled',{enabled:false});
      await until('performance.now()-tutorialTouchTime>350');
      check(await evaluate('(async()=>{const before=JSON.stringify(state);await rpc("save",{mode:2,slot:0});const restored=await rpc("load",{mode:2,slot:0});return restored.scenario===state.scenario&&restored.round===state.round&&JSON.stringify(restored.units)===JSON.stringify(state.units);})()'),name+' save/load');
      await click('#menu-button');check(await evaluate('!document.getElementById("pause").hidden'),name+' pause button');
      await until('!document.getElementById("pause-objectives").hidden&&document.querySelector(".pause-objective-row")');
      await evaluate('new Promise(resolve=>setTimeout(resolve,350))');
      check(await evaluate('(()=>{const m=document.querySelector("#pause>.pause-dialog").getBoundingClientRect(),p=document.getElementById("pause-objectives").getBoundingClientRect(),s=document.querySelector(".pause-objectives-scroll");return Math.abs(m.left+m.width/2-innerWidth/2)<1&&Math.abs(m.top+m.height/2-innerHeight/2)<1&&p.left===0&&(p.right<=m.left||p.bottom<=m.top)&&s.clientHeight>35;})()'),name+' centered pause and scrolling objectives avoid overlap');
      await screenshot(name+'-pause');
      if(injection)await send('Page.removeScriptToEvaluateOnNewDocument',{identifier:injection});
    }
    check(errors.length===0,'zero uncaught browser errors');
  }finally{
    await fs.writeFile(path.join(folder,'results.json'),JSON.stringify({version,checks:checks.length,passed:checks,errors},null,2));
    socket?.close();child.kill();
    console.log(JSON.stringify({checks:checks.length,errors}));
  }
})().catch(error=>{console.error(error.message);process.exitCode=1;});
