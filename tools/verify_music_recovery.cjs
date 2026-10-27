'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('rewrite/Ew4.Web/wwwroot/music.js','utf8');
const flush=async()=>{for(let i=0;i<12;i++)await Promise.resolve();};
function fixture(){
  const events=new EventTarget(),timers=new Map(),urls=new Set();let next=0,fetches=0;
  class Audio extends EventTarget {
    constructor(){super();this.paused=true;this.volume=1;this.plays=0;}
    play(){this.plays++;this.paused=false;const pending=this.playResult?.()||Promise.resolve();pending.catch(()=>{this.paused=true;});return pending;}
    pause(){this.paused=true;}
    load(){this.loads=(this.loads||0)+1;}
    removeAttribute(name){delete this[name];}
    getAttribute(name){return this[name]||null;}
  }
  const context={Audio,document:events,navigator:{userActivation:{hasBeenActive:false}},console:{warn(){}},
    setTimeout(fn,delay){const id=++next;timers.set(id,{fn,delay});return id;},clearTimeout(id){timers.delete(id);},
    AbortController,Blob,Date,URL:{createObjectURL(){const url='blob:test-'+next++;urls.add(url);return url;},revokeObjectURL(url){urls.delete(url);}},
    fetch:async()=>{fetches++;return {ok:true,blob:async()=>new Blob(['mp3'],{type:'audio/mpeg'})};}};
  vm.runInNewContext(source,context);
  const player=new context.Ew4Music.Player({menu:'battle1.mp3',battle:['battle2.mp3'],defeat:'defeat_music.mp3'},()=>50);
  return {player,context,events,timers,urls,fetches:()=>fetches,
    gesture:()=>events.dispatchEvent(new Event('click')),
    runTimer:async()=>{const [id,timer]=timers.entries().next().value||[];assert(timer,'expected recovery timer');timers.delete(id);timer.fn();await flush();}};
}
(async()=>{
  let checks=0;const check=(value,label)=>{assert(value,label);checks++;};
  {
    const f=fixture();f.player.menu();f.gesture();await flush();
    f.player.audio.error={code:2,message:'intercepted media failed'};f.player.audio.dispatchEvent(new Event('error'));
    await f.runTimer();await flush();
    check(f.player.audio.src.startsWith('blob:')&&!f.player.failed,'media failure recovers through fetched blob');
    check(f.fetches()===1,'one complete request for fallback audio');
    f.player.dispose();check(f.urls.size===0&&f.timers.size===0,'dispose releases blob and timers');
  }
  {
    const f=fixture();f.player.menu();f.gesture();await flush();
    let resolve;f.context.fetch=()=>new Promise(r=>{resolve=r;});
    f.player.audio.error={code:2};f.player.audio.dispatchEvent(new Event('error'));await f.runTimer();
    f.player.sync({scenario:'new'},true);
    resolve({ok:true,blob:async()=>new Blob(['old'])});await flush();
    check(f.player.file==='battle2.mp3'&&f.player.audio.src.endsWith('battle2.mp3'),'late fallback cannot replace a newer track');
    check(f.urls.size===0,'stale load does not leak a blob');f.player.dispose();
  }
  {
    const f=fixture();f.player.menu();f.player.audio.playResult=()=>Promise.reject(Object.assign(new Error('activation required'),{name:'NotAllowedError'}));
    f.gesture();await flush();check(!f.player.failed,'gesture rejection is recoverable');
    f.player.audio.playResult=()=>Promise.resolve();f.gesture();await flush();check(f.player.audio.plays===2,'next gesture retries rejected playback');f.player.dispose();
  }
  {
    const f=fixture();f.player.menu();let reject;
    f.player.audio.playResult=()=>new Promise((_,r)=>{reject=r;});f.gesture();
    const rejectOld=reject;f.player.sync({scenario:'new'},true);rejectOld(Object.assign(new Error('old source'),{name:'NotSupportedError'}));await flush();
    check(!f.player.failed,'old play rejection cannot fail the new source');f.player.dispose();
  }
  {
    const f=fixture();f.player.menu();let reject;
    f.player.audio.playResult=()=>new Promise((_,r)=>{reject=r;});f.gesture();
    f.player.setVisible(false);f.player.setVisible(true);
    f.player.audio.playResult=()=>Promise.resolve();reject(Object.assign(new Error('paused'),{name:'AbortError'}));await flush();
    check(f.player.audio.plays===2&&!f.player.audio.paused,'foreground retries after the interrupted play settles');f.player.dispose();
  }
  {
    const f=fixture();f.player.menu();f.player.audio.playResult=()=>new Promise(()=>{});f.gesture();
    await f.runTimer();check(f.player.failed&&f.player.lastError.name==='TimeoutError','hung playback enters bounded recovery');
    f.player.dispose();check(f.timers.size===0,'hung playback leaves no timer after dispose');
  }
  {
    const f=fixture();f.player.menu();f.context.fetch=()=>new Promise(()=>{});f.gesture();await flush();
    f.player.audio.error={code:2};f.player.audio.dispatchEvent(new Event('error'));await f.runTimer();
    f.player.setVisible(false);await f.runTimer();
    check(f.player.failed&&f.player.lastError.name==='TimeoutError','background does not remove a stalled fetch deadline');f.player.dispose();
  }
  {
    const f=fixture();f.player.menu();f.gesture();await flush();
    f.player.audio.error={code:2};f.player.audio.dispatchEvent(new Event('error'));f.player.setVisible(false);
    await f.runTimer();check(f.player.failed&&!f.player.loading,'background does not load replacement audio');
    f.player.setVisible(true);await flush();check(!f.player.failed&&f.player.audio.src.startsWith('blob:'),'foreground recovers deferred failure');f.player.dispose();
  }
  {
    const f=fixture();f.player.menu();let resolve;
    f.context.fetch=()=>new Promise(r=>{resolve=r;});f.gesture();await flush();
    f.player.audio.error={code:2};f.player.audio.dispatchEvent(new Event('error'));await f.runTimer();f.player.dispose();
    resolve({ok:true,blob:async()=>new Blob(['late'])});await flush();
    check(f.urls.size===0&&!f.player.audio.src&&f.timers.size===0,'dispose ignores a late fallback response');
  }
  {
    const f=fixture();f.player.menu();let requests=0;f.context.fetch=async()=>{requests++;return {ok:false,status:503};};f.gesture();await flush();
    f.player.audio.error={code:2};f.player.audio.dispatchEvent(new Event('error'));
    for(let i=0;i<8&&f.timers.size;i++)await f.runTimer();
    check(f.player.failed&&f.timers.size===0&&requests===2,'persistent failure stops automatic retries');
    f.context.fetch=async()=>({ok:true,blob:async()=>new Blob(['repaired'])});
    f.player.lastRetry=0;f.gesture();await flush();
    check(!f.player.failed&&f.player.audio.src.startsWith('blob:'),'user can recover after retries are exhausted');f.player.dispose();
  }
  console.log(JSON.stringify({checks,errors:0}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
